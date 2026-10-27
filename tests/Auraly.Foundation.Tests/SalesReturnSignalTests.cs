using Auraly.Application.DocumentProcessing;
using Auraly.Application.Returns;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Returns;

namespace Auraly.Foundation.Tests;

public sealed class SalesReturnSignalTests
{
    [Theory]
    [InlineData("Accepted", false, 1)]
    [InlineData("Accepted", true, 1)]
    [InlineData("Processed", true, 0)]
    public async Task Only_an_unprocessed_acceptance_needs_another_document_signal(string status, bool replay, int count)
    {
        var user = new SalesReturnUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { SalesReturnPermissionCodes.Create });
        var request = new ConfirmSalesReturnRequest(Guid.NewGuid(), user.BusinessId, Guid.NewGuid(), Guid.NewGuid(),
            new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero), ReturnEconomicResolutions.Refund,
            SalesReturnRefundMethods.Cash, "Reembolso de la devolución",
            [new(1, 1m, ReturnInventoryDispositions.Sellable)], ReasonCode: "Other");
        var accepted = new SalesReturnAcceptance(request.ReturnId, Guid.NewGuid(), "DVT00-00000001", status, 123, replay);
        var store = new Store(accepted);
        var publisher = new Publisher();
        Assert.Equal(accepted, await new SalesReturnService(store, publisher).ConfirmAsync(user, "same-command", request));
        Assert.Equal(1, store.AcceptCalls);
        Assert.Equal(count, publisher.Signals.Count);
        if (count > 0)
        {
            var signal = Assert.Single(publisher.Signals);
            Assert.Equal(accepted.MovementId, signal.MovementId);
            Assert.Equal(request.ReturnId, signal.DocumentId);
            Assert.Equal(user.BusinessId, signal.BusinessId);
            Assert.Equal(SalesReturnDocumentTypes.SalesReturn, signal.DocumentType);
        }
    }

    private sealed class Store(SalesReturnAcceptance result) : ISalesReturnStore
    {
        public int AcceptCalls { get; private set; }
        public Task<SalesReturnAcceptance> AcceptAsync(SalesReturnUserIdentity user, string key,
            ConfirmSalesReturnRequest request, CancellationToken token)
        {
            AcceptCalls++;
            return Task.FromResult(result);
        }
    }

    private sealed class Publisher : IDocumentProcessingSignalPublisher
    {
        public List<DocumentProcessingSignal> Signals { get; } = [];
        public Task PublishAsync(DocumentProcessingSignal signal, CancellationToken cancellationToken)
        {
            Signals.Add(signal);
            return Task.CompletedTask;
        }
    }
}
