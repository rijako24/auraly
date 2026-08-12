namespace Auraly.Domain.Purchasing;

public enum PurchaseTaxTreatment
{
    DeductibleInputVat,
    CapitalizedCost,
    NotApplicable
}

public sealed record CalculatedGoodsReceiptLine(
    int LineNumber,
    Guid ProductId,
    string Description,
    decimal Quantity,
    decimal UnitCost,
    decimal DiscountAmount,
    string TaxCode,
    decimal TaxRate,
    PurchaseTaxTreatment TaxTreatment,
    decimal NetAmount,
    decimal TaxAmount,
    decimal LineTotal);

public sealed record GoodsReceiptCalculation(
    IReadOnlyList<CalculatedGoodsReceiptLine> Lines,
    decimal NetAmount,
    decimal TaxAmount,
    decimal GrandTotal);

public static class GoodsReceiptCalculator
{
    public static GoodsReceiptCalculation Calculate(
        IEnumerable<(int LineNumber, Guid ProductId, string Description,
            decimal Quantity, decimal UnitCost, decimal DiscountAmount,
            string TaxCode, decimal TaxRate, PurchaseTaxTreatment TaxTreatment)> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var input = source.OrderBy(line => line.LineNumber).ToArray();
        if (input.Length == 0)
            throw new ArgumentException("A goods receipt requires at least one line.", nameof(source));
        if (input.Select(line => line.LineNumber).Distinct().Count() != input.Length)
            throw new ArgumentException("Goods receipt line numbers must be unique.", nameof(source));

        var lines = new List<CalculatedGoodsReceiptLine>(input.Length);
        foreach (var line in input)
        {
            if (line.LineNumber <= 0) throw new ArgumentOutOfRangeException(nameof(source), "Line numbers must be positive.");
            if (line.ProductId == Guid.Empty) throw new ArgumentException("A product is required on every line.", nameof(source));
            if (string.IsNullOrWhiteSpace(line.Description)) throw new ArgumentException("A description is required on every line.", nameof(source));
            if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(source), "Received quantity must be positive.");
            if (line.UnitCost < 0) throw new ArgumentOutOfRangeException(nameof(source), "Unit cost cannot be negative.");
            if (line.DiscountAmount < 0) throw new ArgumentOutOfRangeException(nameof(source), "Discount cannot be negative.");
            if (line.TaxRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(source), "Tax rate must be between zero and one hundred.");
            if (string.IsNullOrWhiteSpace(line.TaxCode)) throw new ArgumentException("A tax code is required on every line.", nameof(source));
            if (!Enum.IsDefined(line.TaxTreatment))
                throw new ArgumentException("The purchase tax treatment is not valid.", nameof(source));
            if (line.TaxRate == 0 && line.TaxTreatment != PurchaseTaxTreatment.NotApplicable)
                throw new ArgumentException("A zero-rated purchase line must use NotApplicable tax treatment.", nameof(source));
            if (line.TaxRate > 0 && line.TaxTreatment == PurchaseTaxTreatment.NotApplicable)
                throw new ArgumentException(
                    "A taxed purchase line must declare whether VAT is deductible or capitalized.",
                    nameof(source));

            var gross = Money(line.Quantity * line.UnitCost);
            if (line.DiscountAmount > gross)
                throw new ArgumentOutOfRangeException(nameof(source), "Discount cannot exceed the line gross amount.");
            var discount = Money(line.DiscountAmount);
            var net = Money(gross - discount);
            var tax = Money(net * line.TaxRate / 100m);
            lines.Add(new CalculatedGoodsReceiptLine(
                line.LineNumber,
                line.ProductId,
                line.Description.Trim(),
                Quantity(line.Quantity),
                UnitCost(line.UnitCost),
                discount,
                line.TaxCode.Trim().ToUpperInvariant(),
                Rate(line.TaxRate),
                line.TaxTreatment,
                net,
                tax,
                Money(net + tax)));
        }

        return new GoodsReceiptCalculation(
            lines,
            Money(lines.Sum(line => line.NetAmount)),
            Money(lines.Sum(line => line.TaxAmount)),
            Money(lines.Sum(line => line.LineTotal)));
    }

    private static decimal Money(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);
    private static decimal UnitCost(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static decimal Quantity(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
    private static decimal Rate(decimal value) => decimal.Round(value, 6, MidpointRounding.AwayFromZero);
}
