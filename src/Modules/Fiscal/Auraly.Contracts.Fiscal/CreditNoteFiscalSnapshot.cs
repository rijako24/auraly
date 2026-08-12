using System.Text.Json;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;

namespace Auraly.Contracts.Fiscal;

public static class FiscalDocumentTypeCodes
{
    public const string Invoice = "Invoice";
    public const string CreditNote = "CreditNote";
}

public sealed record CreditNoteLineMetadata(
    int LineNumber,
    string ProductCode,
    string ProductCodeScheme,
    string UnitCode,
    string TaxName);

public sealed record SalesReturnCreditNoteSnapshot(
    SalesReturnDocumentPayload Return,
    Guid FiscalIssuerConfigurationId,
    string FiscalNumber,
    string CurrencyCode,
    int Environment,
    string QrValidationUrl,
    PosSaleUblPartyContract Customer,
    string OriginalInvoiceNumber,
    string OriginalInvoiceCufe,
    DateOnly OriginalInvoiceIssuedOn,
    IReadOnlyList<CreditNoteLineMetadata> Lines);

public static class SalesReturnCreditNoteSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static string Serialize(SalesReturnCreditNoteSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return JsonSerializer.Serialize(snapshot, Options);
    }

    public static SalesReturnCreditNoteSnapshot Deserialize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A credit-note fiscal snapshot is required.", nameof(value));
        return JsonSerializer.Deserialize<SalesReturnCreditNoteSnapshot>(value, Options)
            ?? throw new InvalidOperationException("The credit-note fiscal snapshot is invalid.");
    }
}
