using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Operations.Reservation;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using ConversationState = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class ReservationCheckoutPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_WithoutAddOnsOverride_UsesConfiguredFactAndReturnsExclusiveTemplateData()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.BusinessId, addOns: "Mascarilla de carbono");
        RecordAvailability(request);
        fixture.AddOns.Setup(value => value.ValidateAsync(
                fixture.BusinessId,
                "Corte + tinte",
                "Mascarilla de carbono",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddOnValidationResult.Ok("Mascarilla de carbono"));
        fixture.Payments.Setup(value => value.EnsurePaymentLinkAsync(
                It.IsAny<CheckoutPaymentContext>(),
                It.Is<CheckoutQuote>(quote => quote.TotalCents == 6500000 && quote.LineItems.Count == 2),
                "+15554098032",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/addon", new PaymentTransaction()));

        var result = await fixture.Service.PrepareAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Code.Should().Be("checkout.prepared");
        result.Quote!.TemplateId.Should().Be("checkout_with_deposit");
        result.TemplateData["total"].Should().Be("65,000");
        result.TemplateData["link_url"].Should().Be("https://checkout.test/addon");
        result.TemplateData["addons"].Should().BeAssignableTo<IReadOnlyList<object>>()
            .Which.Should().ContainSingle();
    }

    [Fact]
    public async Task PrepareAsync_WithExplicitEmptyAddOns_DoesNotReuseOldFact()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.BusinessId, addOns: "Mascarilla de carbono") with
        {
            AddOnsProvided = true,
            AddOns = string.Empty
        };
        RecordAvailability(request);
        fixture.Payments.Setup(value => value.EnsurePaymentLinkAsync(
                It.IsAny<CheckoutPaymentContext>(),
                It.Is<CheckoutQuote>(quote => quote.TotalCents == 5000000 && quote.LineItems.Count == 1),
                "+15554098032",
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/no-addon", new PaymentTransaction()));

        var result = await fixture.Service.PrepareAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TemplateData["total"].Should().Be("50,000");
        result.TemplateData["addons"].Should().BeAssignableTo<IReadOnlyList<object>>()
            .Which.Should().BeEmpty();
        fixture.AddOns.Verify(value => value.ValidateAsync(
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PrepareAsync_WithServiceOverride_UsesOverrideInQuoteAndPaymentSnapshot()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.BusinessId, addOns: string.Empty) with
        {
            Service = "Corte premium"
        };
        RecordAvailability(request, "Corte premium");
        fixture.Payments.Setup(value => value.EnsurePaymentLinkAsync(
                It.IsAny<CheckoutPaymentContext>(),
                It.Is<CheckoutQuote>(quote => quote.ServiceName == "Corte premium" && quote.TotalCents == 4000000),
                "+15554098032",
                It.Is<string>(snapshot => snapshot.Contains("\"service_name\":\"Corte premium\"")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/service", new PaymentTransaction()));

        var result = await fixture.Service.PrepareAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Quote!.ServiceName.Should().Be("Corte premium");
        result.TemplateData["total"].Should().Be("40,000");
    }

    [Theory]
    [InlineData("2026-07-09", "14:30")]
    [InlineData("2026-07-08", "15:30")]
    public async Task PrepareAsync_UsesCurrentScheduleInSnapshot(string date, string time)
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.BusinessId, string.Empty, date, time);
        RecordAvailability(request, date: date, time: time);
        fixture.Payments.Setup(value => value.EnsurePaymentLinkAsync(
                It.IsAny<CheckoutPaymentContext>(),
                It.IsAny<CheckoutQuote>(),
                "+15554098032",
                It.Is<string>(snapshot =>
                    snapshot.Contains($"\"reservation_date\":\"{date}\"")
                    && snapshot.Contains($"\"reservation_time\":\"{time}\"")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CheckoutPaymentLinkResult.Ok("https://checkout.test/schedule", new PaymentTransaction()));

        var result = await fixture.Service.PrepareAsync(request, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.TemplateData["date_formatted"].Should().Be(
            DateOnly.Parse(date).ToString("dd/MM/yyyy"));
        result.TemplateData["time"].Should().Be(time);
    }

    [Fact]
    public async Task PrepareAsync_WithStaleAvailability_DoesNotCreateOrReusePayment()
    {
        var fixture = CreateFixture();
        var request = CreateRequest(fixture.BusinessId, string.Empty);
        RecordAvailability(request, date: "2026-07-07");

        var result = await fixture.Service.PrepareAsync(request, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Code.Should().Be("availability.verification_stale");
        result.Recoverable.Should().BeTrue();
        fixture.Payments.Verify(value => value.EnsurePaymentLinkAsync(
            It.IsAny<CheckoutPaymentContext>(),
            It.IsAny<CheckoutQuote>(),
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CheckoutFixture CreateFixture()
    {
        var businessId = Guid.NewGuid();
        var services = new[]
        {
            Service(businessId, "Corte + tinte", 50000),
            Service(businessId, "Corte premium", 40000),
            Service(businessId, "Mascarilla de carbono", 15000)
        };
        var repository = new Mock<IServiceRepository>();
        repository.Setup(value => value.GetActiveByBusinessIdAsync(businessId)).ReturnsAsync(services);
        repository.Setup(value => value.GetByBusinessIdAndNameAsync(businessId, It.IsAny<string>()))
            .ReturnsAsync((Guid _, string name) => services.FirstOrDefault(value =>
                value.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase)));
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(value => value.Services).Returns(repository.Object);
        var names = new ServiceNameResolver(unitOfWork.Object, NullLogger<ServiceNameResolver>.Instance);
        var promotions = new Mock<IPromotionPricingService>();
        promotions.Setup(value => value.EvaluateAsync(
                businessId,
                It.IsAny<IReadOnlyList<PromotionPricingItem>>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, IReadOnlyList<PromotionPricingItem> items, DateTime? _, CancellationToken _) =>
                PromotionPricingResult.Empty(items));
        var pricing = new ReservationPricingResolver(
            unitOfWork.Object,
            names,
            NullLogger<ReservationPricingResolver>.Instance,
            promotions.Object);
        var addOns = new Mock<IAddOnCatalogService>();
        var payments = new Mock<ICheckoutPaymentCoordinator>();
        var service = new ReservationCheckoutPreparationService(
            pricing,
            addOns.Object,
            payments.Object,
            new ConversationVerificationService(),
            unitOfWork.Object,
            names);
        return new CheckoutFixture(businessId, service, addOns, payments);
    }

    private static ReservationCheckoutPreparationRequest CreateRequest(
        Guid businessId,
        string addOns,
        string date = "2026-07-08",
        string time = "14:30")
    {
        var config = new AgentConfig
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
        };
        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["service"] = "Corte + tinte",
            ["desired_date"] = date,
            ["desired_time"] = time,
            ["customer_name"] = "Richard",
            ["customer_phone"] = "+15554098032",
            ["add_ons"] = addOns
        };
        return new ReservationCheckoutPreparationRequest(
            businessId,
            Guid.NewGuid(),
            config,
            new ConversationState(),
            facts,
            null,
            false,
            null,
            null);
    }

    private static void RecordAvailability(
        ReservationCheckoutPreparationRequest request,
        string service = "Corte + tinte",
        string date = "2026-07-08",
        string time = "14:30") =>
        new ConversationVerificationService().Record(
            request.ConversationState,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>("service", service),
                new KeyValuePair<string, string>("desired_date", date),
                new KeyValuePair<string, string>("desired_time", time)),
            VerificationTtl.AvailabilityChecked);

    private static Service Service(Guid businessId, string name, decimal price) => new()
    {
        BusinessId = businessId,
        ServiceId = Guid.NewGuid(),
        ServiceName = name,
        Price = price,
        DurationMinutes = 45,
        IncludeInCheckoutTotal = true,
        IsActive = true
    };

    private sealed record CheckoutFixture(
        Guid BusinessId,
        ReservationCheckoutPreparationService Service,
        Mock<IAddOnCatalogService> AddOns,
        Mock<ICheckoutPaymentCoordinator> Payments);
}
