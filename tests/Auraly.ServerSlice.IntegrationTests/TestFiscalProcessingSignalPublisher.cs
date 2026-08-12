using System.Collections.Concurrent;
using Auraly.Application.Fiscal;

namespace Auraly.ServerSlice.IntegrationTests;

internal sealed record PublishedFiscalSignal(
    FiscalProcessingSignal Signal,
    DateTimeOffset? ScheduledEnqueueTime);

internal sealed class TestFiscalProcessingSignalPublisher
    : IFiscalProcessingSignalPublisher
{
    private readonly ConcurrentQueue<PublishedFiscalSignal> signals = new();

    public Task PublishAsync(
        FiscalProcessingSignal signal,
        DateTimeOffset? scheduledEnqueueTime = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        signals.Enqueue(new PublishedFiscalSignal(signal, scheduledEnqueueTime));
        return Task.CompletedTask;
    }

    public IReadOnlyCollection<PublishedFiscalSignal> Drain()
    {
        var values = new List<PublishedFiscalSignal>();
        while (signals.TryDequeue(out var value)) values.Add(value);
        return values;
    }
}
