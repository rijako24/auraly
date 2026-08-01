namespace Auraly.Fiscal.Core;

public sealed class CudeInput
{
    public CudeInput(
        string creditNoteNumber,
        DateTimeOffset issuedAt,
        decimal lineExtensionAmount,
        decimal payableAmount,
        string supplierTaxId,
        string customerIdentification,
        string softwarePin,
        FiscalEnvironment environment,
        IEnumerable<FiscalTaxAmount> taxes)
    {
        if (string.IsNullOrWhiteSpace(creditNoteNumber))
            throw new ArgumentException("A credit note number is required.", nameof(creditNoteNumber));
        if (lineExtensionAmount < 0) throw new ArgumentOutOfRangeException(nameof(lineExtensionAmount));
        if (payableAmount < 0) throw new ArgumentOutOfRangeException(nameof(payableAmount));
        if (string.IsNullOrWhiteSpace(supplierTaxId))
            throw new ArgumentException("A supplier tax ID is required.", nameof(supplierTaxId));
        if (string.IsNullOrWhiteSpace(customerIdentification))
            throw new ArgumentException("A customer identification is required.", nameof(customerIdentification));
        if (string.IsNullOrWhiteSpace(softwarePin))
            throw new ArgumentException("A software PIN is required.", nameof(softwarePin));
        if (!Enum.IsDefined(environment)) throw new ArgumentOutOfRangeException(nameof(environment));

        CreditNoteNumber = creditNoteNumber.Trim();
        IssuedAt = issuedAt;
        LineExtensionAmount = lineExtensionAmount;
        PayableAmount = payableAmount;
        SupplierTaxId = supplierTaxId.Trim();
        CustomerIdentification = customerIdentification.Trim();
        SoftwarePin = softwarePin.Trim();
        Environment = environment;
        Taxes = taxes?.ToArray() ?? throw new ArgumentNullException(nameof(taxes));
    }

    public string CreditNoteNumber { get; }
    public DateTimeOffset IssuedAt { get; }
    public decimal LineExtensionAmount { get; }
    public decimal PayableAmount { get; }
    public string SupplierTaxId { get; }
    public string CustomerIdentification { get; }
    public string SoftwarePin { get; }
    public FiscalEnvironment Environment { get; }
    public IReadOnlyCollection<FiscalTaxAmount> Taxes { get; }
}
