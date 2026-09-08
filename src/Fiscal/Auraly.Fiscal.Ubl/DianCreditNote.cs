namespace Auraly.Fiscal.Ubl;

public static class DianCreditNoteCodes
{
    public const string DocumentType = "91";
    public const string ReferencesInvoiceOperation = "20";
    public const string PartialReturn = "1";
}

public sealed record DianInvoiceReference(
    string DocumentNumber,
    string Cufe,
    DateOnly IssuedOn);

public sealed record DianCreditNoteLine(
    int Number,
    string ProductCode,
    string ProductCodeScheme,
    string Description,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    decimal UntaxedAmount,
    IReadOnlyList<DianTax> Taxes);

public sealed record DianCreditNote(
    string DocumentNumber,
    string Cude,
    DateTimeOffset IssuedAt,
    string CurrencyCode,
    string OperationCode,
    string CorrectionCode,
    string CorrectionDescription,
    int Environment,
    DianSoftware Software,
    DianParty Supplier,
    DianParty Customer,
    DianInvoiceReference OriginalInvoice,
    IReadOnlyList<DianCreditNoteLine> Lines,
    IReadOnlyList<DianTax> Taxes,
    decimal LineExtensionAmount,
    decimal TaxExclusiveAmount,
    decimal TaxInclusiveAmount,
    decimal DiscountAmount,
    decimal PayableAmount,
    string QrPayload,
    string DocumentTypeCode = DianCreditNoteCodes.DocumentType,
    string CustomizationId = DianCreditNoteCodes.ReferencesInvoiceOperation,
    string ProfileId = "DIAN 2.1: Nota Crédito de Factura Electrónica de Venta",
    string UniqueCodeScheme = "CUDE-SHA384",
    string OriginalUniqueCodeScheme = "CUFE-SHA384",
    bool BuyerGenerated = false)
{
    public void Validate()
    {
        if (Environment is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(Environment));
        if (string.IsNullOrWhiteSpace(DocumentNumber) || string.IsNullOrWhiteSpace(Cude))
            throw new ArgumentException("Document number and unique code are required.");
        if (DocumentTypeCode == DianCreditNoteCodes.DocumentType &&
            (OperationCode != DianCreditNoteCodes.ReferencesInvoiceOperation ||
             CorrectionCode != DianCreditNoteCodes.PartialReturn || BuyerGenerated))
            throw new ArgumentException("The sales return must be a referenced DIAN credit note.");
        if (DocumentTypeCode == "95" &&
            (!BuyerGenerated || CustomizationId is not ("10" or "11") ||
             CorrectionCode is not ("1" or "2" or "3" or "4" or "5") ||
             UniqueCodeScheme != "CUDS-SHA384" || OriginalUniqueCodeScheme != "CUDS-SHA384"))
            throw new ArgumentException("The purchase adjustment does not follow DIAN support-document rules.");
        if (DocumentTypeCode is not (DianCreditNoteCodes.DocumentType or "95"))
            throw new ArgumentException("The credit-note document type is unsupported.");
        if (string.IsNullOrWhiteSpace(OriginalInvoice.DocumentNumber) ||
            string.IsNullOrWhiteSpace(OriginalInvoice.Cufe))
            throw new ArgumentException("The original electronic invoice reference is required.");
        if (Lines.Count == 0) throw new ArgumentException("At least one credit note line is required.");
        if (!Lines.Select(line => line.Number).Order().SequenceEqual(Enumerable.Range(1, Lines.Count)))
            throw new ArgumentException("Credit note line numbers must be consecutive from one.");
        if (Lines.Any(line => line.Quantity <= 0 || line.UnitPrice < 0 ||
                              line.DiscountAmount < 0 || line.UntaxedAmount < 0))
            throw new ArgumentException("Credit note line values are invalid.");
        if (LineExtensionAmount != Lines.Sum(line => line.UntaxedAmount) ||
            DiscountAmount != Lines.Sum(line => line.DiscountAmount) ||
            TaxInclusiveAmount != TaxExclusiveAmount + Taxes.Sum(tax => tax.Amount) ||
            PayableAmount != TaxInclusiveAmount)
            throw new ArgumentException("Credit note monetary totals are inconsistent.");
    }
}
