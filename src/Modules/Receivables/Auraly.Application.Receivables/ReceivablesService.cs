using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Receivables;
using Auraly.Domain.Receivables;

namespace Auraly.Application.Receivables;

public interface IReceivablesStore
{
    Task<ReceivablePage> ListAsync(ReceivablesUserIdentity user, ReceivableQuery query, CancellationToken token);
    Task<CustomerPortfolioPage> ListCustomersAsync(ReceivablesUserIdentity user, CustomerPortfolioQuery query,
        CancellationToken token);
    Task<ReceivableDetail?> GetAsync(ReceivablesUserIdentity user, Guid receivableId, CancellationToken token);
    Task<CustomerCreditProfile?> GetCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId, CancellationToken token);
    Task<CustomerPaymentHistoryPage> PaymentHistoryAsync(ReceivablesUserIdentity user, Guid customerId,
        int page, int pageSize, CancellationToken token);
    Task<CustomerPaymentHistoryPage> ListPaymentsAsync(ReceivablesUserIdentity user,
        CustomerPaymentHistoryQuery query,CancellationToken token);
    Task<CustomerCreditProfile> UpdateCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId,
        UpdateCustomerCreditProfileRequest request, CancellationToken token);
    Task<CustomerPaymentAcceptance> AcceptPaymentAsync(ReceivablesUserIdentity user, string idempotencyKey,
        ConfirmCustomerPaymentRequest request, ReceivableSettlement settlement,
        PaymentTenderBreakdown tenders, CancellationToken token);
    Task<ImportPreexistingReceivablesAcceptance> ImportPreexistingAsync(ReceivablesUserIdentity user,
        ImportPreexistingReceivablesRequest request,CancellationToken token);
}

public sealed class ReceivablesService(
    IReceivablesStore store,
    AccountingProcessingCoordinator accounting,
    Auraly.BuildingBlocks.Application.Synchronization.IPosSynchronizationOutboxDispatcher synchronization)
{
    public Task<CustomerPortfolioPage> ListCustomersAsync(ReceivablesUserIdentity user,
        CustomerPortfolioQuery query, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (query.Page < 1 || query.PageSize is < 1 or > 100)
            throw new ReceivablesValidationException("Invalid pagination.");
        return store.ListCustomersAsync(user, query with { Search = Normalize(query.Search, 120) }, token);
    }

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

    public Task<CustomerPaymentHistoryPage> PaymentHistoryAsync(ReceivablesUserIdentity user,
        Guid customerId, int page, int pageSize, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.Read);
        if (customerId == Guid.Empty || page < 1 || pageSize is < 1 or > 100)
            throw new ReceivablesValidationException("Los filtros del historial de abonos no son válidos.");
        return store.PaymentHistoryAsync(user, customerId, page, pageSize, token);
    }

    public Task<CustomerPaymentHistoryPage> ListPaymentsAsync(ReceivablesUserIdentity user,
        CustomerPaymentHistoryQuery query,CancellationToken token=default)
    {
        Require(user,ReceivablesPermissionCodes.Read);
        if(query.Page<1||query.PageSize is <1 or >100)throw new ReceivablesValidationException("Invalid pagination.");
        return store.ListPaymentsAsync(user,query with { Search=Normalize(query.Search,120) },token);
    }

    public async Task<CustomerCreditProfile> UpdateCreditProfileAsync(ReceivablesUserIdentity user, Guid customerId,
        UpdateCustomerCreditProfileRequest request, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.ManageCredit);
        if (request.BusinessId != user.BusinessId) throw new ReceivablesForbiddenException("The profile belongs to another business.");
        if (customerId == Guid.Empty || request.DefaultDueDays is < 0 or > 3650 || request.CreditLimit < 0)
            throw new ReceivablesValidationException("The credit profile is invalid.");
        var result = await store.UpdateCreditProfileAsync(user, customerId, request, token);
        await synchronization.DispatchPendingAsync(user.TenantId, user.BusinessId, CancellationToken.None);
        return result;
    }

    public async Task<CustomerPaymentAcceptance> ConfirmPaymentAsync(ReceivablesUserIdentity user,
        string idempotencyKey, ConfirmCustomerPaymentRequest request, CancellationToken token = default)
    {
        Require(user, ReceivablesPermissionCodes.RegisterPayment);
        if (request.BusinessId != user.BusinessId) throw new ReceivablesForbiddenException("The receipt belongs to another business.");
        if (request.PaymentId == Guid.Empty || request.CustomerId == Guid.Empty || request.PaidAt == default)
            throw new ReceivablesValidationException("PaymentId, CustomerId and PaidAt are required.");
        if (request.Allocations is null || request.Payments is null)
            throw new ReceivablesValidationException("Allocations and payments are required.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 160)
            throw new ReceivablesValidationException("A valid Idempotency-Key is required.");
        var currency = request.CurrencyCode.Trim().ToUpperInvariant();
        if (currency != "COP") throw new ReceivablesValidationException("Only COP is accepted.");
        ReceivableSettlement settlement;
        try { settlement = ReceivableSettlement.Create(request.Allocations.Select(x => new ReceivableAllocation(x.ReceivableId, x.Amount))); }
        catch (ArgumentException ex) { throw new ReceivablesValidationException(ex.Message, ex); }
        var payments = request.Payments.Select(NormalizeTender).ToArray();
        PaymentTenderBreakdown breakdown;
        try
        {
            breakdown = PaymentTenderBreakdown.Create(payments.Select(ToDomain), settlement.TotalAmount,
                settlement.Allocations.Count, SupportedMethods);
            ValidateTenderEvidence(breakdown);
        }
        catch (ArgumentException ex) { throw new ReceivablesValidationException(ex.Message, ex); }
        var normalized = request with { CurrencyCode = currency,
            Notes = Normalize(request.Notes, 1000), Payments = payments };
        var acceptance = await store.AcceptPaymentAsync(
            user, idempotencyKey.Trim(), normalized, settlement, breakdown, token);
        if (!acceptance.IdempotentReplay)
            await accounting.RequestPostingAsync(request.BusinessId, request.PaymentId,
                ReceivablesDocumentTypes.Payment, token);
        return acceptance;
    }

    public async Task<ImportPreexistingReceivablesAcceptance> ImportPreexistingAsync(
        ReceivablesUserIdentity user,ImportPreexistingReceivablesRequest request,CancellationToken token=default)
    {
        Require(user,ReceivablesPermissionCodes.ManageCredit);
        if(request.BusinessId!=user.BusinessId)throw new ReceivablesForbiddenException("The portfolio belongs to another business.");
        if(request.Items is null||request.Items.Count is <1 or >100)throw new ReceivablesValidationException("Import between 1 and 100 receivables per batch.");
        if(request.Items.Any(x=>x.ReceivableId==Guid.Empty||((x.CustomerId is null||x.CustomerId==Guid.Empty)&&string.IsNullOrWhiteSpace(x.CustomerIdentification))||x.CounterpartAccountId==Guid.Empty||x.Amount<=0||x.IssuedAt==default||x.DueDate==default||string.IsNullOrWhiteSpace(x.DocumentNumber)))throw new ReceivablesValidationException("Every portfolio row must contain customer, invoice, dates, amount and counterpart account.");
        var normalized=request with { Items=request.Items.Select(x=>x with { DocumentNumber=x.DocumentNumber.Trim(),CustomerIdentification=Normalize(x.CustomerIdentification,64),Notes=Normalize(x.Notes,500) }).ToArray() };
        var result=await store.ImportPreexistingAsync(user,normalized,token);
        foreach(var id in result.ReceivableIds)await accounting.RequestPostingAsync(user.BusinessId,id,ReceivablesDocumentTypes.PreexistingReceivable,token);
        return result;
    }

    private static readonly IReadOnlySet<string> SupportedMethods = new HashSet<string>(
        [CustomerPaymentMethods.Cash, CustomerPaymentMethods.BankTransfer,
         CustomerPaymentMethods.DebitCard, CustomerPaymentMethods.CreditCard], StringComparer.Ordinal);

    private static CustomerPaymentTenderRequest NormalizeTender(CustomerPaymentTenderRequest value) => value with
    {
        MethodCode = value.MethodCode?.Trim() ?? string.Empty, Reference = Normalize(value.Reference, 120),
        Notes = Normalize(value.Notes, 500), CardFranchiseCode = Normalize(value.CardFranchiseCode, 40),
        ApprovalNumber = Normalize(value.ApprovalNumber, 80)
    };

    private static PaymentTender ToDomain(CustomerPaymentTenderRequest value) => new(
        value.MethodCode, value.Amount, value.TenderedAmount, value.BankAccountId,
        value.Reference, value.Notes, value.CardFranchiseCode, value.ApprovalNumber);

    private static void ValidateTenderEvidence(PaymentTenderBreakdown breakdown)
    {
        foreach (var tender in breakdown.Tenders)
        {
            if (tender.MethodCode == CustomerPaymentMethods.BankTransfer &&
                string.IsNullOrWhiteSpace(tender.Reference))
                throw new ArgumentException("A bank transfer requires a reference.");
            if (tender.MethodCode is CustomerPaymentMethods.DebitCard or CustomerPaymentMethods.CreditCard &&
                (string.IsNullOrWhiteSpace(tender.CardFranchiseCode) ||
                 string.IsNullOrWhiteSpace(tender.ApprovalNumber)))
                throw new ArgumentException("A card payment requires franchise and approval number.");
            if (tender.MethodCode != CustomerPaymentMethods.BankTransfer && tender.BankAccountId is not null)
                throw new ArgumentException("Only a bank transfer can select a bank account.");
        }
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
