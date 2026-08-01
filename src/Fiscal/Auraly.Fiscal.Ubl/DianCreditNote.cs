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
    string QrPayload)
{
    public void Validate()
    {
        if (Environment is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(Environment));
        if (string.IsNullOrWhiteSpace(DocumentNumber) || string.IsNullOrWhiteSpace(Cude))
            throw new ArgumentException("Document number and CUDE are required.");
        if (OperationCode != DianCreditNoteCodes.ReferencesInvoiceOperation)
            throw new ArgumentException("The MVP only supports credit notes that reference an invoice.");
        if (CorrectionCode != DianCreditNoteCodes.PartialReturn)
            throw new ArgumentException("The sales return flow requires DIAN correction concept 1.");
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
