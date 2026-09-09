using Auraly.Api;
using Auraly.Application.DocumentProcessing;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed class ServiceBusDocumentProcessingPublisherTests
{
    [Fact]
    public async Task Publishing_accepts_the_committed_signal_without_waiting_for_the_network_sender()
    {
        var sender = new BlockingServiceBusSender();
        var publisher = new ServiceBusDocumentProcessingPublisher(
            sender,
            NullLogger<ServiceBusDocumentProcessingPublisher>.Instance);
        var signal = new DocumentProcessingSignal(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "GoodsReceipt");

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        await publisher.PublishAsync(signal);
        elapsed.Stop();

        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(3),
            $"The durable request path waited {elapsed.Elapsed} before returning.");
        Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(1.5),
            $"The blocking sender was not held to the configured budget: {elapsed.Elapsed}.");
        Assert.Equal(1, sender.Calls);
    }

    private sealed class BlockingServiceBusSender : ServiceBusSender
    {
        public int Calls { get; private set; }

        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
