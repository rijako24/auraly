using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Documents;

namespace Auraly.Contracts.Returns;

public static class SalesDebitNotePermissionCodes
{
    public const string Read = "sales.debit-notes.read";
    public const string Create = "sales.debit-notes.create";
}

public static class SalesDebitNoteDocumentTypes
{
    public const string SalesDebitNote = AuralyDocumentTypes.SalesDebitNote;
}

public static class DianDebitNoteConcepts
{
    public const string Interest = "1";
    public const string Charge = "2";
    public const string ValueChange = "3";
    public const string Other = "4";

    public static readonly IReadOnlyDictionary<string, string> All =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Interest] = "Intereses",
            [Charge] = "Gastos por cobrar",
            [ValueChange] = "Cambio del valor",
            [Other] = "Otros"
        };
}

public sealed record ConfirmSalesDebitNoteLineRequest(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode,
    decimal TaxRate);

public sealed record ConfirmSalesDebitNoteRequest(
    Guid DebitNoteId,
    Guid BusinessId,
    Guid OriginalDocumentId,
    DateTimeOffset IssuedAt,
    DateTimeOffset DueAt,
    string ConceptCode,
    string ReasonDescription,
    IReadOnlyCollection<ConfirmSalesDebitNoteLineRequest> Lines,
    string? Notes = null);

public sealed record SalesDebitNoteLineSnapshot(
    int LineNumber,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    string TaxCode,
    decimal TaxRate,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record SalesDebitNoteDocumentPayload(
    Guid TenantId,
    Guid BusinessId,
    Guid DebitNoteId,
    Guid OriginalDocumentId,
    Guid CreatedByUserId,
    string DocumentNumber,
    Guid DocumentSeriesId,
    string DocumentPrefix,
    string DocumentSeriesCode,
    long DocumentConsecutive,
    DateTimeOffset IssuedAt,
    DateTimeOffset DueAt,
    string ConceptCode,
    string ReasonDescription,
    Guid CustomerId,
    string CustomerIdentification,
    decimal UntaxedAmount,
    decimal TaxAmount,
    decimal TotalAmount,
    IReadOnlyList<SalesDebitNoteLineSnapshot> Lines,
    string? Notes = null);

public sealed record SalesDebitNoteAcceptance(
    Guid DebitNoteId,
    Guid JobId,
    string DocumentNumber,
    string Status,
    long ProcessingSequence,
    bool IdempotentReplay);

public sealed record SalesDebitNoteListItem(
    Guid DebitNoteId,
    Guid OriginalDocumentId,
    string DocumentNumber,
    string OriginalDocumentNumber,
    string CustomerName,
    string CustomerIdentification,
    DateTimeOffset IssuedAt,
    string ConceptCode,
    string ReasonDescription,
    decimal TotalAmount,
    string Status,
    string FiscalStatus,
    string? Cude);

public sealed record SalesDebitNoteDetail(
    SalesDebitNoteListItem Header,
    DateTimeOffset DueAt,
    decimal UntaxedAmount,
    decimal TaxAmount,
    string? Notes,
    IReadOnlyList<SalesDebitNoteLineSnapshot> Lines);

public sealed record SalesDebitNotePage(
    IReadOnlyList<SalesDebitNoteListItem> Items,
    int Page,
    int PageSize,
    long TotalCount,
    int TotalPages);

public sealed record SalesDebitNoteQuery(
    int Page,
    int PageSize,
    string? Search,
    DateOnly? From,
    DateOnly? To);

public static class SalesDebitNoteContractSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(SalesDebitNoteDocumentPayload payload) =>
        JsonSerializer.Serialize(payload, Options);

    public static SalesDebitNoteDocumentPayload Deserialize(string payload) =>
        JsonSerializer.Deserialize<SalesDebitNoteDocumentPayload>(payload, Options)
        ?? throw new InvalidOperationException("The sales debit-note payload is invalid.");
}
