using MediatR;

namespace MyCollection.Application.Transfer;

/// <summary>
/// 直接寫進呼叫端提供的 Stream。端點傳 HttpResponse.Body，
/// 因此整個匯出過程不落暫存檔、不整包進記憶體。
/// </summary>
public record ExportArchiveCommand(Stream Destination) : IRequest;

public sealed class ExportArchiveCommandHandler(ArchiveWriter writer) : IRequestHandler<ExportArchiveCommand>
{
    public Task Handle(ExportArchiveCommand request, CancellationToken cancellationToken) =>
        writer.WriteAsync(request.Destination, cancellationToken);
}
