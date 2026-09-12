using System.Collections.Concurrent;
using System.Threading.Channels;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

// The browser only receives a local invalidation. It must obtain every
// authoritative value through the regular authenticated local API.
public sealed class PosUiStateSignal : IPosSynchronizationProgressSink
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> subscribers = new();

    public (Guid SubscriptionId, ChannelReader<string> Reader) Subscribe()
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
            FullMode = BoundedChannelFullMode.DropWrite
        });
        subscribers[subscriptionId] = channel;
        return (subscriptionId, channel.Reader);
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        if (subscribers.TryRemove(subscriptionId, out var channel))
            channel.Writer.TryComplete();
    }

    public void Publish()
    {
        foreach (var channel in subscribers.Values)
            channel.Writer.TryWrite("state");
    }
}
