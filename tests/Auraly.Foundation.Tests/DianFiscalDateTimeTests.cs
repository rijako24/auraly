using Auraly.Fiscal.Core;

namespace Auraly.Foundation.Tests;

public sealed class DianFiscalDateTimeTests
{
    [Fact]
    public void Referenced_document_date_uses_the_same_Colombian_calendar_day_as_its_UBL()
    {
        var issuedAtUtc = new DateTimeOffset(2026, 9, 16, 1, 16, 14, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 9, 15), DianFiscalDateTime.DateInColombia(issuedAtUtc));
    }
}
