namespace Auraly.Commerce.Accounting.Domain;

// Posting metadata, not an account classification or a user-selectable catalog.
public enum JournalOpeningKind { None, Payable, Receivable, SupplierCredit, CustomerCredit }

public sealed record JournalLine(
    Guid AccountId,
    decimal Debit,
    decimal Credit,
    Guid? PartyId,
    Guid? CostCenterId,
    string Description,
    Guid? SourceLineId = null,
    JournalOpeningKind OpeningKind = JournalOpeningKind.None,
    decimal? BaseAmount = null,
    Guid? OpeningDocumentId = null,
    Guid? ExistingPositionId = null);

public static class AccountingJournal
{
    private const decimal MaximumStoredAmount = 999_999_999_999_999.9999m; // SQL decimal(19,4).

    public static IReadOnlyList<JournalLine> Validate(
        IEnumerable<JournalLine> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var lines = source.ToArray();
        if (lines.Length < 2)
            throw new AccountingRuleException(
                "An accounting entry requires at least two lines.");
        ValidateLineIdentityUniqueness(lines.Select(line => line.SourceLineId));
        if (lines.Any(line => !Enum.IsDefined(line.OpeningKind) ||
                line.OpeningDocumentId == Guid.Empty ||
                line.ExistingPositionId == Guid.Empty ||
                line.ExistingPositionId.HasValue && (line.OpeningKind == JournalOpeningKind.None || line.OpeningDocumentId.HasValue) ||
                line.OpeningDocumentId.HasValue && line.OpeningKind == JournalOpeningKind.None ||
                line.OpeningKind != JournalOpeningKind.None && line.PartyId is null ||
                (line.OpeningKind is JournalOpeningKind.Payable or JournalOpeningKind.CustomerCredit) && line.Credit <= 0 ||
                (line.OpeningKind is JournalOpeningKind.Receivable or JournalOpeningKind.SupplierCredit) && line.Debit <= 0))
            throw new AccountingRuleException("A subledger opening requires its party and the corresponding journal side.");
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
            Credit = decimal.Round(line.Credit, 4, MidpointRounding.AwayFromZero),
            BaseAmount = line.BaseAmount is { } recordedBase
                ? decimal.Round(recordedBase, 4, MidpointRounding.AwayFromZero) : null
        }).ToArray();
        if (lines.Any(line => (line.Debit > 0) == (line.Credit > 0)))
            throw new AccountingRuleException(
                "Each accounting line must remain on exactly one positive side at accounting precision.");
        if (lines.Any(line => line.Debit > MaximumStoredAmount || line.Credit > MaximumStoredAmount ||
                line.BaseAmount is { } recordedBase && (recordedBase > MaximumStoredAmount || recordedBase < -MaximumStoredAmount)))
            throw new AccountingRuleException("Each accounting line must fit the persisted monetary range.");

        var debit = lines.Sum(line => line.Debit);
        var credit = lines.Sum(line => line.Credit);
        if (debit > MaximumStoredAmount || credit > MaximumStoredAmount)
            throw new AccountingRuleException("The accounting entry total exceeds the persisted monetary range.");
        if (debit <= 0 || debit != credit)
            throw new AccountingRuleException(
                $"The accounting entry is not balanced: debit {debit} and credit {credit}.");
        return lines;
    }

    public static void ValidateSourceLineIdentities(int? payloadVersion, IEnumerable<Guid?> source)
    {
        var lineIds = source.ToArray();
        if (payloadVersion is not (null or 2) ||
            payloadVersion == 2 && lineIds.Any(id => id is null) ||
            payloadVersion is null && lineIds.Any(id => id.HasValue))
            throw new AccountingRuleException("The payload version and its source line identities are incompatible.");
        ValidateLineIdentityUniqueness(lineIds);
    }

    public static void ValidateIdentifiedSourceLines(IEnumerable<Guid?> source)
    {
        var identities = source.ToArray();
        if (identities.Any(id => id is null))
            throw new AccountingRuleException("Every reconciled opening line requires its source identity.");
        ValidateLineIdentityUniqueness(identities);
    }

    private static void ValidateLineIdentityUniqueness(IEnumerable<Guid?> source)
    {
        var identified = source.Where(id => id.HasValue).ToArray();
        if (identified.Any(id => id == Guid.Empty) || identified.Distinct().Count() != identified.Length)
            throw new AccountingRuleException("Source line identities must be nonempty and unique within an entry.");
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
