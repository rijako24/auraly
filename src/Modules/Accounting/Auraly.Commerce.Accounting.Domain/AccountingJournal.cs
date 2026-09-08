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

        // SQL persists every monetary line as decimal(19,4). Validate and return
        // that exact representation so a value cannot balance in memory and
        // become unbalanced after each individual line is rounded by SQL Server.
        lines = lines.Select(line => line with
        {
            Debit = decimal.Round(line.Debit, 4, MidpointRounding.AwayFromZero),
            Credit = decimal.Round(line.Credit, 4, MidpointRounding.AwayFromZero)
        }).ToArray();
        if (lines.Any(line => (line.Debit > 0) == (line.Credit > 0)))
            throw new AccountingRuleException(
                "Each accounting line must remain on exactly one positive side at accounting precision.");

        var debit = lines.Sum(line => line.Debit);
        var credit = lines.Sum(line => line.Credit);
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
