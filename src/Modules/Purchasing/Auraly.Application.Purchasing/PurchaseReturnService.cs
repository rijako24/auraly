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
        if (page < 1) throw new PurchasingValidationException("La página debe ser mayor que cero.");
        if (pageSize is < 1 or > 100)
            throw new PurchasingValidationException("La cantidad de registros por página debe estar entre 1 y 100.");
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
            throw new PurchasingValidationException("Se requiere una recepción de compra válida.");
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
            throw new PurchasingForbiddenException("La devolución de compra pertenece a otra sede.");
        Require(user, PurchasingPermissionCodes.CreatePurchaseReturns);
        Require(user, PurchasingPermissionCodes.ConfirmPurchaseReturns);
        if (request.ReturnId == Guid.Empty)
            throw new PurchasingValidationException("Se requiere una devolución válida.");
        if (request.OriginalGoodsReceiptId == Guid.Empty)
            throw new PurchasingValidationException("Selecciona la recepción de compra original.");
        if (request.ReturnedAt == default)
            throw new PurchasingValidationException("Indica la fecha de devolución.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new PurchasingValidationException("No se pudo identificar la operación. Vuelve a intentarlo con la misma solicitud.");
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || request.ReasonCode.Trim().Length > 40)
            throw new PurchasingValidationException("Selecciona un motivo.");
        if (request.Lines.Count is < 1 or > 200)
            throw new PurchasingValidationException("La devolución debe contener entre 1 y 200 líneas.");
        if (request.Lines.Any(line => line.OriginalLineNumber <= 0 || line.Quantity <= 0))
            throw new PurchasingValidationException("Cada línea de devolución requiere una línea original y una cantidad válidas.");
        if (request.Lines.Select(line => line.OriginalLineNumber).Distinct().Count() != request.Lines.Count)
            throw new PurchasingValidationException("Una línea de la recepción original no puede repetirse.");

        var normalized = request with { Notes = Normalize(request.Notes, 1000) };
        var acceptance = await store.AcceptAsync(
            user, idempotencyKey.Trim(), normalized, cancellationToken);
        if (acceptance.Status != "Processed")
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
            throw new PurchasingValidationException($"El valor admite máximo {maximumLength} caracteres.");
        return normalized;
    }

    private static void Require(PurchasingUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PurchasingForbiddenException($"No tienes permiso para realizar esta acción.");
    }
}
