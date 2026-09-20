using Auraly.BuildingBlocks.Domain.Money;

namespace Auraly.Domain.Sales;

// Stable calculation identities; labels and selectable options belong to reference.Options.
public static class InvoiceChargeCalculationModes
{
    public const string Manual = "Manual";
    public const string Fixed = "Fixed";
    public const string Percentage = "Percentage";
    public const string Ranges = "Ranges";
}

public static class InvoiceChargeInclusionModes
{
    public const string Always = "Always";
    public const string Never = "Never";
    public const string UpToInvoiceAmount = "UpToInvoiceAmount";
}

public sealed record InvoiceChargeRange(decimal FromInclusive, decimal? ToExclusive,
    string CalculationMode, decimal Value);

public sealed record InvoiceChargeRule(string CalculationMode, decimal? Value,
    string InclusionMode, decimal? InvoiceAmountLimit, IReadOnlyList<InvoiceChargeRange> Ranges);

public sealed record InvoiceChargeAmounts(decimal InvoiceBase, decimal Amount,
    decimal InvoicedAmount, decimal ExpenseAmount);

public sealed record InvoiceChargePaymentShare(int PaymentNumber, decimal AppliedAmount);
public sealed record InvoiceChargeAllocation(int PaymentNumber, decimal Amount);

/// <summary>
/// Pure sales policy shared by online and Edge. Its input is the product total
/// after discounts and tax, before charges and withholding. It never reads live
/// configuration when a confirmed document is replayed.
/// </summary>
public static class InvoiceChargeCalculation
{
    public const int MaximumRanges = 32;

    public static InvoiceChargeAmounts Calculate(InvoiceChargeRule rule,
        decimal invoiceBase, decimal? manualAmount = null)
    {
        Validate(rule);
        RequireMoney(invoiceBase, "La base de la factura");
        decimal amount;
        if (rule.CalculationMode == InvoiceChargeCalculationModes.Manual)
        {
            if (manualAmount is null)
                throw new InvoiceChargeRuleException("Digita el valor del cargo.");
            RequireMoney(manualAmount.Value, "El valor digitado");
            amount = manualAmount.Value;
        }
        else
        {
            if (manualAmount is not null)
                throw new InvoiceChargeRuleException("El cargo automático no admite un valor digitado.");
            var range = rule.CalculationMode == InvoiceChargeCalculationModes.Ranges
                ? rule.Ranges.Single(item => invoiceBase >= item.FromInclusive &&
                    (item.ToExclusive is null || invoiceBase < item.ToExclusive))
                : null;
            var mode = range?.CalculationMode ?? rule.CalculationMode;
            var value = range?.Value ?? rule.Value!.Value;
            amount = mode == InvoiceChargeCalculationModes.Fixed
                ? value : MonetaryRounding.RoundLineAmount(invoiceBase * value / 100m);
        }
        RequireMoney(amount, "El valor calculado");
        var includeInInvoice = rule.InclusionMode == InvoiceChargeInclusionModes.Always ||
            (rule.InclusionMode == InvoiceChargeInclusionModes.UpToInvoiceAmount &&
                invoiceBase <= rule.InvoiceAmountLimit!.Value);
        return new(invoiceBase, amount, includeInInvoice ? amount : 0m, includeInInvoice ? 0m : amount);
    }

    public static void Validate(InvoiceChargeRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (rule.InclusionMode is not (InvoiceChargeInclusionModes.Always or
            InvoiceChargeInclusionModes.Never or InvoiceChargeInclusionModes.UpToInvoiceAmount))
            throw new InvoiceChargeRuleException("La regla para incluir el cargo en la factura no es válida.");
        if (rule.InclusionMode == InvoiceChargeInclusionModes.UpToInvoiceAmount)
        {
            if (rule.InvoiceAmountLimit is null)
                throw new InvoiceChargeRuleException("Configura el importe máximo de factura para incluir el cargo.");
            RequireMoney(rule.InvoiceAmountLimit.Value, "El umbral");
        }
        else if (rule.InvoiceAmountLimit is not null)
            throw new InvoiceChargeRuleException("Solo la regla por umbral admite un valor límite.");

        if (rule.Ranges is null || rule.Ranges.Count > MaximumRanges || rule.Ranges.Any(range => range is null))
            throw new InvoiceChargeRuleException($"Se admiten hasta {MaximumRanges} rangos.");
        if (rule.CalculationMode == InvoiceChargeCalculationModes.Ranges)
        {
            if (rule.Value is not null || rule.Ranges.Count == 0)
                throw new InvoiceChargeRuleException("Configura las tarifas por rango sin un valor general.");
            decimal next = 0;
            for (var index = 0; index < rule.Ranges.Count; index++)
            {
                var range = rule.Ranges[index];
                RequireMoney(range.FromInclusive, "El inicio del rango");
                if (range.FromInclusive != next ||
                    (index == rule.Ranges.Count - 1) != (range.ToExclusive is null))
                    throw new InvoiceChargeRuleException("Los rangos deben cubrir desde cero, sin saltos ni cruces, y terminar sin límite.");
                if (range.ToExclusive is { } upper)
                {
                    RequireMoney(upper, "El fin del rango");
                    if (upper <= range.FromInclusive)
                        throw new InvoiceChargeRuleException("El fin del rango debe superar su inicio.");
                    next = upper;
                }
                ValidateTariff(range.CalculationMode, range.Value);
            }
        }
        else
        {
            if (rule.Ranges.Count != 0)
                throw new InvoiceChargeRuleException("Los rangos solo se admiten en el cálculo por rangos.");
            if (rule.CalculationMode == InvoiceChargeCalculationModes.Manual)
            {
                if (rule.Value is { } suggested) RequireMoney(suggested, "El valor sugerido");
            }
            else if (rule.Value is { } value) ValidateTariff(rule.CalculationMode, value);
            else throw new InvoiceChargeRuleException("Configura el valor o porcentaje del cargo.");
        }
    }

    // PaymentNumber=0 represents the credit balance; real payments retain their
    // existing numbers. Allocation uses applied money, never tendered cash/change.
    public static IReadOnlyList<InvoiceChargeAllocation> Allocate(decimal customerAmount,
        IReadOnlyList<InvoiceChargePaymentShare> payments)
    {
        RequireMoney(customerAmount, "El cargo incluido en la factura");
        if (payments.Count is < 1 or > 11 || payments.Any(item => item.PaymentNumber < 0 || item.AppliedAmount <= 0) ||
            payments.Select(item => item.PaymentNumber).Distinct().Count() != payments.Count)
            throw new InvoiceChargeRuleException("La distribución requiere medios de pago únicos con valores positivos.");
        foreach (var payment in payments) RequireMoney(payment.AppliedAmount, "El pago aplicado");
        var total = payments.Sum(item => item.AppliedAmount);
        if (customerAmount > total)
            throw new InvoiceChargeRuleException("El cargo no puede superar el importe distribuido.");
        var ordered = payments.OrderBy(item => item.PaymentNumber).ToArray();
        var result = new List<InvoiceChargeAllocation>(ordered.Length);
        decimal cumulative = 0, allocated = 0;
        foreach (var payment in ordered)
        {
            cumulative += payment.AppliedAmount;
            var target = MonetaryRounding.RoundLineAmount(customerAmount * cumulative / total);
            result.Add(new(payment.PaymentNumber, target - allocated));
            allocated = target;
        }
        return result;
    }

    private static void ValidateTariff(string mode, decimal value)
    {
        if (mode == InvoiceChargeCalculationModes.Fixed) RequireMoney(value, "La tarifa fija");
        else if (mode != InvoiceChargeCalculationModes.Percentage || value is < 0 or > 100 || decimal.Round(value, 6) != value)
            throw new InvoiceChargeRuleException("La tarifa debe ser fija o un porcentaje entre 0 y 100, con hasta seis decimales.");
    }

    private static void RequireMoney(decimal amount, string label)
    {
        if (amount is < 0 or > 999999999999m || MonetaryRounding.RoundLineAmount(amount) != amount)
            throw new InvoiceChargeRuleException($"{label} debe ser un valor monetario no negativo, con hasta dos decimales.");
    }
}

public sealed class InvoiceChargeRuleException(string message) : Exception(message);
