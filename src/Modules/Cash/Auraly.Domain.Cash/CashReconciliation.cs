using Auraly.Contracts.Cash;

namespace Auraly.Domain.Cash;

public static class CashReconciliation
{
    public static IReadOnlyList<CashReconciliationLine> Calculate(
        IReadOnlyDictionary<string, decimal> expected,
        IReadOnlyCollection<CashCountLineInput> counted)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(counted);

        var normalized = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in counted)
        {
            var method = line.PaymentMethodCode?.Trim();
            if (string.IsNullOrWhiteSpace(method))
                throw new ArgumentException("Payment method is required.", nameof(counted));
            if (line.CountedAmount < 0)
                throw new ArgumentOutOfRangeException(nameof(counted), "Counted amounts cannot be negative.");
            if (!normalized.TryAdd(method, line.CountedAmount))
                throw new ArgumentException($"Payment method '{method}' is repeated.", nameof(counted));
        }

        var methods = expected.Keys
            .Concat(normalized.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase);

        return methods.Select(method =>
        {
            var expectedAmount = expected.GetValueOrDefault(method);
            var countedAmount = normalized.GetValueOrDefault(method);
            return new CashReconciliationLine(
                method,
                expectedAmount,
                countedAmount,
                countedAmount - expectedAmount);
        }).ToArray();
    }
}
