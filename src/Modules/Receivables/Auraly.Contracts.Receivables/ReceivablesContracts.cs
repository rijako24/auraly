using System.Text.Json.Serialization;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Payments;

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
    public const string Cash = PortfolioPaymentMethods.Cash;
    public const string Transfer = PortfolioPaymentMethods.Transfer;
    public const string DebitCard = PortfolioPaymentMethods.DebitCard;
    public const string CreditCard = PortfolioPaymentMethods.CreditCard;
    public static bool IsSupported(string value) => PortfolioPaymentMethods.IsSupported(value);
}

public sealed record ReceivablesUserIdentity(Guid UserId, Guid TenantId, Guid BusinessId,
    IReadOnlySet<string> Permissions);
public sealed record ReceivableQuery(int Page, int PageSize, string? Search, Guid? CustomerId,
    string? Status, bool? Overdue, Guid? PartySiteId = null, bool OutstandingOnly = false,
    DateOnly? From = null, DateOnly? To = null, string? SortBy = null, string? SortDirection = null,
    Guid? ReceivableId = null, int DateRangeVersion = 1);
public sealed record ReceivableListItem(Guid ReceivableId, Guid? CustomerId, string CustomerName,
    string DocumentNumber, string CurrencyCode, decimal OriginalAmount, decimal OutstandingAmount,
    DateTimeOffset DueDate, string Status, bool IsOverdue, DateTimeOffset CreatedAt,
    Guid? PartySiteId = null, string? PartySiteName = null, decimal PaidAmount = 0,
    Guid? PartyId = null, int OverdueDays = 0, string AgingBandCode = "NotDue",DateTimeOffset? IssuedAt = null)
{
    public decimal AdjustmentAmount => OriginalAmount-PaidAmount-OutstandingAmount;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? SourceDocumentType { get; init; }
    public string? FiscalNumber { get; init; }
    public DateOnly? DueOn { get; init; }
    public DateOnly? IssuedOn { get; init; }
}
public sealed record ReceivablePage(IReadOnlyList<ReceivableListItem> Items, int Page, int PageSize,
    int TotalCount, decimal TotalOutstanding, decimal TotalOverdue,
    decimal TotalOriginal = 0, decimal TotalPaid = 0)
{
    public DateOnly? ReferenceDate {get;init;}
    public decimal TotalAdjustments => TotalOriginal-TotalPaid-TotalOutstanding;
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record ReceivableTransactionView(Guid TransactionId, string Type, decimal Amount,
    Guid SourceDocumentId, DateTimeOffset OccurredAt, decimal? BalanceChange = null);
public sealed record ReceivableDetail(Guid ReceivableId, Guid? CustomerId, string CustomerName,
    string CustomerIdentification, Guid SourceDocumentId, string SourceDocumentType,
    string DocumentNumber, string CurrencyCode, decimal OriginalAmount, decimal OutstandingAmount,
    DateTimeOffset DueDate, string Status, IReadOnlyList<ReceivableTransactionView> Transactions,
    Guid? PartySiteId = null, string? PartySiteName = null, Guid? PartyId = null,
    string? SourceNote = null)
{
    public string? FiscalNumber { get; init; }
}
public sealed record CustomerCreditProfile(Guid CustomerId, decimal? CreditLimit,
    int DefaultDueDays, bool IsCreditEnabled, decimal OutstandingAmount, decimal? AvailableCredit,
    int? MaximumOverdueInvoices = null, decimal PendingCreditAmount = 0);
public sealed record UpdateCustomerCreditProfileRequest(Guid BusinessId, decimal? CreditLimit,
    int DefaultDueDays, bool IsCreditEnabled)
{
    private int? maximumOverdueInvoices;
    public int? MaximumOverdueInvoices
    {
        get => maximumOverdueInvoices;
        init { maximumOverdueInvoices=value; HasMaximumOverdueInvoices=true; }
    }
    // Older clients omit the field. Preserve its current value on those updates;
    // an explicit JSON null from the new editor clears the limit.
    [JsonIgnore] public bool HasMaximumOverdueInvoices { get; private init; }
}
public sealed record CustomerPaymentAllocationRequest(Guid ReceivableId, decimal Amount);
public sealed record CustomerPaymentTenderRequest(string MethodCode, decimal Amount,
    decimal? TenderedAmount = null, Guid? BankAccountId = null, string? Reference = null,
    string? Notes = null, string? CardFranchiseCode = null, string? ApprovalNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PaymentMethodId = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PaymentMethodVersion = null);
public sealed record ConfirmCustomerPaymentRequest(Guid PaymentId, Guid BusinessId, Guid CustomerId,
    Guid? WorkSessionId, DateTimeOffset PaidAt, string CurrencyCode, string? Notes,
    IReadOnlyCollection<CustomerPaymentAllocationRequest> Allocations,
    IReadOnlyCollection<CustomerPaymentTenderRequest> Payments,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateOnly? PaidOn = null);
public sealed record CustomerPaymentAllocationSnapshot(int LineNumber, Guid ReceivableId, decimal Amount);
public sealed record CustomerPaymentTenderSnapshot(int LineNumber, string MethodCode, decimal Amount,
    decimal? TenderedAmount = null, Guid? BankAccountId = null, string? Reference = null,
    string? Notes = null, string? CardFranchiseCode = null, string? ApprovalNumber = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? PaymentMethodId = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PaymentMethodVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodCode = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? PaymentMethodName = null, [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? AccountId = null);
public sealed record CustomerPaymentDocumentPayload(Guid TenantId, Guid BusinessId, Guid PaymentId,
    Guid CustomerId, Guid ConfirmedByUserId, Guid? WorkSessionId, string DocumentNumber,
    Guid DocumentSeriesId, string DocumentPrefix, string DocumentSeriesCode, long DocumentConsecutive,
    DateTimeOffset PaidAt, string CurrencyCode, string? Notes,
    decimal TotalAmount, IReadOnlyList<CustomerPaymentAllocationSnapshot> Allocations,
    IReadOnlyList<CustomerPaymentTenderSnapshot> Payments);
public sealed record CustomerPaymentAcceptance(Guid PaymentId, Guid AccountingJobId,
    string DocumentNumber, string Status, bool IdempotentReplay, DateTimeOffset? PaidAt = null);
public sealed record CustomerPaymentHistoryItem(Guid PaymentId, string DocumentNumber,
    DateTimeOffset PaidAt, string CurrencyCode, decimal TotalAmount, string Status,
    int AppliedDocumentCount, IReadOnlyList<CustomerPaymentTenderSnapshot> Payments,
    IReadOnlyList<CustomerPaymentHistoryApplication> Applications,
    Guid? CustomerId = null, string? CustomerName = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? SourceDocumentId = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? SourceDocumentType = null,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    Guid? SourceGroupId = null,
    decimal? NetAppliedAmount = null);
public sealed record CustomerPaymentHistoryApplication(Guid ReceivableId,string DocumentNumber,decimal Amount)
{
    public string? InternalDocumentNumber { get; init; }
    public string? FiscalNumber { get; init; }
}
public sealed record CustomerPaymentHistoryQuery(int Page,int PageSize,string? Search,Guid? CustomerId,
    string? Status = null, bool? Overdue = null, DateOnly? From = null, DateOnly? To = null,
    Guid? PartySiteId = null, string? SortBy = null, string? SortDirection = null, int DateRangeVersion = 1);
public sealed record CustomerPaymentHistoryPage(IReadOnlyList<CustomerPaymentHistoryItem> Items,
    int Page, int PageSize, int TotalCount, decimal TotalAmount = 0)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record CustomerPortfolioQuery(int Page, int PageSize, string? Search, bool? Overdue,
    Guid? CustomerId = null, string? Status = null, DateOnly? From = null, DateOnly? To = null,
    Guid? PartySiteId = null, string? SortBy = null, string? SortDirection = null, int DateRangeVersion = 1);
public sealed record CustomerPortfolioItem(Guid CustomerId, string CustomerName,
    string Identification, int InvoiceCount, decimal OriginalAmount, decimal PaidAmount,
    decimal OutstandingAmount, decimal OverdueAmount, Guid? PartySiteId = null,
    string? PartySiteName = null);
public sealed record CustomerPortfolioPage(IReadOnlyList<CustomerPortfolioItem> Items,
    int Page, int PageSize, int TotalCount, decimal TotalOutstanding, decimal TotalOverdue,
    int TotalInvoiceCount = 0, decimal TotalOriginal = 0, decimal TotalPaid = 0)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record ReceivablesReportQuery(int Page, int PageSize, bool Consolidated,
    DateOnly Cutoff, Guid? CustomerId = null, Guid? PartySiteId = null,
    DateOnly? From = null, DateOnly? To = null, string? Status = null,
    bool OutstandingOnly = false, bool OverdueOnly = false,
    string? SortBy = null, string? SortDirection = null, string? Search = null,
    bool IncludeApplications = true, string? DocumentNumber = null,
    DateOnly? IssuedFrom = null, DateOnly? IssuedTo = null,
    int? MinimumAgeDays = null, int? MaximumAgeDays = null,
    int? MinimumDaysPastDue = null, int? MaximumDaysPastDue = null, int TemplateVersion = 3, int DateRangeVersion = 1);
public sealed record ReceivablesReportApplication(string DocumentNumber, DateTimeOffset AppliedAt,
    decimal Amount);
public sealed record ReceivablesReportGroupTotals(int InvoiceCount, decimal OriginalAmount,
    decimal PaidAmount, decimal OutstandingAmount, decimal NotDueAmount,
    decimal Overdue1To30Amount, decimal Overdue31To60Amount,
    decimal Overdue61To90Amount, decimal OverdueOver90Amount)
{
    public decimal AvailableAdvances { get; init; }
    public decimal AvailableCredits { get; init; }
    public decimal NetAmount => OutstandingAmount - AvailableAdvances - AvailableCredits;
    public decimal OtherImpact => OriginalAmount - PaidAmount - OutstandingAmount;
}
public sealed record ReceivablesReportItem(Guid? CustomerId, string CustomerName,
    string Identification, Guid? PartySiteId, string? PartySiteName, string CurrencyCode,
    int InvoiceCount, decimal OriginalAmount, decimal PaidAmount, decimal OutstandingAmount,
    decimal OverdueAmount, Guid? ReceivableId = null, string? DocumentNumber = null,
    DateTimeOffset? IssuedAt = null, DateTimeOffset? DueDate = null,
    IReadOnlyList<ReceivablesReportApplication>? Applications = null,
    Guid? PartyId = null)
{
    public decimal AvailableAdvances { get; init; }
    public decimal AvailableCredits { get; init; }
    public decimal NetAmount => OutstandingAmount - AvailableAdvances - AvailableCredits;
    public bool IsCreditBalance { get; init; }
    public string? InternalDocumentNumber { get; init; }
    public string? FiscalNumber { get; init; }
    public int? TermDays { get; init; }
    public int? AgeDays { get; init; }
    // Signed calendar difference, as in the reference: negative means not yet due.
    public int? DaysPastDue { get; init; }
    public string? LegalName { get; init; }
    public string? Address { get; init; }
    public string? City { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public decimal? CreditLimit { get; init; }
    public ReceivablesReportGroupTotals? GroupTotals { get; init; }
    public decimal OtherImpact => OriginalAmount - PaidAmount - OutstandingAmount;
    public decimal NotDueAmount { get; init; }
    public decimal Overdue1To30Amount { get; init; }
    public decimal Overdue31To60Amount { get; init; }
    public decimal Overdue61To90Amount { get; init; }
    public decimal OverdueOver90Amount { get; init; }
}
public sealed record ReceivablesReportPage(IReadOnlyList<ReceivablesReportItem> Items,
    int Page, int PageSize, int TotalCount, int TotalInvoiceCount,
    decimal TotalOriginal, decimal TotalPaid, decimal TotalOutstanding, decimal TotalOverdue)
{
    public decimal TotalAvailableAdvances { get; init; }
    public decimal TotalAvailableCredits { get; init; }
    public decimal TotalNetAmount => TotalOutstanding - TotalAvailableAdvances - TotalAvailableCredits;
    public decimal TotalOtherImpact => TotalOriginal - TotalPaid - TotalOutstanding;
    public decimal TotalNotDue { get; init; }
    public decimal TotalOverdue1To30 { get; init; }
    public decimal TotalOverdue31To60 { get; init; }
    public decimal TotalOverdue61To90 { get; init; }
    public decimal TotalOverdueOver90 { get; init; }
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
