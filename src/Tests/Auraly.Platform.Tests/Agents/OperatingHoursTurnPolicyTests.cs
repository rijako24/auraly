using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Application.Time;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class OperatingHoursTurnPolicyTests
{
    [Fact]
    public async Task EvaluateAsync_OutsideConfiguredHours_BlocksOperationsAndReturnsNextWindow()
    {
        var businessId = Guid.NewGuid();
        var today = new DateOnly(2026, 7, 11);
        var hours = new Mock<IWorkingHoursService>();
        hours.Setup(service => service.GetEffectiveBusinessWorkingHoursAsync(
                businessId, It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        hours.Setup(service => service.GetEffectiveBusinessWorkingHoursAsync(
                businessId, today, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new TimeBlock { Open = "09:00", Close = "18:00" }]);

        var result = await new OperatingHoursTurnPolicy(hours.Object).EvaluateAsync(
            new AgentConfig
            {
                BusinessId = businessId,
                OperatingHours = new OperatingHoursDefinitions
                {
                    Enforce = true,
                    OutsideHours = new OutsideOperatingHoursResponseDefinition
                    {
                        Guidance = "Informa el siguiente horario."
                    }
                }
            },
            new BusinessClockSnapshot(
                businessId,
                new DateTimeOffset(2026, 7, 11, 20, 0, 0, TimeSpan.FromHours(-5)),
                today,
                TimeZoneInfo.Utc));

        result.IsEnforced.Should().BeTrue();
        result.IsOutsideOperatingHours.Should().BeTrue();
        result.NextOperatingWindowText.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateAsync_WhenPolicyDisabled_DoesNotReadWorkingHours()
    {
        var hours = new Mock<IWorkingHoursService>(MockBehavior.Strict);
        var result = await new OperatingHoursTurnPolicy(hours.Object).EvaluateAsync(
            new AgentConfig(),
            new BusinessClockSnapshot(Guid.NewGuid(), DateTimeOffset.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow), TimeZoneInfo.Utc));

        result.Should().Be(OperatingHoursTurnContext.Disabled);
        hours.VerifyNoOtherCalls();
    }
}
