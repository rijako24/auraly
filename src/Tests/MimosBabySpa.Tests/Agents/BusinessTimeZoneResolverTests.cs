using FluentAssertions;
using MimosBabySpa.Application.Time;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class BusinessTimeZoneResolverTests
{
    [Fact]
    public void Resolve_AmericaBogota_ReturnsValidTimeZone()
    {
        var tz = BusinessTimeZoneResolver.Resolve("America/Bogota");

        tz.Should().NotBeNull();
        tz.GetUtcOffset(new DateTime(2026, 6, 26)).Should().Be(TimeSpan.FromHours(-5));
    }

    [Fact]
    public void Resolve_UnknownId_FallsBackToBogotaWithoutThrowing()
    {
        var tz = BusinessTimeZoneResolver.Resolve("Not/A_Real_Zone");

        tz.Should().NotBeNull();
        tz.GetUtcOffset(new DateTime(2026, 6, 26)).Should().Be(TimeSpan.FromHours(-5));
    }
    [Fact]
    public void Resolve_NullId_FallsBackToBogota()
    {
        var tz = BusinessTimeZoneResolver.Resolve(null);

        tz.Should().NotBeNull();
        tz.GetUtcOffset(new DateTime(2026, 6, 26)).Should().Be(TimeSpan.FromHours(-5));
    }
}
