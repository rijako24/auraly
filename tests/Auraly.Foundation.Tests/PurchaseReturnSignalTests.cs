using Auraly.Application.DocumentProcessing;
using Auraly.Application.Purchasing;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Purchasing;

namespace Auraly.Foundation.Tests;

public sealed class PurchaseReturnSignalTests
{
    [Theory]
    [InlineData("Accepted", false, 1)]
    [InlineData("Accepted", true, 1)]
    [InlineData("Processed", true, 0)]
    public async Task Completed_replay_returns_the_authoritative_result_without_another_signal(
        string status, bool replay, int signalCount)
    {
        var user = new PurchasingUserIdentity(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new HashSet<string> { PurchasingPermissionCodes.CreatePurchaseReturns, PurchasingPermissionCodes.ConfirmPurchaseReturns });
        var request = new ConfirmPurchaseReturnRequest(Guid.NewGuid(), user.BusinessId, Guid.NewGuid(),
            new(2026, 8, 2, 9, 0, 0, TimeSpan.Zero), "QualityIssue", null, [new(1, 1m)]);
        var accepted = new PurchaseReturnAcceptance(request.ReturnId, Guid.NewGuid(), "DCP00-00000001", status, 123, replay);
        var store = new Store(accepted);
        var publisher = new Publisher();
        var result = await new PurchaseReturnService(store, publisher).ConfirmAsync(user, "same-command", request);
        Assert.Equal(accepted, result);
        Assert.Equal(1, store.AcceptCalls);
        Assert.Equal(signalCount, publisher.Signals.Count);
        if (signalCount > 0)
        {
            var signal = Assert.Single(publisher.Signals);
            Assert.Equal(accepted.MovementId, signal.MovementId);
            Assert.Equal(request.ReturnId, signal.DocumentId);
            Assert.Equal(user.BusinessId, signal.BusinessId);
            Assert.Equal(PurchasingDocumentTypes.PurchaseReturn, signal.DocumentType);
        }
    }

    private sealed class Store(PurchaseReturnAcceptance result) : IPurchaseReturnStore
    {
        public int AcceptCalls { get; private set; }
        public Task<PurchaseReturnAcceptance> AcceptAsync(PurchasingUserIdentity user, string key,
            ConfirmPurchaseReturnRequest request, CancellationToken token)
        {
            AcceptCalls++;
            return Task.FromResult(result);
        }
        public Task<ReturnableGoodsReceiptPage> ListReturnableReceiptsAsync(PurchasingUserIdentity user, string? search,
            DateOnly? from, DateOnly? to, bool? available, int page, int pageSize, CancellationToken token) =>
            throw new NotSupportedException();
        public Task<ReturnableGoodsReceipt?> GetReturnableReceiptAsync(PurchasingUserIdentity user, Guid id, CancellationToken token) =>
            throw new NotSupportedException();
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
