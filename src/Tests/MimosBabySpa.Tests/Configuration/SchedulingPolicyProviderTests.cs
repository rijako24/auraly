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

        policy.Schedule.Should().BeNull();
        policy.SlotIntervalMinutes.Should().Be(60);
    }

    [Fact]
    public async Task GetAsync_WhenValidJson_ReturnsScheduleByDay()
    {
        const string json = """
            {
              "slotIntervalMinutes": 30,
              "bufferBetweenAppointmentsMinutes": 15,
              "requireEmployee": false,
              "schedule": {
                "monday": [{"open":"08:00","close":"12:00"},{"open":"14:00","close":"18:00"}],
                "sunday": []
              }
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
        policy.Schedule.Should().ContainKey("monday");
        policy.Schedule!["monday"].Should().HaveCount(2);
        policy.Schedule!["monday"][0].Open.Should().Be("08:00");
        policy.Schedule!["monday"][0].Close.Should().Be("12:00");
        policy.Schedule!["sunday"].Should().BeEmpty();
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

        policy.Schedule.Should().BeNull();
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
