using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Dispatching;

namespace Auraly.Application.Returns;

public interface ISalesReturnStore
{
    Task<SalesReturnAcceptance> AcceptAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesReturnRequest request,
        CancellationToken cancellationToken);
}

public sealed class SalesReturnService(
    ISalesReturnStore store,
    IDocumentProcessingSignalPublisher signals)
{
    public async Task<SalesReturnAcceptance> ConfirmAsync(
        SalesReturnUserIdentity user,
        string idempotencyKey,
        ConfirmSalesReturnRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        if (request.BusinessId != user.BusinessId)
            throw new SalesReturnForbiddenException("La devolución pertenece a otra sede.");
        Require(user, SalesReturnPermissionCodes.Create);
        if (request.ReturnId == Guid.Empty || request.OriginalDocumentId == Guid.Empty ||
            request.WarehouseId == Guid.Empty)
            throw new SalesReturnValidationException("Se requiere una devolución, el documento original y una bodega válidos.");
        if (request.ReturnedAt == default)
            throw new SalesReturnValidationException("Indica la fecha de devolución.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new SalesReturnValidationException("No se pudo identificar la operación. Vuelve a intentarlo con la misma solicitud.");
        if (!ReturnEconomicResolutions.All.Contains(request.EconomicResolution))
            throw new SalesReturnValidationException("La resolución económica no es válida.");
        if (request.EconomicResolution == ReturnEconomicResolutions.ReverseUncollected ||
            request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit && request.DispatchId is not null)
        {
            if (!user.Permissions.Contains(DispatchPermissionCodes.Settle))
                throw new SalesReturnForbiddenException("No tienes permiso para conciliar este despacho.");
            if (request.DispatchId is null || request.DispatchId == Guid.Empty ||
                request.WorkSessionId is not null || request.RefundMethodCode is not null)
                throw new SalesReturnValidationException("La devolución del despacho requiere su vínculo y no registra un reembolso de caja.");
        }
        else if (request.DispatchId is not null)
            throw new SalesReturnValidationException("El vínculo de despacho solo aplica a una devolución pendiente de conciliación.");
        if (!SalesReturnScopes.All.Contains(request.ReturnScopeCode))
            throw new SalesReturnValidationException("El alcance de la devolución de venta no es válido.");
        if (request.EconomicResolution == ReturnEconomicResolutions.Refund &&
            string.IsNullOrWhiteSpace(request.RefundMethodCode))
            throw new SalesReturnValidationException("Selecciona una forma de pago para realizar el reembolso.");
        if (request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit &&
            !string.IsNullOrWhiteSpace(request.RefundMethodCode))
            throw new SalesReturnValidationException("Un saldo a favor del cliente no puede incluir una forma de reembolso.");
        if (request.EconomicResolution == ReturnEconomicResolutions.Refund &&
            !SalesReturnRefundMethods.All.Contains(request.RefundMethodCode!))
            throw new SalesReturnValidationException("La forma de reembolso no es válida.");
        if (request.WorkSessionId == Guid.Empty)
            throw new SalesReturnValidationException("La sesión de caja debe estar vacía o tener una identificación válida.");
        var cardRefund = request.EconomicResolution == ReturnEconomicResolutions.Refund &&
            request.RefundMethodCode is SalesReturnRefundMethods.DebitCard or SalesReturnRefundMethods.CreditCard;
        var transferRefund = request.EconomicResolution == ReturnEconomicResolutions.Refund &&
            request.RefundMethodCode == SalesReturnRefundMethods.Transfer;
        if (request.OriginalDispatchPaymentId == Guid.Empty ||
            cardRefund && request.OriginalPaymentNumber.HasValue == request.OriginalDispatchPaymentId.HasValue)
            throw new SalesReturnValidationException(
                "La reversión por tarjeta debe identificar el pago original.");
        if (!cardRefund && (request.OriginalPaymentNumber is not null || request.OriginalDispatchPaymentId is not null))
            throw new SalesReturnValidationException(
                "Solo una reversión por tarjeta puede enlazar un pago original.");
        if (!transferRefund && request.BankAccountId is not null)
            throw new SalesReturnValidationException(
                "La cuenta bancaria solo aplica a una devolución por transferencia.");
        if (transferRefund && string.IsNullOrWhiteSpace(request.SettlementReference))
            throw new SalesReturnValidationException(
                "La devolución por transferencia requiere la referencia bancaria.");
        if (!transferRefund && (request.SettlementReference is not null || request.SettlementNotes is not null))
            throw new SalesReturnValidationException(
                "La referencia y nota bancaria solo aplican a una devolución por transferencia.");
        if (request.SettlementReference?.Trim().Length > 160 || request.SettlementNotes?.Trim().Length > 500)
            throw new SalesReturnValidationException(
                "La referencia admite 160 caracteres y la nota bancaria 500.");
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || request.ReasonCode.Trim().Length > 40)
            throw new SalesReturnValidationException("Selecciona un motivo de devolución.");
        if (request.Notes?.Trim().Length > 1000)
            throw new SalesReturnValidationException("Las observaciones de la devolución admiten máximo 1.000 caracteres.");
        if (string.IsNullOrWhiteSpace(request.ReasonDescription) ||
            request.ReasonDescription.Trim().Length > 300)
            throw new SalesReturnValidationException("Indica un motivo de devolución de máximo 300 caracteres.");
        if (request.Lines.Count == 0 || request.Lines.Count > 500)
            throw new SalesReturnValidationException("La devolución debe contener entre 1 y 500 líneas.");
        if (request.Lines.Select(line => line.OriginalLineNumber).Distinct().Count() != request.Lines.Count)
            throw new SalesReturnValidationException("Una línea de la venta original no puede repetirse.");
        foreach (var line in request.Lines)
        {
            if (line.OriginalLineNumber <= 0 || line.Quantity <= 0)
                throw new SalesReturnValidationException("La línea original y la cantidad devuelta deben ser mayores que cero.");
            if (line.InventoryDisposition != ReturnInventoryDispositions.Sellable &&
                !(request.ReasonCode == SalesReturnReasonCodes.RestaurantPreparedCancellation &&
                  line.InventoryDisposition == ReturnInventoryDispositions.NotReturned))
                throw new SalesReturnValidationException(
                    "Solo una cancelación de plato preparado admite devolución sin reingreso de inventario.");
        }
        if (request.ReasonCode == SalesReturnReasonCodes.RestaurantPreparedCancellation &&
            request.Lines.Any(line => line.InventoryDisposition != ReturnInventoryDispositions.NotReturned))
            throw new SalesReturnValidationException(
                "La cancelación de plato preparado no puede reingresar productos al inventario.");

        var normalized = request with
        {
            EconomicResolution = request.EconomicResolution.Trim(),
            RefundMethodCode = request.EconomicResolution == ReturnEconomicResolutions.Refund
                ? request.RefundMethodCode!.Trim()
                : null,
            ReasonDescription = request.ReasonDescription.Trim(),
            ReasonCode = request.ReasonCode.Trim(),
            ReturnScopeCode = request.ReturnScopeCode.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            SettlementReference = transferRefund ? request.SettlementReference!.Trim() : null,
            SettlementNotes = transferRefund && !string.IsNullOrWhiteSpace(request.SettlementNotes)
                ? request.SettlementNotes.Trim()
                : null
        };
        var accepted = await store.AcceptAsync(
            user, idempotencyKey.Trim(), normalized, cancellationToken);
        if (accepted.Status != "Processed")
            await signals.PublishAsync(new DocumentProcessingSignal(
                accepted.MovementId, request.BusinessId, request.ReturnId,
                SalesReturnDocumentTypes.SalesReturn), cancellationToken);
        return accepted;
    }

    private static void Require(SalesReturnUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new SalesReturnForbiddenException($"No tienes permiso para realizar esta acción.");
    }
}

public sealed class SalesReturnValidationException(string message, Exception? innerException = null) : Exception(message, innerException);
public sealed class SalesReturnForbiddenException(string message) : Exception(message);
public sealed class SalesReturnConflictException(string message) : Exception(message);
