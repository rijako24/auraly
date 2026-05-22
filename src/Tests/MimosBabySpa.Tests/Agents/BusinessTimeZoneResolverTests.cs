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
        tz.Id.Should().BeOneOf(
            "America/Bogota",
            "SA Pacific Standard Time",
            "UTC");
    }

    [Fact]
    public void Resolve_UnknownId_FallsBackWithoutThrowing()
    {
        var tz = BusinessTimeZoneResolver.Resolve("Not/A_Real_Zone");

        tz.Should().NotBeNull();
    }
}
