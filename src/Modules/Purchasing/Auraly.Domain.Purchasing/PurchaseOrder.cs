namespace Auraly.Domain.Purchasing;

public sealed record CalculatedPurchaseOrderLine(
    Guid LineId, int LineNumber, Guid ProductId, string Description,
    decimal OrderedQuantity, decimal UnitCost, decimal DiscountAmount,
    string TaxCode, decimal TaxRate, string TaxTreatment,
    decimal NetAmount, decimal TaxAmount, decimal LineTotal);

public sealed record PurchaseOrderCalculation(
    IReadOnlyList<CalculatedPurchaseOrderLine> Lines,
    decimal NetAmount, decimal TaxAmount, decimal GrandTotal);

public static class PurchaseOrderCalculator
{
    public static PurchaseOrderCalculation Calculate(IEnumerable<(
        Guid LineId, int LineNumber, Guid ProductId, string Description,
        decimal Quantity, decimal UnitCost, decimal DiscountAmount,
        string TaxCode, decimal TaxRate, string TaxTreatment)> input)
    {
        var source = input.ToArray();
        if (source.Length == 0) throw new ArgumentException("At least one purchase-order line is required.");
        if (source.Select(x => x.LineId).Distinct().Count() != source.Length)
            throw new ArgumentException("Purchase-order line identifiers must be unique.");
        if (source.Select(x => x.LineNumber).Order().Where((number, index) => number != index + 1).Any())
            throw new ArgumentException("Purchase-order line numbers must be consecutive from one.");

        var lines = source.Select(line =>
        {
            if (line.LineId == Guid.Empty || line.ProductId == Guid.Empty)
                throw new ArgumentException("Every purchase-order line requires identifiers.");
            if (line.Quantity <= 0 || line.UnitCost < 0 || line.DiscountAmount < 0)
                throw new ArgumentException("Quantities must be positive and costs cannot be negative.");
            if (line.TaxRate is < 0 or > 100)
                throw new ArgumentException("TaxRate must be between zero and one hundred.");
            var gross = decimal.Round(line.Quantity * line.UnitCost, 4, MidpointRounding.AwayFromZero);
            if (line.DiscountAmount > gross)
                throw new ArgumentException("Discount cannot exceed the line gross amount.");
            var net = gross - line.DiscountAmount;
            var tax = decimal.Round(net * line.TaxRate / 100m, 4, MidpointRounding.AwayFromZero);
            return new CalculatedPurchaseOrderLine(line.LineId, line.LineNumber, line.ProductId,
                line.Description.Trim(), line.Quantity, line.UnitCost, line.DiscountAmount,
                line.TaxCode.Trim(), line.TaxRate, line.TaxTreatment, net, tax, net + tax);
        }).ToArray();
        var net = lines.Sum(x => x.NetAmount);
        var tax = lines.Sum(x => x.TaxAmount);
        return new(lines, net, tax, net + tax);
    }

    public static decimal Remaining(decimal ordered, decimal received, decimal cancelled) =>
        Math.Max(0m, ordered - received - cancelled);

    public static string Status(IEnumerable<(decimal Ordered, decimal Received, decimal Cancelled)> lines)
    {
        var values = lines.ToArray();
        if (values.All(x => Remaining(x.Ordered, x.Received, x.Cancelled) == 0))
            return "Received";
        return values.Any(x => x.Received > 0)
            ? "PartiallyReceived"
            : "Open";
    }
}
