namespace Auraly.Fiscal.Core;

public static class DianFiscalDateTime
{
    public static readonly TimeSpan ColombiaOffset = TimeSpan.FromHours(-5);

    public static DateTimeOffset InColombia(DateTimeOffset value) =>
        value.ToOffset(ColombiaOffset);
}
