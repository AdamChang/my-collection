using System.Threading.Channels;
using MongoDB.Bson;
using MyCollection.Application.Ingestion;

namespace MyCollection.Infrastructure.Ingestion;

public sealed class InProcessIngestionTaskDispatcher : IIngestionTaskDispatcher
{
    private readonly Channel<ObjectId> _channel = Channel.CreateUnbounded<ObjectId>(
        new UnboundedChannelOptions { SingleReader = true });

    public bool IsDurable => false;

    public async Task DispatchAsync(ObjectId operationId, CancellationToken ct) =>
        await _channel.Writer.WriteAsync(operationId, ct);

    internal ValueTask<ObjectId> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
