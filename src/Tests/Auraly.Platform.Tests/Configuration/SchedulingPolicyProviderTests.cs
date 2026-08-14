using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Configuration;
using Xunit;

namespace Auraly.Platform.Tests.Configuration;

public class SchedulingPolicyProviderTests
{
    private readonly Guid _businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetAsync_WhenSettingsMissing_ReturnsDefault()
    {
        var provider = CreateProvider(null);

        var policy = await provider.GetAsync(_businessId);

        policy.SlotIntervalMinutes.Should().Be(60);
    }

    [Fact]
    public async Task GetAsync_WhenSettingsExist_ReturnsSchedulingRules()
    {
        var provider = CreateProvider(new BusinessSchedulingSettings
        {
            BusinessId = _businessId,
            SlotIntervalMinutes = 30,
            BufferBetweenAppointmentsMinutes = 15,
            RequireEmployee = false,
            MinimumLeadTimeMinutes = 30,
            EmployeeStrategy = "most_available"
        });

        var policy = await provider.GetAsync(_businessId);

        policy.SlotIntervalMinutes.Should().Be(30);
        policy.BufferBetweenAppointmentsMinutes.Should().Be(15);
        policy.RequireEmployee.Should().BeFalse();
        policy.MinimumLeadTimeMinutes.Should().Be(30);
        policy.EmployeeStrategy.Should().Be("most_available");
    }

    private SchedulingPolicyProvider CreateProvider(BusinessSchedulingSettings? settings)
    {
        var repo = new Mock<IBusinessSchedulingSettingsRepository>();
        repo.Setup(r => r.GetByBusinessIdAsync(_businessId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(settings);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BusinessSchedulingSettings).Returns(repo.Object);

        return new SchedulingPolicyProvider(unitOfWork.Object, NullLogger<SchedulingPolicyProvider>.Instance);
    }
}
