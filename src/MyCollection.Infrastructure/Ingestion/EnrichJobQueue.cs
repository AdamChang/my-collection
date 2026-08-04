using System.Threading.Channels;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Ingestion;

public sealed class EnrichJobQueue : IEnrichJobQueue
{
    private readonly Channel<EnrichJobRequest> _channel = Channel.CreateUnbounded<EnrichJobRequest>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(EnrichJobRequest request) => _channel.Writer.TryWrite(request);

    public ValueTask<EnrichJobRequest> DequeueAsync(CancellationToken ct) =>
        _channel.Reader.ReadAsync(ct);
}
