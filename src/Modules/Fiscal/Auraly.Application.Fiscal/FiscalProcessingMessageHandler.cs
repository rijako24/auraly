namespace Auraly.Application.Fiscal;

public sealed class FiscalProcessingMessageHandler(
    FiscalGenerationWorker generation,
    IFiscalGenerationWorkStore generationStore,
    FiscalSubmissionWorker submission,
    IFiscalSubmissionWorkStore submissionStore,
    FiscalProcessingCoordinator coordinator,
    TimeProvider clock)
{
    private static readonly TimeSpan WorkLease = TimeSpan.FromMinutes(2);

    public async Task ProcessAsync(
        FiscalProcessingSignal signal,
        string workerId,
        CancellationToken cancellationToken)
    {
        var (businessId, documentId) = (signal.BusinessId, signal.DocumentId);
        if (signal.Stage == FiscalProcessingStage.Generation)
        {
            if (await generation.ProcessAsync(businessId, documentId, workerId, cancellationToken))
            {
                await coordinator.RequestSubmissionAsync(businessId, documentId,
                    cancellationToken: cancellationToken);
                return;
            }

            var now = clock.GetUtcNow();
            var generationResume = await generationStore.GetResumeAtAsync(
                businessId, documentId, now, WorkLease, cancellationToken);
            if (generationResume is { } generationAt)
            {
                await coordinator.ScheduleGenerationAsync(
                    businessId, documentId, generationAt, cancellationToken);
                return;
            }

            // The previous delivery can have committed the signed artifact before
            // publishing its submission signal.
            var submissionResume = await submissionStore.GetResumeAtAsync(
                businessId, documentId, now, WorkLease, cancellationToken);
            if (submissionResume is { } submissionAt)
                await coordinator.RequestSubmissionAsync(
                    businessId, documentId, submissionAt, cancellationToken);
            return;
        }

        var result = await submission.ProcessAsync(businessId, documentId, workerId, cancellationToken);
        var nextAttempt = result.WorkFound
            ? result.NextAttemptAt
            : await submissionStore.GetResumeAtAsync(
                businessId, documentId, clock.GetUtcNow(), WorkLease, cancellationToken);
        if (nextAttempt is { } nextAttemptAt)
            await coordinator.RequestSubmissionAsync(
                businessId, documentId, nextAttemptAt, cancellationToken);
    }
}
