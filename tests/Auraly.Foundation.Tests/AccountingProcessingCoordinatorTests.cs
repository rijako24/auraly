using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Application;

namespace Auraly.Foundation.Tests;

public sealed class AccountingProcessingCoordinatorTests
{
    [Fact]
    public async Task Does_not_publish_when_no_accounting_job_exists()
    {
        var publisher = new RecordingPublisher();
        var coordinator = new AccountingProcessingCoordinator(
            publisher, new FixedIds(), new FixedGate(false));

        await coordinator.RequestPostingAsync(
            Guid.NewGuid(), Guid.NewGuid(), "SalesInvoice");

        Assert.Empty(publisher.Signals);
    }

    [Fact]
    public async Task Publishes_once_when_ready_job_exists()
    {
        var publisher = new RecordingPublisher();
        var coordinator = new AccountingProcessingCoordinator(
            publisher, new FixedIds(), new FixedGate(true));

        await coordinator.RequestPostingAsync(
            Guid.NewGuid(), Guid.NewGuid(), "SalesInvoice");

        Assert.Single(publisher.Signals);
    }

    [Fact]
    public async Task Retry_reuses_the_signal_id_and_only_targets_the_requested_attempt()
    {
        var business = Guid.NewGuid();
        var document = Guid.NewGuid();
        var publisher = new RecordingPublisher();
        var gate = new FixedGate(true);
        var coordinator = new AccountingProcessingCoordinator(publisher, new FixedIds(), gate);

        Assert.True(await coordinator.RequestRetryAsync(business, document, "SalesInvoice", 0));
        Assert.True(await coordinator.RequestRetryAsync(business, document, "SalesInvoice", 0));
        gate.AttemptCount = 1;
        Assert.False(await coordinator.RequestRetryAsync(business, document, "SalesInvoice", 0));
        Assert.True(await coordinator.RequestRetryAsync(business, document, "SalesInvoice", 1));
        Assert.False(gate.IncludedDependencies);
        Assert.Equal(3, publisher.Signals.Count);
        Assert.Equal(publisher.Signals[0], publisher.Signals[1]);
        Assert.NotEqual(publisher.Signals[0].SignalId, publisher.Signals[2].SignalId);
        Assert.All(publisher.Signals, signal => Assert.Equal(document, signal.DocumentId));
    }

    private sealed class FixedGate(bool result) : IAccountingProcessingSignalGate
    {
        public int AttemptCount { get; set; }
        public bool IncludedDependencies { get; private set; }

        public Task<IReadOnlyList<AccountingPendingWork>> ListPendingWorkAsync(
            Guid businessId, Guid documentId, string documentType,
            CancellationToken cancellationToken = default, bool includeDependencies = true)
        {
            IncludedDependencies = includeDependencies;
            return Task.FromResult<IReadOnlyList<AccountingPendingWork>>(
                result ? [new AccountingPendingWork(documentId, documentType, AttemptCount)] : []);
        }
    }

    private sealed class RecordingPublisher : IAccountingProcessingSignalPublisher
    {
        public List<AccountingProcessingSignal> Signals { get; } = [];

        public Task PublishAsync(
            AccountingProcessingSignal signal,
            CancellationToken cancellationToken = default)
        {
            Signals.Add(signal);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedIds : IAuralyIdGenerator
    {
        public Guid NewId() => Guid.Parse("0198d3b0-0000-7000-8000-000000000001");
    }
}
