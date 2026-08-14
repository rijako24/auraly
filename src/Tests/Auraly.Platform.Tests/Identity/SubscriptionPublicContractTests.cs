using Auraly.Platform.Application.Identity.DTOs;
using Xunit;

namespace Auraly.Platform.Tests.Identity;

public sealed class SubscriptionPublicContractTests
{
    [Theory]
    [InlineData(typeof(BusinessUsageDto))]
    [InlineData(typeof(SubscriptionPlanDto))]
    [InlineData(typeof(SubscriptionDetailsDto))]
    [InlineData(typeof(UsageBreakdownDto))]
    [InlineData(typeof(UsageActivityDto))]
    public void PublicSubscriptionDtos_DoNotExposeInternalCosts(Type dtoType)
    {
        var propertyNames = dtoType
            .GetProperties()
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(propertyNames, name =>
            name.Contains("Cost", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SubscriptionDetailsDto_ExposesRequiredCreditAndValidityInformation()
    {
        var propertyNames = typeof(SubscriptionDetailsDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(SubscriptionDetailsDto.SubscriptionStartedAt), propertyNames);
        Assert.Contains(nameof(SubscriptionDetailsDto.PeriodStart), propertyNames);
        Assert.Contains(nameof(SubscriptionDetailsDto.PeriodEnd), propertyNames);
        Assert.Contains(nameof(SubscriptionDetailsDto.CreditsUsed), propertyNames);
        Assert.Contains(nameof(SubscriptionDetailsDto.CreditsRemaining), propertyNames);
        Assert.Contains(nameof(SubscriptionDetailsDto.UsageBreakdown), propertyNames);
        Assert.Contains(nameof(SubscriptionDetailsDto.RecentUsage), propertyNames);
    }
}
