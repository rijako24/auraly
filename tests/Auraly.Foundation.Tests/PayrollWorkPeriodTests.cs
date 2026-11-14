using Auraly.Commerce.Payroll.Domain;

namespace Auraly.Foundation.Tests;

public sealed class PayrollWorkPeriodTests
{
    [Theory]
    [InlineData(2026, 2, 28)]
    [InlineData(2028, 2, 29)]
    [InlineData(2026, 1, 31)]
    public void Full_month_preserves_thirty_days(int year, int month, int lastDay)
    {
        var period = PayrollWorkPeriod.Create(new(year, month, 1), new(year, month, lastDay), 30, false);
        Assert.Equal(30, period.NominalDays);
        Assert.Equal(30, period.MaximumContractDays(period.Start, period.End));
    }

    [Theory]
    [InlineData(1, 15, 1, 15)]
    [InlineData(16, 28, 16, 15)]
    [InlineData(16, 28, 19, 12)]
    public void Half_month_and_contract_entry_use_the_same_nominal_basis(int start, int end, int entry, int expected)
    {
        var period = PayrollWorkPeriod.Create(new(2026, 2, start), new(2026, 2, end), 15, false);
        Assert.Equal(15, period.NominalDays);
        Assert.Equal(expected, period.MaximumContractDays(new(2026, 2, entry), null));
    }

    [Fact]
    public void Retirement_does_not_recognize_days_after_contract_end()
    {
        var period = PayrollWorkPeriod.Create(new(2026, 2, 1), new(2026, 2, 28), 30, false);
        Assert.Equal(18, period.MaximumContractDays(new(2026, 1, 1), new(2026, 2, 18)));
        Assert.Equal(12, period.MaximumContractDays(new(2026, 2, 19), null));
        Assert.Equal(0, period.MaximumContractDays(new(2026, 3, 1), null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(31)]
    public void Missing_or_unsupported_frequency_is_not_replaced_by_calendar_days(int? days)
        => Assert.Throws<PayrollCalculationException>(() => PayrollWorkPeriod.Create(new(2026, 2, 1), new(2026, 2, 28), days, false));

    [Fact]
    public void Adjustments_do_not_cause_another_base_salary()
    {
        var period = PayrollWorkPeriod.Create(new(2026, 2, 1), new(2026, 2, 28), 30, true);
        Assert.Equal(0, period.NominalDays);
        Assert.Equal(0, period.MaximumContractDays(period.Start, null));
    }

    [Theory]
    [InlineData(2026, 2, 1, 28, 30)]
    [InlineData(2028, 2, 1, 29, 30)]
    [InlineData(2026, 8, 1, 31, 30)]
    [InlineData(2026, 2, 1, 15, 15)]
    [InlineData(2026, 2, 16, 28, 15)]
    [InlineData(2028, 2, 16, 29, 15)]
    [InlineData(2026, 8, 16, 31, 15)]
    public void New_regular_drafts_use_the_contract_calendar(int year, int month, int first, int last, int days)
    {
        var period = PayrollWorkPeriod.CreateRegularDraft(new(year, month, first), new(year, month, last), days);
        Assert.Equal(days, period.NominalDays);
        Assert.Equal(days, period.MaximumContractDays(period.Start, period.End));
    }

    [Theory]
    [InlineData(1, 30, 30)]
    [InlineData(2, 31, 30)]
    [InlineData(1, 15, 30)]
    [InlineData(1, 31, 15)]
    [InlineData(2, 15, 15)]
    [InlineData(16, 30, 15)]
    public void New_regular_drafts_reject_arbitrary_ranges_without_reinterpreting_existing_periods(int first, int last, int days)
    {
        var start = new DateOnly(2026, 8, first); var end = new DateOnly(2026, 8, last);
        Assert.Throws<PayrollCalculationException>(() => PayrollWorkPeriod.CreateRegularDraft(start, end, days));
        Assert.Equal(start, PayrollWorkPeriod.Create(start, end, days, false).Start);
    }

    [Fact]
    public void New_regular_drafts_cannot_span_two_months()
        => Assert.Throws<PayrollCalculationException>(() => PayrollWorkPeriod.CreateRegularDraft(new(2026, 8, 1), new(2026, 9, 30), 30));
}
