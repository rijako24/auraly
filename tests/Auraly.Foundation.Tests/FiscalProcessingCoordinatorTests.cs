using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Foundation.Tests;

public sealed class FiscalProcessingCoordinatorTests
{
    [Fact]
    public async Task Generation_signal_has_a_unique_identity_and_business_session_scope()
    {
        var publisher = new RecordingPublisher();
        var signalId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var coordinator = new FiscalProcessingCoordinator(
            publisher,
            new FixedIdGenerator(signalId));

        await coordinator.RequestGenerationAsync(businessId, documentId);

        var published = Assert.Single(publisher.Values);
        Assert.Equal(signalId, published.Signal.SignalId);
        Assert.Equal(businessId, published.Signal.BusinessId);
        Assert.Equal(documentId, published.Signal.DocumentId);
        Assert.Equal(FiscalProcessingStage.Generation, published.Signal.Stage);
        Assert.Null(published.ScheduledAt);
    }

    [Fact]
    public async Task Recovery_and_DIAN_rechecks_preserve_the_exact_document_and_schedule()
    {
        var publisher = new RecordingPublisher();
        var businessId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var resumeAt = new DateTimeOffset(2026, 8, 1, 15, 30, 0, TimeSpan.Zero);
        var coordinator = new FiscalProcessingCoordinator(
            publisher,
            new FixedIdGenerator(Guid.NewGuid()));

        await coordinator.ScheduleGenerationAsync(businessId, documentId, resumeAt);
        await coordinator.RequestSubmissionAsync(businessId, documentId, resumeAt);

        Assert.Collection(
            publisher.Values,
            generation =>
            {
                Assert.Equal(FiscalProcessingStage.Generation, generation.Signal.Stage);
                Assert.Equal(documentId, generation.Signal.DocumentId);
                Assert.Equal(resumeAt, generation.ScheduledAt);
            },
            submission =>
            {
                Assert.Equal(FiscalProcessingStage.Submission, submission.Signal.Stage);
                Assert.Equal(documentId, submission.Signal.DocumentId);
                Assert.Equal(resumeAt, submission.ScheduledAt);
            });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_business_or_document_is_rejected_before_publication(bool emptyBusiness)
    {
        var publisher = new RecordingPublisher();
        var coordinator = new FiscalProcessingCoordinator(
            publisher,
            new FixedIdGenerator(Guid.NewGuid()));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.RequestGenerationAsync(
                emptyBusiness ? Guid.Empty : Guid.NewGuid(),
                emptyBusiness ? Guid.NewGuid() : Guid.Empty));
        Assert.Empty(publisher.Values);
    }

    private sealed class RecordingPublisher : IFiscalProcessingSignalPublisher
    {
        public List<PublishedSignal> Values { get; } = [];

        public Task PublishAsync(
            FiscalProcessingSignal signal,
            DateTimeOffset? scheduledEnqueueTime = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Values.Add(new PublishedSignal(signal, scheduledEnqueueTime));
            return Task.CompletedTask;
        }
    }

    private sealed record PublishedSignal(
        FiscalProcessingSignal Signal,
        DateTimeOffset? ScheduledAt);

    private sealed class FixedIdGenerator(Guid value) : IAuralyIdGenerator
    {
        public Guid NewId() => value;
    }
}
