using Auraly.Application.DocumentProcessing;
using Auraly.Contracts.Payables;
using Auraly.Domain.Payables;

namespace Auraly.Application.Payables;

public interface IPayablesStore
{
    Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken);

    Task<PayableDetail?> GetAsync(
        PayablesUserIdentity user,
        Guid payableId,
        CancellationToken cancellationToken);

    Task<SupplierPaymentAcceptance> AcceptPaymentAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        PayableSettlement settlement,
        CancellationToken cancellationToken);
}

public sealed class PayablesService(
    IPayablesStore store,
    IDocumentProcessingSignalPublisher signalPublisher)
{
    public Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (query.Page < 1) throw new PayablesValidationException("Page must be greater than zero.");
        if (query.PageSize is < 1 or > 100)
            throw new PayablesValidationException("PageSize must be between 1 and 100.");
        if (query.Status is not null && query.Status is not ("Open" or "PartiallyPaid" or "Paid" or "Cancelled"))
            throw new PayablesValidationException("The payable status is invalid.");
        return store.ListAsync(user, query with { Search = Normalize(query.Search, 120) }, cancellationToken);
    }

    public Task<PayableDetail?> GetAsync(
        PayablesUserIdentity user,
        Guid payableId,
        CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (payableId == Guid.Empty) throw new PayablesValidationException("PayableId is required.");
        return store.GetAsync(user, payableId, cancellationToken);
    }

    public async Task<SupplierPaymentAcceptance> ConfirmPaymentAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.RegisterPayment);
        if (request.BusinessId != user.BusinessId)
            throw new PayablesForbiddenException("The payment belongs to another business.");
        if (request.PaymentId == Guid.Empty) throw new PayablesValidationException("PaymentId is required.");
        if (request.WorkSessionId == Guid.Empty)
            throw new PayablesValidationException("WorkSessionId must be null or valid.");
        if (request.SupplierId == Guid.Empty) throw new PayablesValidationException("SupplierId is required.");
        if (request.PaidAt == default) throw new PayablesValidationException("PaidAt is required.");
        if (string.IsNullOrWhiteSpace(request.CurrencyCode))
            throw new PayablesValidationException("CurrencyCode is required.");
        if (string.IsNullOrWhiteSpace(request.PaymentMethod))
            throw new PayablesValidationException("PaymentMethod is required.");
        if (request.Allocations is null)
            throw new PayablesValidationException("Allocations are required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PayablesValidationException("Idempotency-Key is required.");
        if (idempotencyKey.Length > 160)
            throw new PayablesValidationException("Idempotency-Key is too long.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency != "COP")
            throw new PayablesValidationException("Only COP supplier payments are supported in this slice.");
        var method = request.PaymentMethod.Trim();
        if (!SupplierPaymentMethods.IsSupported(method))
            throw new PayablesValidationException("PaymentMethod must be Cash or BankTransfer.");

        PayableSettlement settlement;
        try
        {
            settlement = PayableSettlement.Create(
                request.Allocations.Select(item => new PayableAllocation(item.PayableId, item.Amount)));
        }
        catch (ArgumentException exception)
        {
            throw new PayablesValidationException(exception.Message, exception);
        }

        var normalized = request with
        {
            CurrencyCode = currency,
            PaymentMethod = method,
            Reference = Normalize(request.Reference, 120),
            Notes = Normalize(request.Notes, 1000)
        };
        var acceptance = await store.AcceptPaymentAsync(
            user, idempotencyKey.Trim(), normalized, settlement, cancellationToken);
        await signalPublisher.PublishAsync(
            new DocumentProcessingSignal(
                acceptance.MovementId,
                request.BusinessId,
                request.PaymentId,
                PayablesDocumentTypes.Payment),
            cancellationToken);
        return acceptance;
    }

    private static string? Normalize(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var result = value.Trim();
        if (result.Length > maximumLength)
            throw new PayablesValidationException($"The value exceeds {maximumLength} characters.");
        return result;
    }

    private static void Require(PayablesUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new PayablesForbiddenException($"Permission '{permission}' is required.");
    }
}

public sealed class PayablesForbiddenException(string message) : Exception(message);
public sealed class PayablesConflictException(string message) : Exception(message);
public sealed class PayablesValidationException : Exception
{
    public PayablesValidationException(string message) : base(message) { }
    public PayablesValidationException(string message, Exception inner) : base(message, inner) { }
}
