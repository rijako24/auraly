using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Commerce.Accounting.Application;
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

    Task<SupplierPortfolioPage> ListSuppliersAsync(
        PayablesUserIdentity user, SupplierPortfolioQuery query, CancellationToken cancellationToken);

    Task<SupplierPaymentHistoryPage> PaymentHistoryAsync(
        PayablesUserIdentity user, Guid supplierId, int page, int pageSize,
        CancellationToken cancellationToken);
    Task<SupplierPaymentHistoryPage> ListPaymentsAsync(PayablesUserIdentity user,
        SupplierPaymentHistoryQuery query,CancellationToken cancellationToken);

    Task<SupplierPaymentAcceptance> AcceptPaymentAsync(
        PayablesUserIdentity user,
        string idempotencyKey,
        ConfirmSupplierPaymentRequest request,
        PayableSettlement settlement,
        PaymentTenderBreakdown tenders,
        CancellationToken cancellationToken);
}

public sealed class PayablesService(
    IPayablesStore store,
    AccountingProcessingCoordinator accounting)
{
    public Task<SupplierPortfolioPage> ListSuppliersAsync(PayablesUserIdentity user,
        SupplierPortfolioQuery query, CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new PayablesValidationException("Invalid pagination.");
        ValidateLedgerFilters(query.Status,query.From,query.To);
        return store.ListSuppliersAsync(user, query with { Search = Normalize(query.Search, 120) }, cancellationToken);
    }

    public Task<PayablePage> ListAsync(
        PayablesUserIdentity user,
        PayableQuery query,
        CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (query.Page < 1) throw new PayablesValidationException("Page must be greater than zero.");
        if (query.PageSize is < 1 or > 100)
            throw new PayablesValidationException("PageSize must be between 1 and 100.");
        ValidateLedgerFilters(query.Status,query.From,query.To);
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

    public Task<SupplierPaymentHistoryPage> PaymentHistoryAsync(PayablesUserIdentity user,
        Guid supplierId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        Require(user, PayablesPermissionCodes.Read);
        if (supplierId == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            throw new PayablesValidationException("Los filtros del historial de abonos no son válidos.");
        return store.PaymentHistoryAsync(user, supplierId, page, pageSize, cancellationToken);
    }

    public Task<SupplierPaymentHistoryPage> ListPaymentsAsync(PayablesUserIdentity user,
        SupplierPaymentHistoryQuery query,CancellationToken cancellationToken=default)
    {
        Require(user,PayablesPermissionCodes.Read);
        if(query.Page<1||query.PageSize is <1 or >100)throw new PayablesValidationException("Invalid pagination.");
        ValidateLedgerFilters(query.Status,query.From,query.To);
        return store.ListPaymentsAsync(user,query with { Search=Normalize(query.Search,120) },cancellationToken);
    }

    private static void ValidateLedgerFilters(string? status,DateOnly? from,DateOnly? to)
    {
        if(status is not null and not ("Open" or "PartiallyPaid" or "Paid" or "Cancelled"))
            throw new PayablesValidationException("The payable status is invalid.");
        if(from>to)throw new PayablesValidationException("The date range is invalid.");
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
        if (request.Allocations is null || request.Payments is null)
            throw new PayablesValidationException("Allocations and payments are required.");
        if (request.Allocations.Any(item => item is null) || request.Payments.Any(item => item is null))
            throw new PayablesValidationException("Allocations and payments cannot contain empty rows.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new PayablesValidationException("Idempotency-Key is required.");
        if (idempotencyKey.Length > 160)
            throw new PayablesValidationException("Idempotency-Key is too long.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency != "COP")
            throw new PayablesValidationException("Only COP supplier payments are supported in this slice.");
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

        var payments = request.Payments.Select(NormalizeTender).ToArray();
        PaymentTenderBreakdown breakdown;
        try
        {
            breakdown = PaymentTenderBreakdown.Create(payments.Select(ToDomain), settlement.TotalAmount,
                settlement.Allocations.Count, SupportedMethods);
            foreach (var tender in breakdown.Tenders)
            {
                if (tender.MethodCode == SupplierPaymentMethods.BankTransfer &&
                    string.IsNullOrWhiteSpace(tender.Reference))
                    throw new ArgumentException("A bank transfer requires a reference.");
                if (tender.MethodCode is SupplierPaymentMethods.DebitCard or SupplierPaymentMethods.CreditCard &&
                    (string.IsNullOrWhiteSpace(tender.CardFranchiseCode) ||
                     string.IsNullOrWhiteSpace(tender.ApprovalNumber)))
                    throw new ArgumentException("A card payment requires franchise and approval number.");
                if (tender.MethodCode != SupplierPaymentMethods.BankTransfer && tender.BankAccountId is not null)
                    throw new ArgumentException("Only a bank transfer can select a bank account.");
            }
        }
        catch (ArgumentException exception)
        {
            throw new PayablesValidationException(exception.Message, exception);
        }
        var normalized = request with { CurrencyCode = currency,
            Notes = Normalize(request.Notes, 1000), Payments = payments };
        var acceptance = await store.AcceptPaymentAsync(
            user, idempotencyKey.Trim(), normalized, settlement, breakdown, cancellationToken);
        if (!acceptance.IdempotentReplay)
            await accounting.RequestPostingAsync(request.BusinessId, request.PaymentId,
                PayablesDocumentTypes.Payment, cancellationToken);
        return acceptance;
    }

    private static readonly IReadOnlySet<string> SupportedMethods = new HashSet<string>(
        [SupplierPaymentMethods.Cash, SupplierPaymentMethods.BankTransfer,
         SupplierPaymentMethods.DebitCard, SupplierPaymentMethods.CreditCard], StringComparer.Ordinal);

    private static SupplierPaymentTenderRequest NormalizeTender(SupplierPaymentTenderRequest value) => value with
    {
        MethodCode = value.MethodCode?.Trim() ?? string.Empty, Reference = Normalize(value.Reference, 120),
        Notes = Normalize(value.Notes, 500), CardFranchiseCode = Normalize(value.CardFranchiseCode, 40),
        ApprovalNumber = Normalize(value.ApprovalNumber, 80)
    };

    private static PaymentTender ToDomain(SupplierPaymentTenderRequest value) => new(
        value.MethodCode, value.Amount, value.TenderedAmount, value.BankAccountId,
        value.Reference, value.Notes, value.CardFranchiseCode, value.ApprovalNumber);

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
