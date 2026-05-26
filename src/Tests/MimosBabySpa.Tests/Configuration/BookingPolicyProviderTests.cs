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

public class BookingPolicyProviderTests
{
    private readonly Guid _businessId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetAsync_WhenConfigMissing_ReturnsDefault()
    {
        var provider = CreateProvider(null);

        var policy = await provider.GetAsync(_businessId);

        policy.DepositRequired.Should().BeFalse();
        policy.DepositPercentage.Should().Be(50);
        policy.Currency.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenValidJson_ReturnsPolicy()
    {
        const string json = """
            {
              "depositRequired": true,
              "depositPercentage": 50,
              "currency": "COP"
            }
            """;

        var provider = CreateProvider(new BusinessConfiguration
        {
            BusinessId = _businessId,
            Key = BusinessConfigurationKey.BookingPolicy,
            Value = json
        });

        var policy = await provider.GetAsync(_businessId);

        policy.DepositRequired.Should().BeTrue();
        policy.DepositPercentage.Should().Be(50);
        policy.Currency.Should().Be("COP");
    }

    [Fact]
    public async Task GetAsync_WhenInvalidJson_ReturnsDefault()
    {
        var provider = CreateProvider(new BusinessConfiguration
        {
            BusinessId = _businessId,
            Key = BusinessConfigurationKey.BookingPolicy,
            Value = "{ not valid json"
        });

        var policy = await provider.GetAsync(_businessId);

        policy.DepositRequired.Should().BeFalse();
    }

    private BookingPolicyProvider CreateProvider(BusinessConfiguration? config)
    {
        var repo = new Mock<IBusinessConfigurationRepository>();
        repo.Setup(r => r.GetByBusinessIdAndKeyAsync(_businessId, BusinessConfigurationKey.BookingPolicy))
            .ReturnsAsync(config);

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.BusinessConfigurations).Returns(repo.Object);

        return new BookingPolicyProvider(unitOfWork.Object, NullLogger<BookingPolicyProvider>.Instance);
    }
}

public class BookingPolicyParamsTests
{
    [Theory]
    [InlineData(100000, 50, 50000)]
    [InlineData(99000, 50, 49500)]
    [InlineData(100000, 0, 0)]
    public void CalculateDepositCents_ComputesPercentage(long totalCents, int percentage, long expected)
    {
        var policy = new BookingPolicyParams
        {
            DepositRequired = percentage > 0,
            DepositPercentage = percentage
        };

        policy.CalculateDepositCents(totalCents).Should().Be(expected);
    }

    [Fact]
    public void CalculateDepositCents_WhenDepositNotRequired_ReturnsZero()
    {
        var policy = new BookingPolicyParams
        {
            DepositRequired = false,
            DepositPercentage = 50
        };

        policy.CalculateDepositCents(100000).Should().Be(0);
    }
}
