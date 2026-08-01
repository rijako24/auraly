using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Returns;

public static class SalesReturnPermissionCodes
{
    public const string Read = "sales.returns.read";
    public const string Create = "sales.returns.create";
    public const string Confirm = "sales.returns.confirm";
}

public static class SalesReturnDocumentTypes
{
    public const string SalesReturn = AuralyDocumentTypes.SalesReturn;
}

public static class ReturnInventoryDispositions
{
    public const string Sellable = "Sellable";
    public const string Inspection = "Inspection";
    public const string Damaged = "Damaged";
    public const string NotReturned = "NotReturned";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Sellable, Inspection, Damaged, NotReturned], StringComparer.Ordinal);
}

public static class ReturnEconomicResolutions
{
    public const string Refund = "Refund";
    public const string CustomerCredit = "CustomerCredit";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Refund, CustomerCredit], StringComparer.Ordinal);
}

public sealed record ConfirmSalesReturnLineRequest(
    int OriginalLineNumber,
    decimal Quantity,
    string InventoryDisposition);

public sealed record ConfirmSalesReturnRequest(
    Guid ReturnId,
    Guid BusinessId,
    Guid WarehouseId,
    Guid OriginalDocumentId,
    DateTimeOffset ReturnedAt,
    string EconomicResolution,
    string? RefundMethodCode,
    string ReasonDescription,
    IReadOnlyCollection<ConfirmSalesReturnLineRequest> Lines);

public sealed record SalesReturnLineSnapshot(
    int LineNumber,
    int OriginalLineNumber,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    string TaxCode,
    decimal TaxRate,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal LineTotal,
    decimal RecognizedUnitCost,
    string InventoryDisposition);

public sealed record SalesReturnDocumentPayload(
    Guid TenantId,
    Guid BusinessId,
    Guid ReturnId,
    Guid WarehouseId,
    Guid OriginalDocumentId,
    Guid CreatedByUserId,
    string DocumentNumber,
    Guid DocumentSeriesId,
    string DocumentPrefix,
    string DocumentSeriesCode,
    long DocumentConsecutive,
    DateTimeOffset ReturnedAt,
    string EconomicResolution,
    string? RefundMethodCode,
    string CorrectionCode,
    string ReasonDescription,
    Guid? CustomerId,
    string CustomerIdentification,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    IReadOnlyList<SalesReturnLineSnapshot> Lines);

public sealed record SalesReturnAcceptance(
    Guid ReturnId,
    Guid MovementId,
    string DocumentNumber,
    string Status,
    long ProcessingSequence,
    bool IdempotentReplay);

public sealed record SalesReturnUserIdentity(
    Guid UserId,
    Guid TenantId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);

public static class SalesReturnContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(SalesReturnDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static SalesReturnDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<SalesReturnDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The sales return payload is invalid.");
}
