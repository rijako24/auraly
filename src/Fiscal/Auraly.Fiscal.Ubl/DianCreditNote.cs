namespace Auraly.Fiscal.Ubl;

public static class DianCreditNoteCodes
{
    public const string DocumentType = "91";
    public const string ReferencesInvoiceOperation = "20";
    public const string PartialReturn = "1";
    public const string FullCancellation = "2";
    public const string SupportAdjustmentProfileId =
        "DIAN 2.1: Nota de ajuste al documento soporte en adquisiciones efectuadas a sujetos no obligados a expedir factura o documento equivalente";
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
    bool BuyerGenerated = false,
    string? SellerPostalZone = null)
{
    public void Validate()
    {
        if (Environment is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(Environment));
        if (string.IsNullOrWhiteSpace(DocumentNumber) || string.IsNullOrWhiteSpace(Cude))
            throw new ArgumentException("Document number and unique code are required.");
        if (DocumentTypeCode == DianCreditNoteCodes.DocumentType &&
            (OperationCode != DianCreditNoteCodes.ReferencesInvoiceOperation ||
             CorrectionCode is not (DianCreditNoteCodes.PartialReturn or
                 DianCreditNoteCodes.FullCancellation) || BuyerGenerated))
            throw new ArgumentException("La nota crédito debe referenciar la factura electrónica y tener un motivo DIAN válido.");
        if (DocumentTypeCode == "95" &&
            (!BuyerGenerated || CustomizationId is not ("10" or "11") ||
             CorrectionCode is not ("1" or "2" or "3" or "4" or "5") ||
             UniqueCodeScheme != "CUDS-SHA384" || OriginalUniqueCodeScheme != "CUDS-SHA384" ||
             ProfileId != DianCreditNoteCodes.SupportAdjustmentProfileId))
            throw new ArgumentException("La nota de ajuste no cumple las reglas DIAN del documento soporte.");
        if (DocumentTypeCode == "95" && CustomizationId == "10" &&
            (SellerPostalZone is null || SellerPostalZone.Length != 6 ||
             !SellerPostalZone.All(char.IsAsciiDigit)))
            throw new ArgumentException("El vendedor residente de la nota de ajuste requiere un código postal DIAN de seis dígitos.");
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
            TaxExclusiveAmount != Lines.Sum(line => line.UntaxedAmount) ||
            TaxInclusiveAmount != LineExtensionAmount + Taxes.Sum(tax => tax.Amount) ||
            PayableAmount != TaxInclusiveAmount)
            throw new ArgumentException("Credit note monetary totals are inconsistent.");
    }
}
