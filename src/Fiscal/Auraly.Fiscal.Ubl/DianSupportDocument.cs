namespace Auraly.Fiscal.Ubl;

public sealed record DianSupportDocument(
    string DocumentNumber,
    string Cuds,
    DateTimeOffset IssuedAt,
    string CurrencyCode,
    int Environment,
    DianAuthorization Authorization,
    DianSoftware Software,
    DianParty Seller,
    DianParty Buyer,
    string SellerOriginCode,
    string SellerPostalZone,
    IReadOnlyList<DianInvoiceLine> Lines,
    IReadOnlyList<DianTax> Taxes,
    DianPayment Payment,
    decimal LineExtensionAmount,
    decimal TaxExclusiveAmount,
    decimal TaxInclusiveAmount,
    decimal DiscountAmount,
    decimal PayableAmount,
    string QrPayload)
{
    public const string Profile =
        "DIAN 2.1: documento soporte en adquisiciones efectuadas a no obligados a facturar.";

    public void Validate()
    {
        if (Environment is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(Environment));
        if (string.IsNullOrWhiteSpace(DocumentNumber) || string.IsNullOrWhiteSpace(Cuds))
            throw new ArgumentException("Document number and CUDS are required.");
        if (SellerOriginCode is not ("10" or "11"))
            throw new ArgumentException("Seller origin must be resident (10) or non-resident (11).");
        if (SellerOriginCode == "10" &&
            (SellerPostalZone.Length != 6 || !SellerPostalZone.All(char.IsDigit)))
            throw new ArgumentException(
                "A resident seller requires a six-digit DIAN postal zone.");
        if (Buyer.IdentificationTypeCode != "31")
            throw new ArgumentException("The support-document buyer must be identified by NIT.");
        if (Lines.Count == 0)
            throw new ArgumentException("At least one support-document line is required.");
        if (!Lines.Select(line => line.Number).Order()
                .SequenceEqual(Enumerable.Range(1, Lines.Count)))
            throw new ArgumentException("Support-document line numbers must be consecutive from one.");
        if (Authorization.RangeStart <= 0 || Authorization.RangeEnd < Authorization.RangeStart)
            throw new ArgumentException("The authorized range is invalid.");
        if (!DocumentNumber.StartsWith(Authorization.Prefix, StringComparison.Ordinal))
            throw new ArgumentException("The document number does not match the authorized prefix.");
        if (Lines.Any(line =>
                line.Quantity * line.UnitPrice - line.DiscountAmount != line.UntaxedAmount))
            throw new ArgumentException(
                "A support-document line does not reconcile quantity, price, discount and net amount.");
        if (LineExtensionAmount != Lines.Sum(line => line.UntaxedAmount) ||
            DiscountAmount != Lines.Sum(line => line.DiscountAmount) ||
            TaxExclusiveAmount != Lines.Sum(line => line.UntaxedAmount) ||
            TaxInclusiveAmount != LineExtensionAmount + Taxes.Sum(tax => tax.Amount) ||
            PayableAmount != TaxInclusiveAmount)
            throw new ArgumentException("Support-document monetary totals are inconsistent.");
    }
}
