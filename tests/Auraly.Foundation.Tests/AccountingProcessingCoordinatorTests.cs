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

    private sealed class FixedGate(bool result) : IAccountingProcessingSignalGate
    {
        public Task<bool> HasPendingWorkAsync(
            Guid businessId, Guid documentId, string documentType,
            CancellationToken cancellationToken = default) => Task.FromResult(result);
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
