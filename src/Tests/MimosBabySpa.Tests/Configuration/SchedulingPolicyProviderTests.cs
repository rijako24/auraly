using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Configuration;
using Xunit;

namespace MimosBabySpa.Tests.Configuration;

public class SchedulingPolicyProviderTests
{
    private readonly Guid _businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetAsync_WhenConfigMissing_ReturnsDefault()
    {
        var provider = CreateProvider(null);

        var policy = await provider.GetAsync(_businessId);

        policy.SlotIntervalMinutes.Should().Be(60);
    }

    [Fact]
    public async Task GetAsync_WhenValidJson_ReturnsSchedulingRules()
    {
        const string json = """
            {
              "slotIntervalMinutes": 30,
              "bufferBetweenAppointmentsMinutes": 15,
              "requireEmployee": false,
              "employeeStrategy": "most_available"
            }
            """;

        var provider = CreateProvider(new BusinessConfiguration
        {
            BusinessId = _businessId,
            Key = BusinessConfigurationKey.SchedulingPolicy,
            Value = json
        });

        var policy = await provider.GetAsync(_businessId);

        policy.SlotIntervalMinutes.Should().Be(30);
        policy.BufferBetweenAppointmentsMinutes.Should().Be(15);
        policy.RequireEmployee.Should().BeFalse();
        policy.EmployeeStrategy.Should().Be("most_available");
    }

    [Fact]
    public async Task GetAsync_WhenInvalidJson_ReturnsDefault()
    {
        var provider = CreateProvider(new BusinessConfiguration
        {
            BusinessId = _businessId,
            Key = BusinessConfigurationKey.SchedulingPolicy,
            Value = "{ not valid json"
        });

        var policy = await provider.GetAsync(_businessId);

        policy.SlotIntervalMinutes.Should().Be(60);
    }

    private SchedulingPolicyProvider CreateProvider(BusinessConfiguration? config)
    {
        var repo = new Mock<IBusinessConfigurationRepository>();
        repo.Setup(r => r.GetByBusinessIdAndKeyAsync(_businessId, BusinessConfigurationKey.SchedulingPolicy))
            .ReturnsAsync(config);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BusinessConfigurations).Returns(repo.Object);

        return new SchedulingPolicyProvider(unitOfWork.Object, NullLogger<SchedulingPolicyProvider>.Instance);
    }
}
