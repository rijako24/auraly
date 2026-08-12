namespace Auraly.Domain.Payables;

public sealed record PayableOpening(decimal OriginalAmount, decimal OutstandingAmount, DateTimeOffset DueDate)
{
    public static PayableOpening Create(
        decimal amount,
        DateTimeOffset receivedAt,
        DateTimeOffset? dueDate)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (dueDate is null) throw new ArgumentNullException(nameof(dueDate));
        if (dueDate < receivedAt) throw new ArgumentOutOfRangeException(nameof(dueDate));
        var rounded = decimal.Round(amount, 4, MidpointRounding.AwayFromZero);
        return new PayableOpening(rounded, rounded, dueDate.Value);
    }
}
