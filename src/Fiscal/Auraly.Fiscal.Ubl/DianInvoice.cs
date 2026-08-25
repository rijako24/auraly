namespace Auraly.Fiscal.Ubl;

public sealed record DianAddress(
    string MunicipalityCode,
    string CityName,
    string DepartmentName,
    string DepartmentCode,
    string AddressLine,
    string CountryCode = "CO",
    string CountryName = "Colombia");

public sealed record DianParty(
    string Identification,
    string CheckDigit,
    string IdentificationTypeCode,
    string OrganizationTypeCode,
    string RegistrationName,
    string TradeName,
    string TaxResponsibilityCode,
    string TaxSchemeId,
    string TaxSchemeName,
    DianAddress Address,
    string? Email = null,
    string? Telephone = null);

public sealed record DianAuthorization(
    string Number,
    DateOnly ValidFrom,
    DateOnly ValidUntil,
    string Prefix,
    long RangeStart,
    long RangeEnd);

public sealed record DianSoftware(
    string ProviderTaxId,
    string ProviderCheckDigit,
    string SoftwareId,
    string SoftwarePin);

public sealed record DianTax(
    string Code,
    string Name,
    decimal TaxableAmount,
    decimal Amount,
    decimal Percent);

public sealed record DianInvoiceLine(
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

public sealed record DianPayment(
    string PaymentFormCode,
    string PaymentMeansCode,
    DateOnly DueDate,
    string? Reference);

public sealed record DianInvoice(
    string DocumentNumber,
    string Cufe,
    DateTimeOffset IssuedAt,
    string CurrencyCode,
    string InvoiceTypeCode,
    int Environment,
    DianAuthorization Authorization,
    DianSoftware Software,
    DianParty Supplier,
    DianParty Customer,
    IReadOnlyList<DianInvoiceLine> Lines,
    IReadOnlyList<DianTax> Taxes,
    DianPayment Payment,
    decimal LineExtensionAmount,
    decimal TaxExclusiveAmount,
    decimal TaxInclusiveAmount,
    decimal DiscountAmount,
    decimal PayableAmount,
    string QrPayload,
    string CustomizationId = "10",
    string ProfileId = "DIAN 2.1: Factura Electrónica de Venta",
    string UniqueCodeScheme = "CUFE-SHA384",
    bool BuyerGenerated = false)
{
    public void Validate()
    {
        if (Environment is not (1 or 2)) throw new ArgumentOutOfRangeException(nameof(Environment));
        if (string.IsNullOrWhiteSpace(DocumentNumber) || string.IsNullOrWhiteSpace(Cufe))
            throw new ArgumentException("Document number and CUFE are required.");
        if (Lines.Count == 0) throw new ArgumentException("At least one invoice line is required.");
        if (Lines.Select(line => line.Number).Order().SequenceEqual(Enumerable.Range(1, Lines.Count)) is false)
            throw new ArgumentException("Invoice line numbers must be consecutive from one.");
        if (Authorization.RangeStart <= 0 || Authorization.RangeEnd < Authorization.RangeStart)
            throw new ArgumentException("The authorized range is invalid.");
        if (!DocumentNumber.StartsWith(Authorization.Prefix, StringComparison.Ordinal))
            throw new ArgumentException("The document number does not match the authorized prefix.");
        if (LineExtensionAmount != Lines.Sum(line => line.UntaxedAmount) ||
            DiscountAmount != Lines.Sum(line => line.DiscountAmount) ||
            TaxInclusiveAmount != TaxExclusiveAmount + Taxes.Sum(tax => tax.Amount) ||
            PayableAmount != TaxInclusiveAmount)
            throw new ArgumentException("Invoice monetary totals are inconsistent.");
    }
}
