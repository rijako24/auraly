using Auraly.Commerce.Accounting.Domain;

namespace Auraly.Foundation.Tests;

public sealed class AccountingJournalTests
{
    [Fact]
    public void Balanced_double_entry_is_accepted()
    {
        var lines = AccountingJournal.Validate([
            new(Guid.NewGuid(), 11900m, 0, null, null, "Venta"),
            new(Guid.NewGuid(), 0, 10000m, null, null, "Ingreso"),
            new(Guid.NewGuid(), 0, 1900m, null, null, "IVA")]);
        Assert.Equal(11900m, lines.Sum(line => line.Debit));
        Assert.Equal(11900m, lines.Sum(line => line.Credit));
    }

    [Fact]
    public void Unbalanced_or_two_sided_lines_are_rejected()
    {
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            new(Guid.NewGuid(), 100m, 0, null, null, "Debito"),
            new(Guid.NewGuid(), 0, 99m, null, null, "Credito")]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            new(Guid.NewGuid(), 100m, 1m, null, null, "Dos lados"),
            new(Guid.NewGuid(), 0, 99m, null, null, "Credito")]));
    }

    [Fact]
    public void Accounting_period_cannot_span_calendar_years()
    {
        Assert.Throws<AccountingRuleException>(() => AccountingPeriodRules.Validate(
            new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 31)));
    }
}
