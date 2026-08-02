using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Payables;

public static class PayablesPermissionCodes
{
    public const string Read = "payables.read";
    public const string RegisterPayment = "payables.payments.create";
}

public static class PayablesDocumentTypes
{
    public const string Payment = AuralyDocumentTypes.PayablePayment;
}

public static class SupplierPaymentMethods
{
    public const string Cash = "Cash";
    public const string BankTransfer = "BankTransfer";
    public static bool IsSupported(string value) => value is Cash or BankTransfer;
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
    bool? Overdue);

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
    DateTimeOffset CreatedAt);

public sealed record PayablePage(
    IReadOnlyList<PayableListItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    decimal TotalOutstanding,
    decimal TotalOverdue)
{
    public int TotalPages => TotalCount == 0
        ? 0
        : (int)Math.Ceiling(TotalCount / (decimal)PageSize);
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
    IReadOnlyList<PayableTransactionView> Transactions);

public sealed record SupplierPaymentAllocationRequest(
    Guid PayableId,
    decimal Amount);

public sealed record ConfirmSupplierPaymentRequest(
    Guid PaymentId,
    Guid BusinessId,
    Guid SupplierId,
    DateTimeOffset PaidAt,
    string CurrencyCode,
    string PaymentMethod,
    string? Reference,
    string? Notes,
    IReadOnlyCollection<SupplierPaymentAllocationRequest> Allocations);

public sealed record SupplierPaymentAllocationSnapshot(
    int LineNumber,
    Guid PayableId,
    decimal Amount);

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
    string PaymentMethod,
    string? Reference,
    string? Notes,
    decimal TotalAmount,
    IReadOnlyList<SupplierPaymentAllocationSnapshot> Allocations);

public sealed record SupplierPaymentAcceptance(
    Guid PaymentId,
    Guid MovementId,
    string DocumentNumber,
    string Status,
    long ProcessingSequence,
    bool IdempotentReplay);

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
