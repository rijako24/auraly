namespace Auraly.Domain.Expenses;

public sealed record ExpenseAmounts(decimal TaxExclusiveAmount, decimal VatAmount, decimal GrossAmount)
{
    public static ExpenseAmounts Create(decimal taxExclusiveAmount, decimal vatAmount)
    {
        var net = decimal.Round(taxExclusiveAmount, 4, MidpointRounding.AwayFromZero);
        var vat = decimal.Round(vatAmount, 4, MidpointRounding.AwayFromZero);
        if (net <= 0 || vat < 0) throw new ExpenseRuleException("El valor antes de impuestos debe ser mayor que cero y el IVA no puede ser negativo.");
        return new(net, vat, checked(net + vat));
    }
}

public sealed class ExpenseRuleException(string message) : Exception(message);
