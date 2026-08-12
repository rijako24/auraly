using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Receivables;

public static class ReceivablesPermissionCodes
{
    public const string Read = "receivables.read";
    public const string RegisterPayment = "receivables.payments.create";
    public const string ManageCredit = "receivables.credit.manage";
}

public static class ReceivablesDocumentTypes
{
    public const string Payment = AuralyDocumentTypes.ReceivablePayment;
}

public static class CustomerPaymentMethods
{
    public const string Cash = "Cash";
    public const string BankTransfer = "BankTransfer";
    public const string DebitCard = "DebitCard";
    public const string CreditCard = "CreditCard";
    public static bool IsSupported(string value) => value is Cash or BankTransfer or DebitCard or CreditCard;
}

public sealed record ReceivablesUserIdentity(Guid UserId, Guid TenantId, Guid BusinessId,
    IReadOnlySet<string> Permissions);
public sealed record ReceivableQuery(int Page, int PageSize, string? Search, Guid? CustomerId,
    string? Status, bool? Overdue);
public sealed record ReceivableListItem(Guid ReceivableId, Guid CustomerId, string CustomerName,
    string DocumentNumber, string CurrencyCode, decimal OriginalAmount, decimal OutstandingAmount,
    DateTimeOffset DueDate, string Status, bool IsOverdue, DateTimeOffset CreatedAt);
public sealed record ReceivablePage(IReadOnlyList<ReceivableListItem> Items, int Page, int PageSize,
    int TotalCount, decimal TotalOutstanding, decimal TotalOverdue)
{
    public int TotalPages => TotalCount == 0 ? 0 : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
}
public sealed record ReceivableTransactionView(Guid TransactionId, string Type, decimal Amount,
    Guid SourceDocumentId, DateTimeOffset OccurredAt);
public sealed record ReceivableDetail(Guid ReceivableId, Guid CustomerId, string CustomerName,
    string CustomerIdentification, Guid SourceDocumentId, string SourceDocumentType,
    string DocumentNumber, string CurrencyCode, decimal OriginalAmount, decimal OutstandingAmount,
    DateTimeOffset DueDate, string Status, IReadOnlyList<ReceivableTransactionView> Transactions);
public sealed record CustomerCreditProfile(Guid CustomerId, decimal? CreditLimit,
    int DefaultDueDays, bool IsCreditEnabled, decimal OutstandingAmount, decimal? AvailableCredit);
public sealed record UpdateCustomerCreditProfileRequest(Guid BusinessId, decimal? CreditLimit,
    int DefaultDueDays, bool IsCreditEnabled);
public sealed record CustomerPaymentAllocationRequest(Guid ReceivableId, decimal Amount);
public sealed record ConfirmCustomerPaymentRequest(Guid PaymentId, Guid BusinessId, Guid CustomerId,
    Guid? WorkSessionId, DateTimeOffset PaidAt, string CurrencyCode, string PaymentMethod,
    string? Reference, string? Notes, IReadOnlyCollection<CustomerPaymentAllocationRequest> Allocations);
public sealed record CustomerPaymentAllocationSnapshot(int LineNumber, Guid ReceivableId, decimal Amount);
public sealed record CustomerPaymentDocumentPayload(Guid TenantId, Guid BusinessId, Guid PaymentId,
    Guid CustomerId, Guid ConfirmedByUserId, Guid? WorkSessionId, string DocumentNumber,
    Guid DocumentSeriesId, string DocumentPrefix, string DocumentSeriesCode, long DocumentConsecutive,
    DateTimeOffset PaidAt, string CurrencyCode, string PaymentMethod, string? Reference, string? Notes,
    decimal TotalAmount, IReadOnlyList<CustomerPaymentAllocationSnapshot> Allocations);
public sealed record CustomerPaymentAcceptance(Guid PaymentId, Guid MovementId, string DocumentNumber,
    string Status, long ProcessingSequence, bool IdempotentReplay);

public static class CustomerPaymentContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    public static string Serialize(CustomerPaymentDocumentPayload payload) => JsonSerializer.Serialize(payload, Options);
    public static CustomerPaymentDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<CustomerPaymentDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The customer payment payload is invalid.");
}
