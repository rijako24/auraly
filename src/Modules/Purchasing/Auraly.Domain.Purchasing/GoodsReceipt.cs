namespace Auraly.Domain.Purchasing;

public enum PurchaseTaxTreatment
{
    DeductibleInputVat,
    CapitalizedCost,
    NotApplicable
}

public sealed record GoodsReceiptLineInput(
    int LineNumber, Guid ProductId, string Description, decimal Quantity,
    decimal UnitCost, decimal DiscountAmount, string TaxCode, decimal TaxRate,
    PurchaseTaxTreatment TaxTreatment, decimal? CommercialDiscountPercent = null);

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
    decimal LineTotal,
    decimal? CommercialDiscountPercent = null);

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
        return Calculate(source.Select(line => new GoodsReceiptLineInput(
            line.LineNumber, line.ProductId, line.Description, line.Quantity,
            line.UnitCost, line.DiscountAmount, line.TaxCode, line.TaxRate,
            line.TaxTreatment)));
    }

    public static GoodsReceiptCalculation Calculate(IEnumerable<GoodsReceiptLineInput> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var input = source.OrderBy(line => line.LineNumber).ToArray();
        if (input.Length == 0)
            throw new ArgumentException("La recepción requiere al menos un producto.", nameof(source));
        if (input.Select(line => line.LineNumber).Distinct().Count() != input.Length)
            throw new ArgumentException("Los números de línea de la recepción deben ser únicos.", nameof(source));

        var lines = new List<CalculatedGoodsReceiptLine>(input.Length);
        foreach (var line in input)
        {
            if (line.LineNumber <= 0) throw new ArgumentOutOfRangeException(nameof(source), "Los números de línea deben ser positivos.");
            if (line.ProductId == Guid.Empty) throw new ArgumentException("Cada línea requiere un producto.", nameof(source));
            if (string.IsNullOrWhiteSpace(line.Description)) throw new ArgumentException("Cada línea requiere una descripción.", nameof(source));
            var description = line.Description.Trim();
            if (description.Length > 250)
                throw new ArgumentException("La descripción del producto no puede superar 250 caracteres.", nameof(source));
            if (line.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(source), "La cantidad recibida debe ser positiva.");
            if (line.UnitCost < 0) throw new ArgumentOutOfRangeException(nameof(source), "El costo unitario no puede ser negativo.");
            if (line.DiscountAmount < 0) throw new ArgumentOutOfRangeException(nameof(source), "El descuento no puede ser negativo.");
            if (line.CommercialDiscountPercent is < 0 or > 100)
                throw new ArgumentOutOfRangeException(nameof(source), "El descuento comercial debe estar entre 0 y 100 %.");
            if (line.TaxRate is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(source), "La tarifa del impuesto debe estar entre 0 y 100.");
            if (string.IsNullOrWhiteSpace(line.TaxCode)) throw new ArgumentException("Cada línea requiere un código tributario.", nameof(source));
            var taxCode = line.TaxCode.Trim().ToUpperInvariant();
            if (taxCode.Length > 32)
                throw new ArgumentException("El código tributario no puede superar 32 caracteres.", nameof(source));
            if (!Enum.IsDefined(line.TaxTreatment))
                throw new ArgumentException("El tratamiento del IVA de compra es inválido.", nameof(source));
            if (line.TaxRate == 0 && line.TaxTreatment != PurchaseTaxTreatment.NotApplicable)
                throw new ArgumentException("Una línea con tarifa cero debe marcar el IVA como no aplicable.", nameof(source));
            if (line.TaxRate > 0 && line.TaxTreatment == PurchaseTaxTreatment.NotApplicable)
                throw new ArgumentException(
                    "Una línea gravada debe indicar si el IVA es descontable o se incluye en el costo.",
                    nameof(source));

            var gross = Money(line.Quantity * line.UnitCost);
            if (line.DiscountAmount > gross)
                throw new ArgumentOutOfRangeException(nameof(source), "El descuento no puede superar el valor bruto de la línea.");
            var percent = line.CommercialDiscountPercent is { } percentage ? Rate(percentage) : (decimal?)null;
            var discount = percent is { } rate ? Money(gross * rate / 100m) : Money(line.DiscountAmount);
            if (percent.HasValue && line.DiscountAmount != 0 && Money(line.DiscountAmount) != discount)
                throw new ArgumentException("El importe y el porcentaje del descuento comercial no coinciden.", nameof(source));
            var net = Money(gross - discount);
            var tax = Money(net * line.TaxRate / 100m);
            lines.Add(new CalculatedGoodsReceiptLine(
                line.LineNumber,
                line.ProductId,
                description,
                Quantity(line.Quantity),
                UnitCost(line.UnitCost),
                discount,
                taxCode,
                Rate(line.TaxRate),
                line.TaxTreatment,
                net,
                tax,
                Money(net + tax), percent));
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
