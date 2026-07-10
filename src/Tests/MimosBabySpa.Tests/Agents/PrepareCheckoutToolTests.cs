using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class PrepareCheckoutToolTests
{
    [Fact]
    public async Task ExecuteAsync_WithoutAddOnsArgument_UsesFactAddOns()
    {
        var businessId = Guid.NewGuid();
        var tool = CreateTool(businessId, out var addOnCatalog, out var checkoutPayments);
        var ctx = CreateContext(businessId, addOns: "Mascarilla de carbono");
        RecordAvailability(ctx);

        addOnCatalog.Setup(c => c.ValidateAsync(
                businessId,
                "Corte + tinte",
                "Mascarilla de carbono",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddOnValidationResult.Ok("Mascarilla de carbono"));
        checkoutPayments.Setup(c => c.EnsurePaymentLinkAsync(
                ctx,
                It.Is<CheckoutQuote>(q => q.TotalCents == 6500000 && q.LineItems.Count == 2),
                "+15554098032",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/addon", new PaymentTransaction()));

        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        var fragment = ctx.Turn!.FragmentEntries.Should().ContainSingle().Subject.Fragment;
        fragment.Data["total"].Should().Be("65,000");
        fragment.Data["addons"].Should().BeAssignableTo<IReadOnlyList<object>>()
            .Which.Should().ContainSingle();
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitEmptyAddOns_DoesNotUseFactAddOns()
    {
        var businessId = Guid.NewGuid();
        var tool = CreateTool(businessId, out var addOnCatalog, out var checkoutPayments);
        var ctx = CreateContext(businessId, addOns: "Mascarilla de carbono");
        RecordAvailability(ctx);

        checkoutPayments.Setup(c => c.EnsurePaymentLinkAsync(
                ctx,
                It.Is<CheckoutQuote>(q => q.TotalCents == 5000000 && q.LineItems.Count == 1),
                "+15554098032",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/no-addon", new PaymentTransaction()));

        using var args = JsonDocument.Parse("""{"add_ons":""}""");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        var fragment = ctx.Turn!.FragmentEntries.Should().ContainSingle().Subject.Fragment;
        fragment.Data["total"].Should().Be("50,000");
        fragment.Data["addons"].Should().BeAssignableTo<IReadOnlyList<object>>()
            .Which.Should().BeEmpty();
        addOnCatalog.Verify(c => c.ValidateAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitService_UsesChangedServiceForSummaryAndPaymentLink()
    {
        var businessId = Guid.NewGuid();
        var tool = CreateTool(businessId, out _, out var checkoutPayments);
        var ctx = CreateContext(businessId, addOns: string.Empty);
        RecordAvailability(ctx, service: "Corte premium", date: "2026-07-08", time: "14:30");

        checkoutPayments.Setup(c => c.EnsurePaymentLinkAsync(
                ctx,
                It.Is<CheckoutQuote>(q =>
                    q.ServiceName == "Corte premium"
                    && q.TotalCents == 4000000
                    && q.LineItems.Single().Name == "Corte premium"),
                "+15554098032",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/service-change", new PaymentTransaction()));

        using var args = JsonDocument.Parse("""{"service":"Corte premium"}""");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        var fragment = ctx.Turn!.FragmentEntries.Should().ContainSingle().Subject.Fragment;
        fragment.Data["service_name"].Should().Be("Corte premium");
        fragment.Data["total"].Should().Be("40,000");
    }

    [Theory]
    [InlineData("2026-07-09", "14:30")]
    [InlineData("2026-07-08", "15:30")]
    public async Task ExecuteAsync_WithChangedScheduleFacts_UsesChangedScheduleInCheckoutSnapshot(
        string date,
        string time)
    {
        var businessId = Guid.NewGuid();
        var tool = CreateTool(businessId, out _, out var checkoutPayments);
        var ctx = CreateContext(businessId, addOns: string.Empty, date: date, time: time);
        RecordAvailability(ctx, service: "Corte + tinte", date: date, time: time);

        checkoutPayments.Setup(c => c.EnsurePaymentLinkAsync(
                ctx,
                It.Is<CheckoutQuote>(q => q.ServiceName == "Corte + tinte" && q.TotalCents == 5000000),
                "+15554098032",
                It.Is<string>(snapshot =>
                    snapshot.Contains($"\"reservation_date\":\"{date}\"")
                    && snapshot.Contains($"\"reservation_time\":\"{time}\"")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/schedule-change", new PaymentTransaction()));

        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Turn!.FragmentEntries.Should().ContainSingle().Subject.Fragment.Data["total"].Should().Be("50,000");
    }
    private static PrepareCheckoutTool CreateTool(
        Guid businessId,
        out Mock<IAddOnCatalogService> addOnCatalog,
        out Mock<ICheckoutPaymentCoordinator> checkoutPayments)
    {
        var services = new[]
        {
            new Service
            {
                BusinessId = businessId,
                ServiceId = Guid.NewGuid(),
                ServiceName = "Corte + tinte",
                Price = 50000,
                DurationMinutes = 45,
                IsActive = true
            },
            new Service
            {
                BusinessId = businessId,
                ServiceId = Guid.NewGuid(),
                ServiceName = "Corte premium",
                Price = 40000,
                DurationMinutes = 45,
                IsActive = true
            },
            new Service
            {
                BusinessId = businessId,
                ServiceId = Guid.NewGuid(),
                ServiceName = "Mascarilla de carbono",
                Price = 15000,
                IncludeInCheckoutTotal = true,
                IsActive = true
            }
        };

        var serviceRepo = new Mock<IServiceRepository>();
        serviceRepo.Setup(r => r.GetActiveByBusinessIdAsync(businessId))
            .ReturnsAsync(services);
        serviceRepo.Setup(r => r.GetByBusinessIdAndNameAsync(businessId, It.IsAny<string>()))
            .ReturnsAsync((Guid _, string name) =>
                services.FirstOrDefault(s => s.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase)));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(serviceRepo.Object);

        var nameResolver = new ServiceNameResolver(unitOfWork.Object, NullLogger<ServiceNameResolver>.Instance);
        var promotions = new Mock<IPromotionPricingService>();
        promotions.Setup(p => p.EvaluateAsync(
                businessId,
                It.IsAny<IReadOnlyList<PromotionPricingItem>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<PromotionPricingItem> items, DateTime? _, CancellationToken _) =>
                PromotionPricingResult.Empty(items));

        var pricing = new ReservationPricingResolver(
            unitOfWork.Object,
            nameResolver,
            NullLogger<ReservationPricingResolver>.Instance,
            promotions.Object);

        addOnCatalog = new Mock<IAddOnCatalogService>();
        checkoutPayments = new Mock<ICheckoutPaymentCoordinator>();

        return new PrepareCheckoutTool(
            pricing,
            addOnCatalog.Object,
            checkoutPayments.Object,
            Mock.Of<IConversationFactsService>(),
            new ConversationVerificationService(),
            unitOfWork.Object,
            nameResolver);
    }

    private static AgentToolContext CreateContext(Guid businessId, string addOns, string service = "Corte + tinte", string date = "2026-07-08", string time = "14:30")
    {
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationId = Guid.NewGuid(),
            ConversationState = new ConversationStateModel(),
            Turn = new AgentTurnExecution(errorEscalationThreshold: 3),
            Config = new AgentConfig
            {
                FactSchema =
                [
                    new FactSchemaEntry { Key = "service", Role = "booking.service", Source = "user" },
                    new FactSchemaEntry { Key = "desired_date", Role = "booking.date", Source = "user" },
                    new FactSchemaEntry { Key = "desired_time", Role = "booking.time", Source = "user" },
                    new FactSchemaEntry { Key = "customer_name", Role = "customer.name", Source = "user" },
                    new FactSchemaEntry { Key = "customer_phone", Role = "customer.phone", Source = "user" },
                    new FactSchemaEntry { Key = "add_ons", Role = "booking.addons", Source = "user" }
                ],
                Checkout = new CheckoutDefinitions
                {
                    Currency = "COP",
                    Modes =
                    {
                        ["reservation"] = new CheckoutModeDefinition
                        {
                            PaymentMethods =
                            {
                                ["wompi"] = new CheckoutPaymentMethodDefinition
                                {
                                    Label = "Wompi",
                                    Template = "checkout_with_deposit",
                                    ConfirmationOutcome = "reservation",
                                    Payment = new CheckoutPaymentDefinition { Percentage = 100 }
                                }
                            }
                        }
                    }
                }
            },
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = service,
                ["desired_date"] = date,
                ["desired_time"] = time,
                ["customer_name"] = "richard",
                ["customer_phone"] = "+15554098032",
                ["add_ons"] = addOns
            }
        };

        return ctx;
    }

    private static void RecordAvailability(AgentToolContext ctx, string service = "Corte + tinte", string date = "2026-07-08", string time = "14:30")
    {
        new ConversationVerificationService().Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>("service", service),
                new KeyValuePair<string, string>("desired_date", date),
                new KeyValuePair<string, string>("desired_time", time)),
            VerificationTtl.AvailabilityChecked);
    }
}
