namespace Auraly.Fiscal.Ubl;

public static class DianDebitNoteCodes
{
    public const string DocumentType = "92";
    public const string ReferencesInvoiceOperation = "30";
}

public sealed record DianDebitNoteLine(
    int Number,
    string Description,
    string UnitCode,
    decimal Quantity,
    decimal UnitPrice,
    decimal UntaxedAmount,
    IReadOnlyList<DianTax> Taxes);

public sealed record DianDebitNote(
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
    IReadOnlyList<DianDebitNoteLine> Lines,
    IReadOnlyList<DianTax> Taxes,
    decimal UntaxedAmount,
    decimal TotalAmount,
    string QrPayload)
{
    public void Validate()
    {
        if (Environment is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(Environment));
        if (string.IsNullOrWhiteSpace(DocumentNumber) || string.IsNullOrWhiteSpace(Cude))
            throw new ArgumentException("Document number and CUDE are required.");
        if (OperationCode != DianDebitNoteCodes.ReferencesInvoiceOperation)
            throw new ArgumentException("The debit note must reference an electronic invoice.");
        if (!DianDebitNoteConcepts.Contains(CorrectionCode))
            throw new ArgumentException("The DIAN debit-note concept is invalid.");
        if (string.IsNullOrWhiteSpace(OriginalInvoice.DocumentNumber) ||
            string.IsNullOrWhiteSpace(OriginalInvoice.Cufe))
            throw new ArgumentException("The original electronic invoice reference is required.");
        if (Lines.Count == 0 || !Lines.Select(line => line.Number).Order()
                .SequenceEqual(Enumerable.Range(1, Lines.Count)))
            throw new ArgumentException("Debit-note lines must be consecutive from one.");
        if (Lines.Any(line => line.Quantity <= 0 || line.UnitPrice <= 0 || line.UntaxedAmount <= 0))
            throw new ArgumentException("Debit-note line values are invalid.");
        if (UntaxedAmount != Lines.Sum(line => line.UntaxedAmount) ||
            TotalAmount != UntaxedAmount + Taxes.Sum(tax => tax.Amount))
            throw new ArgumentException("Debit-note monetary totals are inconsistent.");
    }

    private static readonly IReadOnlySet<string> DianDebitNoteConcepts =
        new HashSet<string>(["1", "2", "3", "4"], StringComparer.Ordinal);
}
