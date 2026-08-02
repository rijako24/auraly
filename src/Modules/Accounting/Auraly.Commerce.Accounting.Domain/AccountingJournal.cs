namespace Auraly.Commerce.Accounting.Domain;

public sealed record JournalLine(
    Guid AccountId,
    decimal Debit,
    decimal Credit,
    Guid? PartyId,
    Guid? CostCenterId,
    string Description);

public static class AccountingJournal
{
    public static IReadOnlyList<JournalLine> Validate(
        IEnumerable<JournalLine> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lines = source.ToArray();
        if (lines.Length < 2)
            throw new AccountingRuleException(
                "An accounting entry requires at least two lines.");
        if (lines.Any(line =>
                line.AccountId == Guid.Empty ||
                line.Debit < 0 || line.Credit < 0 ||
                line.Debit > 0 && line.Credit > 0 ||
                line.Debit == 0 && line.Credit == 0 ||
                string.IsNullOrWhiteSpace(line.Description)))
            throw new AccountingRuleException(
                "Each accounting line must have one positive side and a description.");

        var debit = decimal.Round(lines.Sum(line => line.Debit), 4);
        var credit = decimal.Round(lines.Sum(line => line.Credit), 4);
        if (debit <= 0 || debit != credit)
            throw new AccountingRuleException(
                $"The accounting entry is not balanced: debit {debit} and credit {credit}.");
        return lines;
    }
}

public static class AccountingPeriodRules
{
    public static void Validate(DateOnly startsOn, DateOnly endsOn)
    {
        if (startsOn == default || endsOn == default || endsOn < startsOn)
            throw new AccountingRuleException(
                "The accounting period date range is invalid.");
        if (startsOn.Year != endsOn.Year)
            throw new AccountingRuleException(
                "An accounting period cannot span calendar years.");
    }
}

public sealed class AccountingRuleException(string message) : Exception(message);
