using Auraly.Platform.Infrastructure.Processing;
using Auraly.Infrastructure.Persistence;
using Microsoft.Azure.Functions.Worker;

namespace Auraly.Commerce.Worker;

public sealed class DocumentInitialSignalRecoveryFunction(
    ServiceBusDocumentProcessingPublisher publisher,
    SqlDispatchSettlementActivation? dispatches = null)
{
    [Function("RecoverDocumentInitialSignals")]
    public async Task RunAsync(
        [TimerTrigger("%DocumentInitialSignalRecoverySchedule%", UseMonitor = false)]
        TimerInfo timer,
        CancellationToken cancellationToken)
    {
        await publisher.RecoverInitialSignalsAsync(cancellationToken);
        await publisher.RecoverContinuationSignalsAsync(cancellationToken);
        if (dispatches is not null)
            await dispatches.RecoverAsync(cancellationToken);
    }
}
