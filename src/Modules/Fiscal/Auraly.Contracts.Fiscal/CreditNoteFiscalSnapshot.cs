using System.Text.Json;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Expenses;

namespace Auraly.Contracts.Fiscal;

public static class FiscalDocumentTypeCodes
{
    public const string Invoice = "Invoice";
    public const string CreditNote = "CreditNote";
    public const string DebitNote = "DebitNote";
    public const string SupportDocument = "SupportDocument";
    public const string SupportDocumentAdjustment = "SupportDocumentAdjustment";
    public const string ElectronicPayroll = "ElectronicPayroll";
}

public sealed record PurchaseSupportLineMetadata(
    int LineNumber, string ProductCode, string ProductCodeScheme,
    string UnitCode, string TaxName, string? DianTaxCode = null);

public sealed record PurchaseSupportFiscalSnapshot(
    GoodsReceiptDocumentPayload? Receipt,
    Guid FiscalIssuerConfigurationId,
    string FiscalNumber,
    int Environment,
    string QrValidationUrl,
    PosSaleUblPartyContract Seller,
    PosSaleUblAuthorizationContract Authorization,
    IReadOnlyList<PurchaseSupportLineMetadata> Lines,
    string SellerOriginCode = "10",
    ExpenseDocumentPayload? Expense = null,
    PurchaseReturnDocumentPayload? Adjustment = null,
    string? OriginalSupportNumber = null,
    string? OriginalSupportCuds = null,
    DateOnly? OriginalSupportIssuedOn = null,
    string? SellerPostalZone = null,
    ExpenseCancellationPayload? ExpenseCancellation = null,
    GoodsReceiptCostDocumentAccountingPayload? CostDocument = null);

public static class PurchaseSupportFiscalSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(PurchaseSupportFiscalSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static PurchaseSupportFiscalSnapshot Deserialize(string value) =>
        JsonSerializer.Deserialize<PurchaseSupportFiscalSnapshot>(value, Options)
        ?? throw new InvalidOperationException("The purchase support fiscal snapshot is invalid.");
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

public sealed record FiscalOnlyCreditNoteLine(
    int LineNumber,
    string ProductCode,
    string ProductCodeScheme,
    string Description,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal UntaxedAmount,
    string TaxCode,
    string TaxName,
    decimal TaxAmount,
    decimal TaxRate);

public sealed record FiscalOnlyCreditNoteSnapshot(
    Guid CorrectionId,
    Guid BusinessId,
    Guid OriginalDocumentId,
    Guid RetainedDocumentId,
    Guid FiscalIssuerConfigurationId,
    string FiscalNumber,
    string CurrencyCode,
    int Environment,
    string QrValidationUrl,
    PosSaleUblPartyContract Customer,
    string CustomerIdentification,
    string OriginalInvoiceNumber,
    string OriginalInvoiceCufe,
    DateOnly OriginalInvoiceIssuedOn,
    DateTimeOffset IssuedAt,
    decimal UntaxedAmount,
    decimal TotalAmount,
    decimal DiscountAmount,
    IReadOnlyList<FiscalOnlyCreditNoteLine> Lines);

public static class FiscalOnlyCreditNoteSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(FiscalOnlyCreditNoteSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static FiscalOnlyCreditNoteSnapshot Deserialize(string value) =>
        JsonSerializer.Deserialize<FiscalOnlyCreditNoteSnapshot>(value, Options)
        ?? throw new InvalidOperationException("El snapshot de la nota crédito fiscal es inválido.");
}

public sealed record SalesDebitNoteFiscalSnapshot(
    SalesDebitNoteDocumentPayload DebitNote,
    Guid FiscalIssuerConfigurationId,
    string FiscalNumber,
    string CurrencyCode,
    int Environment,
    string QrValidationUrl,
    PosSaleUblPartyContract Customer,
    string OriginalInvoiceNumber,
    string OriginalInvoiceCufe,
    DateOnly OriginalInvoiceIssuedOn);

public static class SalesDebitNoteFiscalSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize(SalesDebitNoteFiscalSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static SalesDebitNoteFiscalSnapshot Deserialize(string value) =>
        JsonSerializer.Deserialize<SalesDebitNoteFiscalSnapshot>(value, Options)
        ?? throw new InvalidOperationException("The debit-note fiscal snapshot is invalid.");
}
