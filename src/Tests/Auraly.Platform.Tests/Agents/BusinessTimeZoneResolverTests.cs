using FluentAssertions;
using Auraly.Platform.Application.Time;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public class BusinessTimeZoneResolverTests
{
    [Theory]
    [InlineData("America/New_York", "America/New_York")]
    [InlineData(" SA Pacific Standard Time ", "America/Bogota")]
    public void Configured_zone_is_normalized_for_browser_use(string configured, string expected)
        => Assert.Equal(expected, BusinessTimeZoneResolver.NormalizeConfiguredId(configured));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Unknown/Zone")]
    public void Configured_zone_never_uses_a_fallback(string configured)
        => Assert.ThrowsAny<ArgumentException>(() => BusinessTimeZoneResolver.NormalizeConfiguredId(configured));

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
