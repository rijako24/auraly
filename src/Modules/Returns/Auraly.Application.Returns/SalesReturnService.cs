using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Returns;

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
            throw new SalesReturnForbiddenException("The return belongs to another business.");
        Require(user, SalesReturnPermissionCodes.Create);
        Require(user, SalesReturnPermissionCodes.Confirm);
        if (request.ReturnId == Guid.Empty || request.OriginalDocumentId == Guid.Empty ||
            request.WarehouseId == Guid.Empty)
            throw new SalesReturnValidationException("ReturnId, OriginalDocumentId and WarehouseId are required.");
        if (request.ReturnedAt == default)
            throw new SalesReturnValidationException("ReturnedAt is required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new SalesReturnValidationException("A valid Idempotency-Key is required.");
        if (!ReturnEconomicResolutions.All.Contains(request.EconomicResolution))
            throw new SalesReturnValidationException("The economic resolution is invalid.");
        if (request.EconomicResolution == ReturnEconomicResolutions.Refund &&
            string.IsNullOrWhiteSpace(request.RefundMethodCode))
            throw new SalesReturnValidationException("A refund method is required for a refund.");
        if (request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit &&
            !string.IsNullOrWhiteSpace(request.RefundMethodCode))
            throw new SalesReturnValidationException("Customer credit cannot include a refund method.");
        if (request.EconomicResolution == ReturnEconomicResolutions.Refund &&
            (!string.Equals(request.RefundMethodCode, SalesReturnRefundMethods.Cash, StringComparison.OrdinalIgnoreCase) ||
             request.WorkSessionId is null || request.OriginalPaymentNumber is null))
            throw new SalesReturnValidationException(
                "A cash refund requires its original payment and an active work session.");
        if (request.EconomicResolution == ReturnEconomicResolutions.CustomerCredit &&
            request.OriginalPaymentNumber is not null)
            throw new SalesReturnValidationException("Customer credit cannot reference a payment to refund.");
        if (string.IsNullOrWhiteSpace(request.ReasonCode) || request.ReasonCode.Trim().Length > 40)
            throw new SalesReturnValidationException("The return reason code is required.");
        if (request.Notes?.Trim().Length > 1000)
            throw new SalesReturnValidationException("Return notes cannot exceed 1000 characters.");
        if (string.IsNullOrWhiteSpace(request.ReasonDescription) ||
            request.ReasonDescription.Trim().Length > 300)
            throw new SalesReturnValidationException("A return reason of at most 300 characters is required.");
        if (request.Lines.Count == 0 || request.Lines.Count > 500)
            throw new SalesReturnValidationException("The return requires between one and 500 lines.");
        if (request.Lines.Select(line => line.OriginalLineNumber).Distinct().Count() != request.Lines.Count)
            throw new SalesReturnValidationException("An original sale line can appear only once.");
        foreach (var line in request.Lines)
        {
            if (line.OriginalLineNumber <= 0 || line.Quantity <= 0)
                throw new SalesReturnValidationException("Return line and quantity must be positive.");
            if (line.InventoryDisposition is not (ReturnInventoryDispositions.Sellable or ReturnInventoryDispositions.NotReturned))
                throw new SalesReturnValidationException(
                    "Inspection and damaged returns require a configured inventory destination.");
        }

        var normalized = request with
        {
            EconomicResolution = request.EconomicResolution.Trim(),
            RefundMethodCode = request.EconomicResolution == ReturnEconomicResolutions.Refund
                ? SalesReturnRefundMethods.Cash
                : null,
            ReasonDescription = request.ReasonDescription.Trim(),
            ReasonCode = request.ReasonCode.Trim(),
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };
        var accepted = await store.AcceptAsync(
            user, idempotencyKey.Trim(), normalized, cancellationToken);
        await signals.PublishAsync(new DocumentProcessingSignal(
            accepted.MovementId, request.BusinessId, request.ReturnId,
            SalesReturnDocumentTypes.SalesReturn), cancellationToken);
        return accepted;
    }

    private static void Require(SalesReturnUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new SalesReturnForbiddenException($"Permission '{permission}' is required.");
    }
}

public sealed class SalesReturnValidationException(string message) : Exception(message);
public sealed class SalesReturnForbiddenException(string message) : Exception(message);
public sealed class SalesReturnConflictException(string message) : Exception(message);
