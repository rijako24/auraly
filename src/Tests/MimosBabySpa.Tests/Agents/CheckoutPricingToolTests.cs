using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class ResolvePricingToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsDepositFromBookingPolicy()
    {
        var checkout = new Mock<IReservationCheckoutPricing>();
        checkout.Setup(c => c.ResolveAsync(
                It.IsAny<Guid>(),
                "Plan Marineritos",
                "Decoración Sencilla",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCheckoutResult(200000, 100000));

        var addOnCatalog = new Mock<IAddOnCatalogService>();
        addOnCatalog.Setup(c => c.ValidateAsync(
                It.IsAny<Guid>(),
                "Plan Marineritos",
                "Decoración Sencilla",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddOnValidationResult.Ok("Decoración Sencilla"));

        var tool = new ResolvePricingTool(checkout.Object, addOnCatalog.Object);
        var ctx = new AgentToolContext { BusinessId = Guid.NewGuid() };

        using var args = JsonDocument.Parse("""
            {"service":"Plan Marineritos","add_ons":"Decoración Sencilla"}
            """);
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(tool, args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"deposit_required\":true");
        json.Should().Contain("\"deposit_cents\":100000");
        json.Should().Contain("\"currency\":\"COP\"");
    }

    [Fact]
    public async Task ExecuteAsync_WhenServiceNotFound_ReturnsError()
    {
        var checkout = new Mock<IReservationCheckoutPricing>();
        checkout.Setup(c => c.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CheckoutPricingResult?)null);

        var addOnCatalog = new Mock<IAddOnCatalogService>();
        var tool = new ResolvePricingTool(checkout.Object, addOnCatalog.Object);
        var ctx = new AgentToolContext { BusinessId = Guid.NewGuid() };

        using var args = JsonDocument.Parse("""{"service":"Unknown"}""");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(tool, args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("service_not_found");
    }

    private static CheckoutPricingResult CreateCheckoutResult(long totalCents, long depositCents)
    {
        var pricing = new PricingResult(
            [new PricingLineItem("Plan Marineritos", totalCents / 100m)],
            new Dictionary<string, string>(),
            totalCents / 100m);

        var policy = new BookingPolicyParams
        {
            DepositRequired = depositCents > 0,
            DepositPercentage = 50,
            Currency = "COP"
        };

        return new CheckoutPricingResult(pricing, policy, totalCents, depositCents);
    }
}

public class GeneratePaymentLinkToolTests
{
    private readonly Guid _businessId = Guid.NewGuid();
    private readonly Guid _conversationId = Guid.NewGuid();

    [Fact]
    public async Task ExecuteAsync_WhenServiceMissing_ReturnsMissingPrerequisites()
    {
        var tool = CreateTool(out _, out _, out var checkout, out _, out _, out _);
        checkout.Setup(c => c.ResolveAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCheckoutResult(100000, 50000));

        var ctx = CreateContext(service: null);
        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(tool, args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("missing_prerequisites");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDepositNotRequired_ReturnsError()
    {
        var tool = CreateTool(out _, out _, out var checkout, out _, out _, out _);
        checkout.Setup(c => c.ResolveAsync(
                It.IsAny<Guid>(),
                "Plan Marineritos",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCheckoutResult(100000, 0, depositRequired: false));

        var ctx = CreateContext("Plan Marineritos");
        AgentTestHelpers.SetBookingPack(ctx, new BookingPolicyParams
        {
            DepositRequired = false,
            Currency = "COP"
        });
        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(tool, args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("deposit_not_required");
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesDepositFromFactsWithoutAmountParam()
    {
        var tool = CreateTool(out var paymentLinks, out var paymentLifecycle, out var checkout, out var intentBuilder, out var availability, out var employeeAssignment);
        checkout.Setup(c => c.ResolveAsync(
                _businessId,
                "Plan Marineritos",
                "Decoración Sencilla",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCheckoutResult(200000, 100000));

        SetupIntentAndAvailability(intentBuilder, availability, employeeAssignment);

        paymentLinks.Setup(p => p.GenerateAnticipoLinkAsync(
                It.Is<PaymentLinkRequest>(r => r.AmountInCents == 100000 && r.Currency == "COP"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkResult(
                Success: true,
                PaymentLinkUrl: "https://pay.test/link",
                PaymentReferenceId: "ref-123",
                ExpiresAt: DateTime.UtcNow.AddHours(1),
                ErrorMessage: null));

        paymentLifecycle.Setup(p => p.GetActiveByConversationAsync(_conversationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentTransaction?)null);
        paymentLifecycle.Setup(p => p.CreatePendingAsync(
                _businessId,
                _conversationId,
                It.IsAny<ReservationIntentSnapshot>(),
                "ref-123",
                "https://pay.test/link",
                100000,
                "COP",
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentTransaction
            {
                PaymentTransactionId = Guid.NewGuid(),
                LinkUrl = "https://pay.test/link",
                PaymentReferenceId = "ref-123",
                AmountInCents = 100000,
                Currency = "COP",
                ExpiresAt = DateTime.UtcNow.AddHours(1)
            });

        var ctx = CreateContext("Plan Marineritos", "Decoración Sencilla");
        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(tool, args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"deposit_cents\":100000");
        json.Should().Contain("https://pay.test/link");
        ctx.GetPackContext<IBookingPackContext>()!.ActivePayment!.LinkUrl.Should().Be("https://pay.test/link");
        ctx.GetPackContext<IBookingPackContext>()!.ActivePayment!.AmountInCents.Should().Be(100000);
    }

    [Fact]
    public async Task ExecuteAsync_UsesChannelPhoneWhenFactPhoneEmpty()
    {
        var tool = CreateTool(out var paymentLinks, out var paymentLifecycle, out var checkout, out var intentBuilder, out var availability, out var employeeAssignment);
        checkout.Setup(c => c.ResolveAsync(
                It.IsAny<Guid>(),
                "Plan Marineritos",
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateCheckoutResult(100000, 50000));

        SetupIntentAndAvailability(intentBuilder, availability, employeeAssignment);

        paymentLifecycle.Setup(p => p.GetActiveByConversationAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PaymentTransaction?)null);
        paymentLifecycle.Setup(p => p.CreatePendingAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<ReservationIntentSnapshot>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentTransaction { LinkUrl = "https://pay.test/link" });

        paymentLinks.Setup(p => p.GenerateAnticipoLinkAsync(
                It.Is<PaymentLinkRequest>(r => r.CustomerPhone == "+573001234567"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkResult(
                Success: true,
                PaymentLinkUrl: "https://pay.test/link",
                PaymentReferenceId: "ref-456",
                ExpiresAt: DateTime.UtcNow.AddHours(1),
                ErrorMessage: null));

        var ctx = CreateContext("Plan Marineritos", channelPhone: "+573001234567");
        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(tool, args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
    }

    private static void SetupIntentAndAvailability(
        Mock<IReservationIntentBuilder> intentBuilder,
        Mock<IAvailabilityService> availability,
        Mock<IEmployeeAssignmentService> employeeAssignment)
    {
        var serviceId = Guid.NewGuid();
        intentBuilder.Setup(b => b.BuildFromContextAsync(It.IsAny<AgentToolContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationIntentSnapshot(
                serviceId,
                "Plan Marineritos",
                new DateTime(2026, 5, 22, 10, 0, 0),
                60,
                null,
                "Test",
                null,
                "+573001234567",
                [],
                "{}"));

        availability.Setup(a => a.CheckAvailabilityAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<TimeSpan?>(),
                It.IsAny<AvailabilityParams>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AvailabilityResult
            {
                IsAvailable = true,
                RequestServiceName = "Plan Marineritos"
            });

        employeeAssignment.Setup(e => e.FindBestAvailableEmployeeAsync(
                It.IsAny<Guid>(),
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<Guid?>()))
            .ReturnsAsync(new Employee { EmployeeId = Guid.NewGuid(), Name = "María" });
    }

    private GeneratePaymentLinkTool CreateTool(
        out Mock<IPaymentLinkService> paymentLinks,
        out Mock<IPaymentLifecycleService> paymentLifecycle,
        out Mock<IReservationCheckoutPricing> checkout,
        out Mock<IReservationIntentBuilder> intentBuilder,
        out Mock<IAvailabilityService> availability,
        out Mock<IEmployeeAssignmentService> employeeAssignment)
    {
        paymentLinks = new Mock<IPaymentLinkService>();
        paymentLifecycle = new Mock<IPaymentLifecycleService>();
        checkout = new Mock<IReservationCheckoutPricing>();
        intentBuilder = new Mock<IReservationIntentBuilder>();
        availability = new Mock<IAvailabilityService>();
        employeeAssignment = new Mock<IEmployeeAssignmentService>();
        var scheduling = new Mock<ISchedulingPolicyProvider>();
        scheduling.Setup(s => s.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AvailabilityParams.Default);

        return new GeneratePaymentLinkTool(
            paymentLinks.Object,
            checkout.Object,
            paymentLifecycle.Object,
            intentBuilder.Object,
            availability.Object,
            scheduling.Object,
            employeeAssignment.Object);
    }

    private AgentToolContext CreateContext(
        string? service,
        string? addOns = null,
        string? phone = null,
        string? channelPhone = null)
    {
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(service))
            facts["service"] = service;
        if (!string.IsNullOrWhiteSpace(addOns))
            facts["add_ons"] = addOns;
        if (!string.IsNullOrWhiteSpace(phone))
            facts["customer_phone"] = phone;

        var ctx = new AgentToolContext
        {
            BusinessId = _businessId,
            ConversationId = _conversationId,
            ChannelPhone = channelPhone ?? "+573001234567",
            Config = new AgentConfig { FactSchema = AgentTestHelpers.MimiFactSchema },
            ConversationState = new ConversationStateModel { BusinessId = _businessId },
            Conversation = new Conversation(),
            Facts = facts
        };

        AgentTestHelpers.SetBookingPack(ctx, new BookingPolicyParams
        {
            DepositRequired = true,
            DepositPercentage = 50,
            Currency = "COP"
        });

        return ctx;
    }

    private static CheckoutPricingResult CreateCheckoutResult(
        long totalCents,
        long depositCents,
        bool depositRequired = true)
    {
        var pricing = new PricingResult(
            [new PricingLineItem("Plan Marineritos", totalCents / 100m)],
            new Dictionary<string, string>(),
            totalCents / 100m);

        var policy = new BookingPolicyParams
        {
            DepositRequired = depositRequired,
            DepositPercentage = depositRequired ? 50 : 0,
            Currency = "COP"
        };

        return new CheckoutPricingResult(pricing, policy, totalCents, depositCents);
    }
}
