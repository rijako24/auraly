using Auraly.BuildingBlocks.Domain.Identifiers;

namespace Auraly.Application.Fiscal;

public enum FiscalProcessingStage
{
    Generation,
    Submission
}

public sealed record FiscalProcessingSignal(
    Guid SignalId,
    Guid BusinessId,
    Guid DocumentId,
    FiscalProcessingStage Stage);

public interface IFiscalProcessingSignalPublisher
{
    Task PublishAsync(
        FiscalProcessingSignal signal,
        DateTimeOffset? scheduledEnqueueTime = null,
        CancellationToken cancellationToken = default);
}

public sealed class FiscalProcessingCoordinator(
    IFiscalProcessingSignalPublisher publisher,
    IAuralyIdGenerator ids)
{
    public Task RequestGenerationAsync(
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            businessId,
            documentId,
            FiscalProcessingStage.Generation,
            null,
            cancellationToken);

    public Task ScheduleGenerationAsync(
        Guid businessId,
        Guid documentId,
        DateTimeOffset scheduledEnqueueTime,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            businessId,
            documentId,
            FiscalProcessingStage.Generation,
            scheduledEnqueueTime,
            cancellationToken);

    public Task RequestSubmissionAsync(
        Guid businessId,
        Guid documentId,
        DateTimeOffset? scheduledEnqueueTime = null,
        CancellationToken cancellationToken = default) =>
        PublishAsync(
            businessId,
            documentId,
            FiscalProcessingStage.Submission,
            scheduledEnqueueTime,
            cancellationToken);

    private Task PublishAsync(
        Guid businessId,
        Guid documentId,
        FiscalProcessingStage stage,
        DateTimeOffset? scheduledEnqueueTime,
        CancellationToken cancellationToken)
    {
        if (businessId == Guid.Empty || documentId == Guid.Empty)
            throw new ArgumentException("Business and document identifiers are required.");

        return publisher.PublishAsync(
            new FiscalProcessingSignal(
                ids.NewId(),
                businessId,
                documentId,
                stage),
            scheduledEnqueueTime,
            cancellationToken);
    }
}
