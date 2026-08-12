using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Auraly.Pos.Edge.Host;

// The browser only receives a local invalidation. It must obtain every
// authoritative value through the regular authenticated local API.
public sealed class PosUiStateSignal
{
    private readonly ConcurrentDictionary<Guid, Channel<string>> subscribers = new();

    public (Guid SubscriptionId, ChannelReader<string> Reader) Subscribe()
    {
        var subscriptionId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
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