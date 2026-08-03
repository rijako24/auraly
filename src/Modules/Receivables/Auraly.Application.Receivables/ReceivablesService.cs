using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Receivables;
using Auraly.Domain.Receivables;

namespace Auraly.Application.Receivables;

public interface IReceivablesStore
{
    Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token);
    Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid receivableId, CancellationToken token);
    Task<CustomerCreditProfile?> GetCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, CancellationToken token);
    Task<CustomerCreditProfile> UpdateCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId,
        UpdateCustomerCreditProfileRequest request, CancellationToken token);
    Task<CustomerPaymentAcceptance> AcceptPaymentAsync(ReceivablesUserIdentity user, string idempotencyKey,
        ConfirmCustomerPaymentRequest request, ReceivableSettlement settlement, CancellationToken token);
}

public sealed class ReceivablesService(IReceivablesStore store, IDocumentProcessingSignalPublisher signals)
{
    public Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100) throw new ReceivablesValidationException("Invalid pagination.");
        if (query.Status is not null && query.Status is not ("Open" or "PartiallyPaid" or "Paid" or "Cancelled"))
            throw new ReceivablesValidationException("The receivable status is invalid.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120) }, token);
    }

    public Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid id, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (id == Guid.Empty) throw new ReceivablesValidationException("ReceivableId is required.");
        return store.GetAsync(user, id, token);
    }

    public Task<CustomerCreditProfile?> GetCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (customerId == Guid.Empty) throw new ReceivablesValidationException("CustomerId is required.");
        return store.GetCreditProfileAsync(user, customerId, token);
    }

    public Task<CustomerCreditProfile> UpdateCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId,
        UpdateCustomerCreditProfileRequest request, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.ManageCredit);
        if (request.BusinessId != user.BusinessId) throw new ReceivablesForbiddenException("The profile belongs to another business.");
        if (customerId == Guid.Empty || request.DefaultDueDays is < 0 or > 3650 || request.CreditLimit < 0)
            throw new ReceivablesValidationException("The credit profile is invalid.");
        return store.UpdateCreditProfileAsync(user, customerId, request, token);
    }

    public async Task<CustomerPaymentAcceptance> ConfirmPaymentAsync(ReceivablesUserIdentity user,
        string idempotencyKey, ConfirmCustomerPaymentRequest request, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.RegisterPayment);
        if (request.BusinessId != user.BusinessId) throw new ReceivablesForbiddenException("The receipt belongs to another business.");
        if (request.PaymentId == Guid.Empty || request.CustomerId == Guid.Empty || request.PaidAt == default)
            throw new ReceivablesValidationException("PaymentId, CustomerId and PaidAt are required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new ReceivablesValidationException("A valid Idempotency-Key is required.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        var method = request.PaymentMethod.Trim();
        if (currency != "COP" || !CustomerPaymentMethods.IsSupported(method))
            throw new ReceivablesValidationException("Only COP and supported payment methods are accepted.");
        ReceivableSettlement settlement;
        try { settlement = ReceivableSettlement.Create(request.Allocations.Select(x => new ReceivableAllocation(x.ReceivableId, x.Amount))); }
        catch (ArgumentException ex) { throw new ReceivablesValidationException(ex.Message, ex); }
        var normalized = request with { CurrencyCode = currency, PaymentMethod = method,
            Reference = Normalize(request.Reference, 120), Notes = Normalize(request.Notes, 1000) };
        var acceptance = await store.AcceptPaymentAsync(user, idempotencyKey.Trim(), normalized, settlement, token);
        await signals.PublishAsync(new DocumentProcessingSignal(acceptance.MovementId, request.BusinessId,
            request.PaymentId, ReceivablesDocumentTypes.Payment), token);
        return acceptance;
    }

    private static string? Normalize(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > max) throw new ReceivablesValidationException($"The value exceeds {max} characters.");
        return result;
    }
    private static void Require(ReceivablesUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission)) throw new ReceivablesForbiddenException($"Permission '{permission}' is required.");
    }
}

public sealed class ReceivablesForbiddenException(string message) : Exception(message);
public sealed class ReceivablesConflictException(string message) : Exception(message);
public sealed class ReceivablesValidationException : Exception
{
    public ReceivablesValidationException(string message) : base(message) { }
    public ReceivablesValidationException(string message, Exception inner) : base(message, inner) { }
}
