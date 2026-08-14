using Microsoft.Extensions.Logging;
using Moq;
using Auraly.Platform.Application.Billing;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Xunit;

namespace Auraly.Platform.Tests.Billing;

public sealed class UsageBillingServiceTests
{
    [Fact]
    public async Task CanProcessAsync_RenewsExpiredSubscription_AndOpensFreshPeriod()
    {
        var now = DateTime.UtcNow;
        var subscription = CreateSubscription(now.AddMonths(-5), now.AddMonths(-4), autoRenew: true);
        var (service, subscriptions, periods) = CreateService(subscription);
        BusinessUsagePeriod? created = null;
        periods
            .Setup(repository => repository.AddAsync(It.IsAny<BusinessUsagePeriod>(), It.IsAny<CancellationToken>()))
            .Callback<BusinessUsagePeriod, CancellationToken>((period, _) => created = period)
            .ReturnsAsync((BusinessUsagePeriod period, CancellationToken _) => period);

        var result = await service.CanProcessAsync(subscription.BusinessId);

        Assert.True(result.IsAllowed);
        Assert.Equal("ok", result.Code);
        Assert.True(subscription.CurrentPeriodEnd > DateTime.UtcNow);
        Assert.Equal(subscription.CurrentPeriodStart.AddMonths(1), subscription.CurrentPeriodEnd);
        Assert.NotNull(created);
        Assert.Equal(subscription.CurrentPeriodStart, created!.PeriodStart);
        Assert.Equal(subscription.CurrentPeriodEnd, created.PeriodEnd);
        Assert.Equal(0, created.CreditsUsed);
        Assert.Equal(0, created.VariableCostUsedCop);
        Assert.Equal(UsagePeriodStatus.Open, created.Status);
        subscriptions.Verify(
            repository => repository.UpdateAsync(subscription, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CanProcessAsync_BlocksExpiredSubscription_WhenAutoRenewIsDisabled()
    {
        var now = DateTime.UtcNow;
        var subscription = CreateSubscription(now.AddMonths(-1).AddDays(-1), now.AddDays(-1), autoRenew: false);
        var (service, subscriptions, periods) = CreateService(subscription);

        var result = await service.CanProcessAsync(subscription.BusinessId);

        Assert.False(result.IsAllowed);
        Assert.Equal("subscription_inactive", result.Code);
        subscriptions.Verify(
            repository => repository.UpdateAsync(It.IsAny<BusinessSubscription>(), It.IsAny<CancellationToken>()),
            Times.Never);
        periods.Verify(
            repository => repository.AddAsync(It.IsAny<BusinessUsagePeriod>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CanProcessAsync_DoesNotRenewCurrentSubscription()
    {
        var now = DateTime.UtcNow;
        var subscription = CreateSubscription(now.AddDays(-1), now.AddMonths(1), autoRenew: true);
        var current = new BusinessUsagePeriod
        {
            BusinessUsagePeriodId = Guid.NewGuid(),
            BusinessSubscriptionId = subscription.BusinessSubscriptionId,
            BusinessId = subscription.BusinessId,
            PeriodStart = subscription.CurrentPeriodStart,
            PeriodEnd = subscription.CurrentPeriodEnd,
            CreditsIncluded = subscription.IncludedCredits,
            VariableCostLimitCop = subscription.MaxVariableCostCop,
            Status = UsagePeriodStatus.Open,
            BusinessSubscription = subscription
        };
        var (service, subscriptions, periods) = CreateService(subscription);
        periods
            .Setup(repository => repository.GetCurrentAsync(
                subscription.BusinessSubscriptionId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(current);

        var result = await service.CanProcessAsync(subscription.BusinessId);

        Assert.True(result.IsAllowed);
        subscriptions.Verify(
            repository => repository.UpdateAsync(It.IsAny<BusinessSubscription>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static (
        UsageBillingService Service,
        Mock<IBusinessSubscriptionRepository> Subscriptions,
        Mock<IBusinessUsagePeriodRepository> Periods)
        CreateService(BusinessSubscription subscription)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var subscriptions = new Mock<IBusinessSubscriptionRepository>();
        var periods = new Mock<IBusinessUsagePeriodRepository>();
        subscriptions
            .Setup(repository => repository.GetActiveByBusinessIdAsync(
                subscription.BusinessId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscription);
        periods
            .Setup(repository => repository.GetCurrentAsync(
                subscription.BusinessSubscriptionId,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessUsagePeriod?)null);
        unitOfWork.SetupGet(value => value.BusinessSubscriptions).Returns(subscriptions.Object);
        unitOfWork.SetupGet(value => value.BusinessUsagePeriods).Returns(periods.Object);

        return (
            new UsageBillingService(unitOfWork.Object, Mock.Of<ILogger<UsageBillingService>>()),
            subscriptions,
            periods);
    }

    private static BusinessSubscription CreateSubscription(DateTime start, DateTime end, bool autoRenew) =>
        new()
        {
            BusinessSubscriptionId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            SubscriptionPlanId = Guid.NewGuid(),
            Status = SubscriptionStatus.Active,
            CurrentPeriodStart = start,
            CurrentPeriodEnd = end,
            PlanCodeSnapshot = "TEST",
            PlanNameSnapshot = "Test plan",
            IncludedCredits = 100,
            MaxVariableCostCop = 10_000,
            AutoRenew = autoRenew
        };
}
