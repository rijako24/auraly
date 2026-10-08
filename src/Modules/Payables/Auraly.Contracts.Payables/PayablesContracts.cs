using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Payables;

public static class PayablesPermissionCodes
{
    public const string Read = "payables.read";
    public const string RegisterPayment = "payables.payments.create";
    public const string RegisterPosPayment = "pos.payables.payments.create";
}

public static class PayablesDocumentTypes
{
    public const string Payment = AuralyDocumentTypes.PayablePayment;
}

public static class SupplierPaymentMethods
{
    public const string Cash = "Cash";
    public const string Transfer = "Transfer";
    public const string DebitCard = "DebitCard";
    public const string CreditCard = "CreditCard";
    public static bool IsSupported(string value) => value is Cash or Transfer or DebitCard or CreditCard;
}

public sealed record PayablesUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public sealed record PayableQuery(
    int Page,
    int PageSize,
    string? Search,
    Guid? SupplierId,
    string? Status,
    bool? Overdue,
    bool OutstandingOnly = false,
    DateOnly? From = null, DateOnly? To = null, Guid? ConceptId = null,
    Guid? PartySiteId = null, string? SortBy = null, string? SortDirection = null);

public sealed record PayableListItem(
    Guid PayableId,
    Guid SupplierId,
    string SupplierName,
    string DocumentNumber,
    string CurrencyCode,
    decimal OriginalAmount,
    decimal OutstandingAmount,
    DateTimeOffset DueDate,
    string Status,
    bool IsOverdue,
    DateTimeOffset CreatedAt,
    string? ExpenseConceptName = null,
    decimal PaidAmount = 0,
    Guid? PartySiteId = null, string? PartySiteName = null);

public sealed record PayablePage(
    IReadOnlyList<PayableListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalOutstanding,
    decimal TotalOverdue)
{
    public IReadOnlyList<PayableCurrencyTotal> CurrencyTotals { get; init; } = [];
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}

public sealed record PayableCurrencyTotal(string CurrencyCode, decimal OutstandingAmount,
    decimal OverdueAmount);

public sealed record PayableExpenseConceptOption(Guid ConceptId, string Name);
public sealed record PayableExpenseConceptPage(
    IReadOnlyList<PayableExpenseConceptOption> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}

public sealed record PayableTransactionView(
    Guid TransactionId,
    string Type,
    decimal Amount,
    Guid SourceDocumentId,
    DateTimeOffset OccurredAt);

public sealed record PayableDetail(
    Guid PayableId,
    Guid SupplierId,
    string SupplierName,
    string SupplierIdentification,
    Guid SourceDocumentId,
    string SourceDocumentType,
    string DocumentNumber,
    string CurrencyCode,
    decimal OriginalAmount,
    decimal OutstandingAmount,
    DateTimeOffset DueDate,
    string Status,
    IReadOnlyList<PayableTransactionView> Transactions,
    string? ExpenseConceptName = null,
    string? ExpenseDescription = null,
    string? SourceInvoiceNumber = null,
    Guid? GoodsReceiptId = null,
    Guid? PartySiteId = null, string? PartySiteName = null);

public sealed record SupplierPaymentAllocationRequest(
    Guid PayableId,
    decimal Amount);

public sealed record SupplierPaymentTenderRequest(string MethodCode, decimal Amount,
    decimal? TenderedAmount = null, Guid? BankAccountId = null, string? Reference = null,
    string? Notes = null, string? CardFranchiseCode = null, string? ApprovalNumber = null);

public sealed record ConfirmSupplierPaymentRequest(
    Guid PaymentId,
    Guid BusinessId,
    Guid SupplierId,
    DateTimeOffset PaidAt,
    string CurrencyCode,
    string? Notes,
    IReadOnlyCollection<SupplierPaymentAllocationRequest> Allocations,
    IReadOnlyCollection<SupplierPaymentTenderRequest> Payments,
    Guid? WorkSessionId = null);

public sealed record SupplierPaymentAllocationSnapshot(
    int LineNumber,
    Guid PayableId,
    decimal Amount);

public sealed record SupplierPaymentTenderSnapshot(int LineNumber, string MethodCode, decimal Amount,
    decimal? TenderedAmount = null, Guid? BankAccountId = null, string? Reference = null,
    string? Notes = null, string? CardFranchiseCode = null, string? ApprovalNumber = null);

public sealed record SupplierPaymentDocumentPayload(
    Guid TenantId,
    Guid BusinessId,
    Guid PaymentId,
    Guid SupplierId,
    Guid ConfirmedByUserId,
    string DocumentNumber,
    Guid DocumentSeriesId,
    string DocumentPrefix,
    string DocumentSeriesCode,
    long DocumentConsecutive,
    DateTimeOffset PaidAt,
    string CurrencyCode,
    string? Notes,
    decimal TotalAmount,
    IReadOnlyList<SupplierPaymentAllocationSnapshot> Allocations,
    IReadOnlyList<SupplierPaymentTenderSnapshot> Payments,
    Guid? WorkSessionId = null);

public sealed record SupplierPaymentAcceptance(
    Guid PaymentId,
    Guid AccountingJobId,
    string DocumentNumber,
    string Status,
    bool IdempotentReplay);
public sealed record SupplierPaymentHistoryItem(Guid PaymentId, string DocumentNumber,
    DateTimeOffset PaidAt, string CurrencyCode, decimal TotalAmount, string Status,
    int AppliedDocumentCount, IReadOnlyList<SupplierPaymentTenderSnapshot> Payments,
    IReadOnlyList<SupplierPaymentHistoryApplication> Applications,
    Guid? SupplierId = null, string? SupplierName = null);
public sealed record SupplierPaymentHistoryApplication(Guid PayableId,string DocumentNumber,decimal Amount);
public sealed record SupplierPaymentHistoryQuery(int Page,int PageSize,string? Search,Guid? SupplierId,
    string? Status = null, bool? Overdue = null, DateOnly? From = null, DateOnly? To = null,
    Guid? PartySiteId = null, string? SortBy = null, string? SortDirection = null);
public sealed record SupplierPaymentHistoryPage(IReadOnlyList<SupplierPaymentHistoryItem> Items,
    int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record SupplierPortfolioQuery(int Page, int PageSize, string? Search, bool? Overdue,
    Guid? SupplierId = null, string? Status = null, DateOnly? From = null, DateOnly? To = null,
    Guid? PartySiteId = null, string? SortBy = null, string? SortDirection = null);
public sealed record SupplierPortfolioItem(Guid SupplierId, string SupplierName,
    string Identification, int InvoiceCount, decimal OriginalAmount, decimal PaidAmount,
    decimal OutstandingAmount, decimal OverdueAmount, decimal SupplierCreditAmount = 0,
    string CurrencyCode = "COP", Guid? PartySiteId = null, string? PartySiteName = null);
public sealed record SupplierPortfolioPage(IReadOnlyList<SupplierPortfolioItem> Items,
    int Page, int PageSize, int TotalCount, decimal TotalOutstanding, decimal TotalOverdue,
    int TotalInvoiceCount = 0, decimal TotalSupplierCredit = 0)
{
    public IReadOnlyList<PayableCurrencyTotal> CurrencyTotals { get; init; } = [];
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record PayablesReportQuery(int Page, int PageSize, bool Consolidated,
    DateOnly Cutoff, Guid? SupplierId = null, Guid? PartySiteId = null,
    DateOnly? From = null, DateOnly? To = null, string? Status = null,
    bool OutstandingOnly = false, bool OverdueOnly = false,
    string? SortBy = null, string? SortDirection = null, string? Search = null,
    Guid? ConceptId = null);
public sealed record PayablesReportApplication(string DocumentNumber, DateTimeOffset AppliedAt,
    decimal Amount);
public sealed record PayablesReportItem(Guid SupplierId, string SupplierName,
    string Identification, Guid? PartySiteId, string? PartySiteName, string CurrencyCode,
    int InvoiceCount, decimal OriginalAmount, decimal PaidAmount, decimal OutstandingAmount,
    decimal OverdueAmount, Guid? PayableId = null, string? DocumentNumber = null,
    DateTimeOffset? IssuedAt = null, DateTimeOffset? DueDate = null,
    IReadOnlyList<PayablesReportApplication>? Applications = null)
{
    public decimal OtherImpact => OriginalAmount - PaidAmount - OutstandingAmount;
}
public sealed record PayablesReportCurrencyTotal(string CurrencyCode, int InvoiceCount,
    decimal OriginalAmount, decimal PaidAmount, decimal OutstandingAmount, decimal OverdueAmount)
{
    public decimal OtherImpact => OriginalAmount - PaidAmount - OutstandingAmount;
}
public sealed record PayablesReportPage(IReadOnlyList<PayablesReportItem> Items,
    IReadOnlyList<PayablesReportCurrencyTotal> CurrencyTotals,
    int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}

public static class SupplierPaymentContractSerializer
{
    private static readonly JsonSerializerOptions Options =
        new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public static string Serialize(SupplierPaymentDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static SupplierPaymentDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<SupplierPaymentDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The supplier payment payload is invalid.");
}
