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
            if (!ReturnInventoryDispositions.All.Contains(line.InventoryDisposition))
                throw new SalesReturnValidationException("A return line has an invalid inventory disposition.");
        }

        var normalized = request with
        {
            EconomicResolution = request.EconomicResolution.Trim(),
            RefundMethodCode = request.RefundMethodCode?.Trim().ToUpperInvariant(),
            ReasonDescription = request.ReasonDescription.Trim()
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
