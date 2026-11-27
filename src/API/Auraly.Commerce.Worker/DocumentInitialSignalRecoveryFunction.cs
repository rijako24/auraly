using Auraly.Platform.Infrastructure.Processing;
using Auraly.Infrastructure.Persistence;
using Microsoft.Azure.Functions.Worker;

namespace Auraly.Commerce.Worker;

public sealed class DocumentInitialSignalRecoveryFunction(
    ServiceBusDocumentProcessingPublisher publisher,
    SqlDispatchSettlementActivation dispatches)
{
    [Function("RecoverDocumentInitialSignals")]
    public async Task RunAsync(
        [TimerTrigger("%DocumentInitialSignalRecoverySchedule%", UseMonitor = false)]
        TimerInfo timer,
        CancellationToken cancellationToken)
    {
        await publisher.RecoverInitialSignalsAsync(cancellationToken);
        await publisher.RecoverContinuationSignalsAsync(cancellationToken);
        await dispatches.RecoverAsync(cancellationToken);
    }
}
