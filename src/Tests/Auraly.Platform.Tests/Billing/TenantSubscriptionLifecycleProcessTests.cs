using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Auraly.Platform.Tests.Billing;

public sealed class TenantSubscriptionLifecycleProcessTests
{
    [Fact]
    public void BuildQuoteRequest_PreservesPaidCapacityAsCanonicalAddOns()
    {
        var request = TenantSubscriptionLifecycleProcess.BuildQuoteRequest(Candidate());

        request.Should().BeEquivalentTo(new TenantQuoteRequest(
            "starter", "Annual", 2, 1, 2, 3, 2));
    }

    [Fact]
    public void BuildQuoteRequest_RejectsFractionalDianPackages()
    {
        var candidate = Candidate() with { DianDocumentMonthlyLimit = 3_101 };

        var action = () => TenantSubscriptionLifecycleProcess.BuildQuoteRequest(candidate);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*paquetes completos de 1000*");
    }

    [Theory]
    [InlineData(-5, null, "PreDue:5")]
    [InlineData(0, "PastDue", null)]
    [InlineData(2, "PastDue", null)]
    [InlineData(3, "PastDue", "Overdue:3")]
    [InlineData(6, "PastDue", "Overdue:6")]
    [InlineData(9, "PastDue", "Overdue:9")]
    [InlineData(10, "Suspended", "Suspended:10")]
    public void Evaluate_ImplementsConfiguredGraceCalendar(
        int daysOverdue, string? expectedStatus, string? expectedEvent)
    {
        var dueAt = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var candidate = Candidate() with { CurrentPeriodEnd = dueAt };

        var result = TenantSubscriptionLifecycleProcess.Evaluate(
            candidate, dueAt.AddDays(daysOverdue));

        result.SubscriptionStatus.Should().Be(expectedStatus);
        result.EventKey.Should().Be(expectedEvent);
        result.SendEmail.Should().Be(expectedEvent is not null);
        result.NextEvaluationAt.Should().Be(daysOverdue >= 10 ? null
            : daysOverdue >= 9 ? dueAt.AddDays(10)
            : daysOverdue >= 6 ? dueAt.AddDays(9)
            : daysOverdue >= 3 ? dueAt.AddDays(6)
            : daysOverdue >= 0 ? dueAt.AddDays(3)
            : dueAt);
    }

    [Fact]
    public void Evaluate_KeepsInAppReminderWhenGlobalEmailIsDisabled()
    {
        var dueAt = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);
        var result = TenantSubscriptionLifecycleProcess.Evaluate(
            Candidate() with { CurrentPeriodEnd = dueAt, EmailRemindersEnabled = false },
            dueAt.AddDays(3));

        result.EventKey.Should().Be("Overdue:3");
        result.SendEmail.Should().BeFalse();
    }

    [Theory]
    [InlineData("POST", "/api/v1/sales", true)]
    [InlineData("POST", "/api/v1/purchasing/orders", true)]
    [InlineData("PUT", "/api/v1/inventory/items", true)]
    [InlineData("GET", "/api/v1/sales", false)]
    [InlineData("POST", "/api/v1/tenant-commercial/subscription/renewal-order/checkout", false)]
    [InlineData("POST", "/api/v1/tenant-commercial/quote", false)]
    [InlineData("POST", "/api/v1/auth/refresh", false)]
    public void SuspendedGate_AllowsPaymentAndReadOnlyButBlocksOperations(
        string method, string path, bool expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        Auraly.Api.TenantSubscriptionAccessMiddleware.ShouldCheck(context.Request)
            .Should().Be(expected);
    }

    private static TenantSubscriptionLifecycleCandidate Candidate() => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero),
        "starter", "Annual",
        FullUserLimit: 3, SellerUserLimit: 1, PosDeviceLimit: 3,
        DianDocumentMonthlyLimit: 3_100, PayrollEmployeeLimit: 20,
        IncludedFullUsers: 1, IncludedSellerUsers: 0, IncludedPosDevices: 1,
        IncludedDianDocuments: 100, IncludedPayrollEmployees: 0,
        DianDocumentPackSize: 1_000, PayrollEmployeePackSize: 10,
        EmailRemindersEnabled: true, PreDueReminderDays: 5,
        OverdueReminderIntervalDays: 3, GracePeriodDays: 10,
        BillingTimeZoneId: "UTC");
}
