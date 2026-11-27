using System.Threading.Channels;
using Auraly.Application.Dispatching;
using Auraly.Application.DocumentProcessing;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Dispatching;
using Auraly.Contracts.Returns;
using Auraly.Infrastructure.Persistence;

namespace Auraly.Api;

public sealed class DispatchSettlementCoordinator
{
    private static readonly TimeSpan MinimumLeaseDelay = TimeSpan.FromMilliseconds(50);
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });

    public DispatchSettlementCoordinator() => Signal();
    public void Signal() => signals.Writer.TryWrite(true);
    public ValueTask<bool> WaitAsync(CancellationToken token) => signals.Reader.ReadAsync(token);
    public async Task WaitAsync(TimeSpan? retryAfter,TimeProvider timeProvider,CancellationToken token)
    {
        if(retryAfter is null) { await WaitAsync(token); return; }
        // A zero/sub-millisecond deadline can complete immediately and spin SQL
        // claims just before a lease expires. Signals and shutdown still wake it.
        var delay=retryAfter.Value<MinimumLeaseDelay?MinimumLeaseDelay:retryAfter.Value;
        using var deadline=new CancellationTokenSource(delay,timeProvider);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,deadline.Token);
        try { await signals.Reader.ReadAsync(linked.Token); }
        catch(OperationCanceledException) when(!token.IsCancellationRequested&&deadline.IsCancellationRequested) { }
    }
}

public sealed class DispatchSettlementHostedService(
    IServiceScopeFactory scopes,
    DispatchSettlementCoordinator coordinator,
    TimeProvider timeProvider,
    ILogger<DispatchSettlementHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan? retryAfter=null;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await coordinator.WaitAsync(retryAfter,timeProvider,stoppingToken);
                retryAfter=null;
                while (true)
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var store = scope.ServiceProvider.GetRequiredService<SqlDispatchSettlementPreparationStore>();
                    var claim=await store.ClaimLegacyAsync(stoppingToken);
                    if(claim.Work is not { } operation) { retryAfter=claim.RetryAfter; break; }
                    await ProcessLegacyAsync(scope.ServiceProvider, store, operation, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception)
            {
                logger.LogError(exception, "Dispatch settlement worker loop failed.");
                // The durable work remains pending. A new explicit request or
                // host restart wakes it; a queue failure must not hot-loop SQL.
            }
        }
    }

    private async Task ProcessLegacyAsync(IServiceProvider services, SqlDispatchSettlementPreparationStore store,
        DispatchSettlementWork operation, CancellationToken token)
    {
        try
        {
            var preparation = services.GetRequiredService<DispatchSettlementPreparation>();
            var worker = services.GetRequiredService<DocumentProcessingWorker>();
            foreach (var item in await store.LoadReturnsAsync(operation, token))
            {
                var accepted = await preparation.AcceptReturnAsync(operation, item, token);
                await worker.ProcessOneAsync(new DocumentProcessingSignal(
                    accepted.MovementId, operation.BusinessId, accepted.ReturnId, SalesReturnDocumentTypes.SalesReturn), token);
            }
            foreach (var item in await store.LoadPaymentsAsync(operation, token))
                await preparation.AcceptPaymentAsync(operation, item, token);
            var work = await store.EnsureCashDifferenceDocumentAsync(operation, token);
            await worker.ProcessOneAsync(work.Signal, token);
            if (work.Replay)
                await services.GetRequiredService<AccountingProcessingCoordinator>().RequestPostingAsync(
                    operation.BusinessId, operation.SettlementId, DispatchAccountingDocumentTypes.CashDifference, token);
        }
        catch (Exception exception) when (!token.IsCancellationRequested)
        {
            logger.LogError(exception, "Settlement operation {OperationId} failed for dispatch {DispatchId}.", operation.Id, operation.DispatchId);
            var attention = operation.Attempts >= 3;
            await store.RescheduleLegacyAsync(operation, exception.Message, attention, token);
            if (!attention) coordinator.Signal();
        }
    }
}
