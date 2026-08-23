using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Purchasing;

namespace Auraly.Application.Purchasing;

public interface IPurchaseReturnStore
{
    Task<ReturnableGoodsReceiptPage> ListReturnableReceiptsAsync(
        PurchasingUserIdentity user, string? search, DateOnly? from, DateOnly? to,
        bool? withAvailableQuantity, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<ReturnableGoodsReceipt?> GetReturnableReceiptAsync(
        PurchasingUserIdentity user, Guid goodsReceiptId,
        CancellationToken cancellationToken);
    Task<PurchaseReturnAcceptance> AcceptAsync(
        PurchasingUserIdentity user, string idempotencyKey,
        ConfirmPurchaseReturnRequest request, CancellationToken cancellationToken);
}

public sealed class PurchaseReturnService(
    IPurchaseReturnStore store,
    IDocumentProcessingSignalPublisher signalPublisher)
{
    public Task<ReturnableGoodsReceiptPage> ListReturnableReceiptsAsync(
        PurchasingUserIdentity user, string? search, DateOnly? from, DateOnly? to,
        bool? withAvailableQuantity, int page, int pageSize,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadPurchaseReturns);
        if (page < 1) throw new PurchasingValidationException("Page must be greater than zero.");
        if (pageSize is < 1 or > 100)
            throw new PurchasingValidationException("PageSize must be between 1 and 100.");
        return store.ListReturnableReceiptsAsync(
            user, Normalize(search, 160), from, to, withAvailableQuantity,
            page, pageSize, cancellationToken);
    }

    public Task<ReturnableGoodsReceipt?> GetReturnableReceiptAsync(
        PurchasingUserIdentity user, Guid goodsReceiptId,
        CancellationToken cancellationToken = default)
    {
        Require(user, PurchasingPermissionCodes.ReadPurchaseReturns);
        if (goodsReceiptId == Guid.Empty)
            throw new PurchasingValidationException("GoodsReceiptId is required.");
        return store.GetReturnableReceiptAsync(user, goodsReceiptId, cancellationToken);
    }

    public async Task<PurchaseReturnAcceptance> ConfirmAsync(
        PurchasingUserIdentity user, string idempotencyKey,
        ConfirmPurchaseReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        if (user.BusinessId != request.BusinessId)
            throw new PurchasingForbiddenException("The purchase return belongs to another business.");
        Require(user, PurchasingPermissionCodes.CreatePurchaseReturns);
        Require(user, PurchasingPermissionCodes.ConfirmPurchaseReturns);
        if (request.ReturnId == Guid.Empty)
            throw new PurchasingValidationException("ReturnId is required.");
        if (request.OriginalGoodsReceiptId == Guid.Empty)
            throw new PurchasingValidationException("OriginalGoodsReceiptId is required.");
        if (request.ReturnedAt == default)
            throw new PurchasingValidationException("ReturnedAt is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new PurchasingValidationException("A valid Idempotency-Key is required.");
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || request.ReasonCode.Trim().Length > 40)
            throw new PurchasingValidationException("ReasonCode is required.");
        if (request.Lines.Count is < 1 or > 200)
            throw new PurchasingValidationException("A return requires between 1 and 200 lines.");
        if (request.Lines.Any(line => line.OriginalLineNumber <= 0 || line.Quantity <= 0))
            throw new PurchasingValidationException("Every return line requires a valid original line and quantity.");
        if (request.Lines.Select(line => line.OriginalLineNumber).Distinct().Count() != request.Lines.Count)
            throw new PurchasingValidationException("An original receipt line can only appear once.");

        var normalized = request with { Notes = Normalize(request.Notes, 1000) };
        var acceptance = await store.AcceptAsync(
            user, idempotencyKey.Trim(), normalized, cancellationToken);
        await signalPublisher.PublishAsync(new DocumentProcessingSignal(
            acceptance.MovementId, request.BusinessId, request.ReturnId,
            PurchasingDocumentTypes.PurchaseReturn), cancellationToken);
        return acceptance;
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new PurchasingValidationException($"The value exceeds {maximumLength} characters.");
        return normalized;
    }

    private static void Require(PurchasingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PurchasingForbiddenException($"Permission '{permission}' is required.");
    }
}
