using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Receivables;

public static class ReceivablesPermissionCodes
{
    public const string Read = "receivables.read";
    public const string RegisterPayment = "receivables.payments.create";
    public const string RegisterPosPayment = "pos.receivables.payments.create";
    public const string ManageCredit = "receivables.credit.manage";
}

public static class ReceivablesDocumentTypes
{
    public const string Payment = AuralyDocumentTypes.ReceivablePayment;
    public const string PreexistingReceivable = "PreexistingReceivable";
}

public static class CustomerPaymentMethods
{
    public const string Cash = "Cash";
    public const string Transfer = "Transfer";
    public const string DebitCard = "DebitCard";
    public const string CreditCard = "CreditCard";
    public static bool IsSupported(string value) => value is Cash or Transfer or DebitCard or CreditCard;
}

public sealed record ReceivablesUserIdentity(Guid UserId, Guid TenantId, Guid BusinessId,
    IReadOnlySet<string> Permissions);
public sealed record ReceivableQuery(int Page, int PageSize, string? Search, Guid? CustomerId,
    string? Status, bool? Overdue, Guid? PartySiteId = null, bool OutstandingOnly = false,
    DateOnly? From = null, DateOnly? To = null);
public sealed record ReceivableListItem(Guid ReceivableId, Guid CustomerId, string CustomerName,
    string DocumentNumber, string CurrencyCode, decimal OriginalAmount, decimal OutstandingAmount,
    DateTimeOffset DueDate, string Status, bool IsOverdue, DateTimeOffset CreatedAt,
    Guid? PartySiteId = null, string? PartySiteName = null, decimal PaidAmount = 0);
public sealed record ReceivablePage(IReadOnlyList<ReceivableListItem> Items, int Page, int PageSize,
    int TotalCount, decimal TotalOutstanding, decimal TotalOverdue,
    decimal TotalOriginal = 0, decimal TotalPaid = 0)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record ReceivableTransactionView(Guid TransactionId, string Type, decimal Amount,
    Guid SourceDocumentId, DateTimeOffset OccurredAt);
public sealed record ReceivableDetail(Guid ReceivableId, Guid CustomerId, string CustomerName,
    string CustomerIdentification, Guid SourceDocumentId, string SourceDocumentType,
    string DocumentNumber, string CurrencyCode, decimal OriginalAmount, decimal OutstandingAmount,
    DateTimeOffset DueDate, string Status, IReadOnlyList<ReceivableTransactionView> Transactions,
    Guid? PartySiteId = null, string? PartySiteName = null);
public sealed record CustomerCreditProfile(Guid CustomerId, decimal? CreditLimit,
    int DefaultDueDays, bool IsCreditEnabled, decimal OutstandingAmount, decimal? AvailableCredit);
public sealed record UpdateCustomerCreditProfileRequest(Guid BusinessId, decimal? CreditLimit,
    int DefaultDueDays, bool IsCreditEnabled);
public sealed record CustomerPaymentAllocationRequest(Guid ReceivableId, decimal Amount);
public sealed record CustomerPaymentTenderRequest(string MethodCode, decimal Amount,
    decimal? TenderedAmount = null, Guid? BankAccountId = null, string? Reference = null,
    string? Notes = null, string? CardFranchiseCode = null, string? ApprovalNumber = null);
public sealed record ConfirmCustomerPaymentRequest(Guid PaymentId, Guid BusinessId, Guid CustomerId,
    Guid? WorkSessionId, DateTimeOffset PaidAt, string CurrencyCode, string? Notes,
    IReadOnlyCollection<CustomerPaymentAllocationRequest> Allocations,
    IReadOnlyCollection<CustomerPaymentTenderRequest> Payments);
public sealed record CustomerPaymentAllocationSnapshot(int LineNumber, Guid ReceivableId, decimal Amount);
public sealed record CustomerPaymentTenderSnapshot(int LineNumber, string MethodCode, decimal Amount,
    decimal? TenderedAmount = null, Guid? BankAccountId = null, string? Reference = null,
    string? Notes = null, string? CardFranchiseCode = null, string? ApprovalNumber = null);
public sealed record CustomerPaymentDocumentPayload(Guid TenantId, Guid BusinessId, Guid PaymentId,
    Guid CustomerId, Guid ConfirmedByUserId, Guid? WorkSessionId, string DocumentNumber,
    Guid DocumentSeriesId, string DocumentPrefix, string DocumentSeriesCode, long DocumentConsecutive,
    DateTimeOffset PaidAt, string CurrencyCode, string? Notes,
    decimal TotalAmount, IReadOnlyList<CustomerPaymentAllocationSnapshot> Allocations,
    IReadOnlyList<CustomerPaymentTenderSnapshot> Payments);
public sealed record CustomerPaymentAcceptance(Guid PaymentId, Guid AccountingJobId,
    string DocumentNumber, string Status, bool IdempotentReplay);
public sealed record CustomerPaymentHistoryItem(Guid PaymentId, string DocumentNumber,
    DateTimeOffset PaidAt, string CurrencyCode, decimal TotalAmount, string Status,
    int AppliedDocumentCount, IReadOnlyList<CustomerPaymentTenderSnapshot> Payments,
    IReadOnlyList<CustomerPaymentHistoryApplication> Applications,
    Guid? CustomerId = null, string? CustomerName = null);
public sealed record CustomerPaymentHistoryApplication(Guid ReceivableId,string DocumentNumber,decimal Amount);
public sealed record CustomerPaymentHistoryQuery(int Page,int PageSize,string? Search,Guid? CustomerId,
    string? Status = null, bool? Overdue = null, DateOnly? From = null, DateOnly? To = null);
public sealed record CustomerPaymentHistoryPage(IReadOnlyList<CustomerPaymentHistoryItem> Items,
    int Page, int PageSize, int TotalCount, decimal TotalAmount = 0)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record CustomerPortfolioQuery(int Page, int PageSize, string? Search, bool? Overdue,
    Guid? CustomerId = null, string? Status = null, DateOnly? From = null, DateOnly? To = null);
public sealed record CustomerPortfolioItem(Guid CustomerId, string CustomerName,
    string Identification, int InvoiceCount, decimal OriginalAmount, decimal PaidAmount,
    decimal OutstandingAmount, decimal OverdueAmount);
public sealed record CustomerPortfolioPage(IReadOnlyList<CustomerPortfolioItem> Items,
    int Page, int PageSize, int TotalCount, decimal TotalOutstanding, decimal TotalOverdue,
    int TotalInvoiceCount = 0, decimal TotalOriginal = 0, decimal TotalPaid = 0)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record PreexistingReceivableItemRequest(Guid ReceivableId,Guid? CustomerId,string? CustomerIdentification,
    Guid? PartySiteId,string DocumentNumber,DateTimeOffset IssuedAt,DateTimeOffset DueDate,
    decimal Amount,Guid CounterpartAccountId,string? Notes);
public sealed record ImportPreexistingReceivablesRequest(Guid BusinessId,
    IReadOnlyCollection<PreexistingReceivableItemRequest> Items);
public sealed record PreexistingReceivablePayload(Guid TenantId,Guid BusinessId,Guid ReceivableId,
    Guid CustomerId,Guid? PartySiteId,Guid ConfirmedByUserId,string DocumentNumber,
    DateTimeOffset IssuedAt,DateTimeOffset DueDate,decimal Amount,Guid CounterpartAccountId,string? Notes);
public sealed record ImportPreexistingReceivablesAcceptance(int AcceptedCount,IReadOnlyList<Guid> ReceivableIds);

public static class CustomerPaymentContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize(CustomerPaymentDocumentPayload payload) => JsonSerializer.Serialize(payload, Options);
    public static CustomerPaymentDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<CustomerPaymentDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The customer payment payload is invalid.");
}

public static class PreexistingReceivableContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize(PreexistingReceivablePayload payload) => JsonSerializer.Serialize(payload, Options);
    public static PreexistingReceivablePayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<PreexistingReceivablePayload>(payload, Options)
        ?? throw new InvalidOperationException("The preexisting receivable payload is invalid.");
}
