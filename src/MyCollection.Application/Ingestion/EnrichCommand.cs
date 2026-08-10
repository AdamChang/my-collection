using FluentValidation;
using MediatR;
using MongoDB.Bson;
using MyCollection.Application.Common;
using MyCollection.Domain.Entities;

namespace MyCollection.Application.Ingestion;

/// <summary>
/// 補完既有品項的 provider 欄位。給 ItemIds 是單筆／重抓，不給是批次補完尚未處理的品項。
/// </summary>
public record EnrichCommand(
    string Provider,
    IReadOnlyList<string>? ItemIds = null,
    int Limit = 50) : IRequest<SyncJobDto>;

public sealed class EnrichCommandValidator : AbstractValidator<EnrichCommand>
{
    public EnrichCommandValidator()
    {
        RuleFor(x => x.Provider).NotEmpty();

        RuleForEach(x => x.ItemIds)
            .Must(id => ObjectId.TryParse(id, out _))
            .WithMessage("ItemIds must contain valid object ids.");
    }
}

/// <summary>
/// 只負責建立作業並決定在哪裡執行；實際工作在 <see cref="EnrichJobRunner"/>。
///
/// 兩種回應語意是刻意的取捨：反查成本低的 provider（IGDB 可批次、每秒四次）
/// 在請求內跑完，回應時作業已結束；成本高的（Steam 商店逐款查、五分鐘兩百次）
/// 回應時才剛開始，進度要靠 /ingest/jobs 輪詢。統一兩者列為待清理。
/// </summary>
public sealed class EnrichCommandHandler(
    ProviderRegistry registry,
    ISyncJobRepository jobs,
    IIngestionTaskDispatcher dispatcher,
    EnrichJobRunner runner,
    TimeProvider timeProvider) : IRequestHandler<EnrichCommand, SyncJobDto>
{
    public async Task<SyncJobDto> Handle(EnrichCommand request, CancellationToken cancellationToken)
    {
        var provider = registry.Require<IExternalIdLookupProvider>(request.Provider);

        var job = new SyncJob
        {
            Id = ObjectId.GenerateNewId(),
            Provider = provider.Key,
            Kind = SyncJobKind.Enrich,
            ItemIds = request.ItemIds?.ToList(),
            Limit = Math.Clamp(request.Limit, 1, 200),
            Status = SyncStatus.Running,
            StartedAt = timeProvider.GetUtcNow().UtcDateTime
        };
        await jobs.InsertAsync(job, cancellationToken);

        if (dispatcher.IsDurable || provider.PrefersBackgroundExecution)
        {
            await IngestionTaskDispatch.PersistedAsync(
                dispatcher, jobs, job, timeProvider, cancellationToken);
            return SyncJobMapper.ToDto(job);
        }

        var finished = await runner.RunAsync(
            job, provider, job.ItemIds, job.Limit, cancellationToken);

        return SyncJobMapper.ToDto(finished);
    }
}
