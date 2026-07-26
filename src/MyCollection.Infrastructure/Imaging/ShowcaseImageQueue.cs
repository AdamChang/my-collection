using System.Threading.Channels;
using MongoDB.Bson;
using MyCollection.Application.Showcase;

namespace MyCollection.Infrastructure.Imaging;

public sealed class ShowcaseImageQueue : IShowcaseImageQueue
{
    private readonly Channel<ObjectId> _channel = Channel.CreateUnbounded<ObjectId>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(ObjectId itemId) => _channel.Writer.TryWrite(itemId);

    public ValueTask<ObjectId> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
