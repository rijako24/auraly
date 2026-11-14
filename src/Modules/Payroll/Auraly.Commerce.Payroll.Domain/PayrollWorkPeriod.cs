namespace Auraly.Commerce.Payroll.Domain;

public sealed record PayrollWorkPeriod
{
    public DateOnly Start { get; }
    public DateOnly End { get; }
    public int NominalDays { get; }

    private PayrollWorkPeriod(DateOnly start, DateOnly end, int nominalDays)
        => (Start, End, NominalDays) = (start, end, nominalDays);

    public static PayrollWorkPeriod Create(DateOnly start, DateOnly end, int? frequencyDays, bool adjustment)
    {
        if (start == default || end < start)
            throw new PayrollCalculationException("El período de liquidación no es válido.");
        if (frequencyDays is not (15 or 30))
            throw new PayrollCalculationException("La periodicidad debe tener configurados 15 o 30 días de liquidación.");
        return new(start, end, adjustment ? 0 : frequencyDays.Value);
    }

    public static PayrollWorkPeriod CreateRegularDraft(DateOnly start, DateOnly end, int? frequencyDays)
    {
        var period = Create(start, end, frequencyDays, false);
        var sameMonth = start.Year == end.Year && start.Month == end.Month;
        var closesMonth = end.Day == DateTime.DaysInMonth(end.Year, end.Month);
        var valid = sameMonth && (frequencyDays == 30
            ? start.Day == 1 && closesMonth
            : start.Day == 1 && end.Day == 15 || start.Day == 16 && closesMonth);
        if (!valid)
            throw new PayrollCalculationException("Selecciona el mes completo para pago mensual, o los días 1–15 o 16–fin de mes para pago quincenal.");
        return period;
    }

    public decimal MaximumContractDays(DateOnly employmentStart, DateOnly? employmentEnd)
    {
        var start = employmentStart > Start ? employmentStart : Start;
        var end = employmentEnd.HasValue && employmentEnd.Value < End ? employmentEnd.Value : End;
        if (end < start || NominalDays == 0) return 0;
        // A complete month remains thirty days; February's last day closes that
        // nominal month too. Dates themselves are never changed or invented.
        var startDay = Math.Min(start.Day, 30);
        var endDay = end.Day == DateTime.DaysInMonth(end.Year, end.Month) ? 30 : Math.Min(end.Day, 30);
        var days = ((end.Year - start.Year) * 12 + end.Month - start.Month) * 30 + endDay - startDay + 1;
        return Math.Clamp(days, 0, NominalDays);
    }
}
