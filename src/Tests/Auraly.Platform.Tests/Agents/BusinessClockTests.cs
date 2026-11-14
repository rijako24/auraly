using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Time;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class BusinessClockTests
{
    [Theory]
    [InlineData("Pacific/Apia", "2011-12-30T10:00:00Z", "2011-12-31", 14)]
    [InlineData("America/New_York", "2026-03-08T07:00:00Z", "2026-03-08", -4)]
    [InlineData("America/Bogota", "2026-11-12T00:30:00Z", "2026-11-11", -5)]
    [InlineData("SA Pacific Standard Time", "2026-11-12T00:30:00Z", "2026-11-11", -5)]
    public async Task Snapshot_uses_the_configured_calendar_with_one_business_read(
        string zone, string instant, string day, int offsetHours)
    {
        var business = new Business { BusinessId = Guid.NewGuid(), TimeZone = zone };
        var at = DateTimeOffset.Parse(instant);
        var (clock, businesses) = Create(business, at);

        var result = await clock.GetSnapshotAsync(business.BusinessId);

        Assert.Equal(DateOnly.Parse(day), result.Today);
        Assert.Equal(TimeSpan.FromHours(offsetHours), result.Now.Offset);
        Assert.Equal(at, result.Now.ToUniversalTime());
        businesses.Verify(value => value.GetByIdAsync(business.BusinessId), Times.Once);
        businesses.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task An_invalid_configured_zone_does_not_silently_change_the_business_calendar()
    {
        var business = new Business { BusinessId = Guid.NewGuid(), TimeZone = "Unknown/Zone" };
        var (clock, businesses) = Create(business, DateTimeOffset.Parse("2026-11-12T00:30:00Z"));

        await Assert.ThrowsAsync<ArgumentException>(() => clock.GetSnapshotAsync(business.BusinessId));

        businesses.Verify(value => value.GetByIdAsync(business.BusinessId), Times.Once);
        businesses.VerifyNoOtherCalls();
    }

    private static (BusinessClock Clock, Mock<IBusinessRepository> Businesses) Create(Business business, DateTimeOffset at)
    {
        var businesses = new Mock<IBusinessRepository>(MockBehavior.Strict);
        businesses.Setup(value => value.GetByIdAsync(business.BusinessId)).ReturnsAsync(business);
        var work = new Mock<IUnitOfWork>(MockBehavior.Strict);
        work.SetupGet(value => value.Businesses).Returns(businesses.Object);
        var time = new Mock<TimeProvider>();
        time.Setup(value => value.GetUtcNow()).Returns(at);
        // A configured site must not consult or inherit a calendar integration.
        var integrations = new Mock<IIntegrationsConfigProvider>(MockBehavior.Strict);
        return (new BusinessClock(work.Object, integrations.Object, NullLogger<BusinessClock>.Instance, time.Object), businesses);
    }
}
