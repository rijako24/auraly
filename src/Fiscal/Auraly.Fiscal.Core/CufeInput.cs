namespace Auraly.Fiscal.Core;

public enum FiscalEnvironment
{
    Production = 1,
    Test = 2
}

public sealed record FiscalTaxAmount(string Code, decimal Amount);

public sealed class CufeInput
{
    public CufeInput(
        string invoiceNumber,
        DateTimeOffset issuedAt,
        decimal untaxedAmount,
        decimal payableAmount,
        string supplierTaxId,
        string customerIdentification,
        FiscalTechnicalKey technicalKey,
        FiscalEnvironment environment,
        IEnumerable<FiscalTaxAmount> taxes)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber)) throw new ArgumentException("An invoice number is required.", nameof(invoiceNumber));
        if (untaxedAmount < 0) throw new ArgumentOutOfRangeException(nameof(untaxedAmount));
        if (payableAmount < 0) throw new ArgumentOutOfRangeException(nameof(payableAmount));
        if (string.IsNullOrWhiteSpace(supplierTaxId)) throw new ArgumentException("A supplier tax ID is required.", nameof(supplierTaxId));
        if (string.IsNullOrWhiteSpace(customerIdentification)) throw new ArgumentException("A customer identification is required.", nameof(customerIdentification));

        InvoiceNumber = invoiceNumber.Trim();
        IssuedAt = issuedAt;
        UntaxedAmount = untaxedAmount;
        PayableAmount = payableAmount;
        SupplierTaxId = supplierTaxId.Trim();
        CustomerIdentification = customerIdentification.Trim();
        TechnicalKey = technicalKey ?? throw new ArgumentNullException(nameof(technicalKey));
        Environment = environment;
        Taxes = taxes?.ToArray() ?? throw new ArgumentNullException(nameof(taxes));
    }

    public string InvoiceNumber { get; }
    public DateTimeOffset IssuedAt { get; }
    public decimal UntaxedAmount { get; }
    public decimal PayableAmount { get; }
    public string SupplierTaxId { get; }
    public string CustomerIdentification { get; }
    public FiscalTechnicalKey TechnicalKey { get; }
    public FiscalEnvironment Environment { get; }
    public IReadOnlyCollection<FiscalTaxAmount> Taxes { get; }
}
