using Auraly.Commerce.Accounting.Domain;

namespace Auraly.Foundation.Tests;

public sealed class AccountingJournalTests
{
    [Fact]
    public void An_existing_position_requires_its_opening_kind_and_cannot_also_name_a_new_origin()
    {
        var position = Guid.NewGuid();
        var debit = new JournalLine(Guid.NewGuid(), 100m, 0, Guid.NewGuid(), null,
            "Cartera conciliada", OpeningKind: JournalOpeningKind.Receivable, ExistingPositionId: position);
        var credit = new JournalLine(Guid.NewGuid(), 0, 100m, null, null, "Contrapartida");
        Assert.Equal(position, AccountingJournal.Validate([debit, credit])[0].ExistingPositionId);
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            debit with { ExistingPositionId = Guid.Empty }, credit]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            debit with { OpeningKind = JournalOpeningKind.None }, credit]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            debit with { OpeningDocumentId = Guid.NewGuid() }, credit]));
    }

    [Fact]
    public void Reconciled_openings_require_a_distinct_nonempty_identity_for_every_line()
    {
        var id = Guid.NewGuid();
        AccountingJournal.ValidateIdentifiedSourceLines([id, Guid.NewGuid()]);
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.ValidateIdentifiedSourceLines([id, null]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.ValidateIdentifiedSourceLines([id, Guid.Empty]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.ValidateIdentifiedSourceLines([id, id]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.ValidateSourceLineIdentities(3, [id]));
    }

    [Fact]
    public void An_opening_document_identity_requires_a_real_opening_and_survives_normalization()
    {
        var origin = Guid.NewGuid();
        var debit = new JournalLine(Guid.NewGuid(), 100.12345m, 0, Guid.NewGuid(), null,
            "Apertura de una venta corregida", OpeningKind: JournalOpeningKind.Receivable, OpeningDocumentId: origin);
        var credit = new JournalLine(Guid.NewGuid(), 0, 100.12345m, null, null, "Contrapartida");
        var normalized = AccountingJournal.Validate([debit, credit]);
        Assert.Equal(origin, normalized[0].OpeningDocumentId);
        Assert.Equal(100.1235m, normalized[0].Debit);
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            debit with { OpeningDocumentId = Guid.Empty }, credit]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            debit with { OpeningKind = JournalOpeningKind.None }, credit]));
    }

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
    public void Lines_are_balanced_at_the_same_four_decimal_precision_persisted_by_sql()
    {
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            new(Guid.NewGuid(), 0.00005m, 0, null, null, "Primera fraccion"),
            new(Guid.NewGuid(), 0.00005m, 0, null, null, "Segunda fraccion"),
            new(Guid.NewGuid(), 0, 0.0001m, null, null, "Contrapartida")]));

        var lines = AccountingJournal.Validate([
            new(Guid.NewGuid(), 10.12345m, 0, null, null, "Debito"),
            new(Guid.NewGuid(), 0, 10.12345m, null, null, "Credito")]);

        Assert.All(lines, line =>
            Assert.True(decimal.Round(line.Debit, 4) == line.Debit &&
                        decimal.Round(line.Credit, 4) == line.Credit));
    }

    [Fact]
    public void Accounting_period_cannot_span_calendar_years()
    {
        Assert.Throws<AccountingRuleException>(() => AccountingPeriodRules.Validate(
            new DateOnly(2026, 12, 1), new DateOnly(2027, 1, 31)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("0", "0")]
    [InlineData("12.34565", "12.3457")]
    [InlineData("-12.34565", "-12.3457")]
    [InlineData("999999999999999.9999", "999999999999999.9999")]
    [InlineData("-999999999999999.9999", "-999999999999999.9999")]
    public void Recorded_bases_preserve_unknown_zero_and_sign_at_accounting_precision(string? recorded, string? expected)
    {
        static decimal? Parse(string? value) => value is null ? null
            : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var lines = AccountingJournal.Validate([
            new(Guid.NewGuid(), 100m, 0m, null, null, "Base documental", BaseAmount: Parse(recorded)),
            new(Guid.NewGuid(), 0m, 100m, null, null, "Sin base registrada")]);
        Assert.Equal(Parse(expected), lines[0].BaseAmount);
        Assert.Null(lines[1].BaseAmount);
        Assert.Equal(100m, lines.Sum(line => line.Debit));
        Assert.Equal(100m, lines.Sum(line => line.Credit));
    }

    [Theory]
    [InlineData("1000000000000000")]
    [InlineData("-1000000000000000")]
    public void A_recorded_base_must_fit_its_persisted_decimal_range(string recorded)
    {
        var value = decimal.Parse(recorded, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            new(Guid.NewGuid(), 100m, 0m, null, null, "Base fuera de rango", BaseAmount: value),
            new(Guid.NewGuid(), 0m, 100m, null, null, "Contrapartida")]));
    }

    [Theory]
    [InlineData("1000000000000000", 1)]
    [InlineData("600000000000000", 2)]
    public void Lines_and_entry_totals_must_fit_the_persisted_money_range(string value, int pairCount)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var lines = Enumerable.Range(0, pairCount).SelectMany(_ => new[]
        {
            new JournalLine(Guid.NewGuid(), amount, 0, null, null, "Débito"),
            new JournalLine(Guid.NewGuid(), 0, amount, null, null, "Crédito")
        });
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate(lines));
    }

    [Fact]
    public void The_maximum_persistable_balanced_entry_is_valid()
    {
        const decimal amount = 999_999_999_999_999.9999m;
        var lines = AccountingJournal.Validate([
            new(Guid.NewGuid(), amount, 0, null, null, "Débito máximo"),
            new(Guid.NewGuid(), 0, amount, null, null, "Crédito máximo")]);
        Assert.Equal(amount, lines[0].Debit);
        Assert.Equal(amount, lines[1].Credit);
    }

    [Theory]
    [InlineData(JournalOpeningKind.Payable, true)]
    [InlineData(JournalOpeningKind.Receivable, false)]
    [InlineData(JournalOpeningKind.SupplierCredit, false)]
    [InlineData(JournalOpeningKind.CustomerCredit, true)]
    public void Opening_metadata_requires_a_party_and_the_correct_side(JournalOpeningKind kind, bool credit)
    {
        var opening = new JournalLine(Guid.NewGuid(), credit ? 0 : 100m, credit ? 100m : 0,
            Guid.NewGuid(), null, "Obligación", OpeningKind: kind);
        var counterpart = new JournalLine(Guid.NewGuid(), opening.Credit, opening.Debit,
            null, null, "Contrapartida");
        Assert.Equal(kind, AccountingJournal.Validate([opening, counterpart])[0].OpeningKind);
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            opening with { PartyId = null }, counterpart]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            opening with { Debit = opening.Credit, Credit = opening.Debit },
            counterpart with { Debit = counterpart.Credit, Credit = counterpart.Debit }]));
        Assert.Throws<AccountingRuleException>(() => AccountingJournal.Validate([
            opening with { OpeningKind = (JournalOpeningKind)99 }, counterpart]));
    }
}
