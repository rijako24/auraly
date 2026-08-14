using System.Text.Json;
using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Agents.Gating;
using Auraly.Platform.Application.Agents.Operations;
using Auraly.Platform.Application.Agents.Operations.Reservation;
using Auraly.Platform.Application.Agents.Templates;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Models;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class PrepareReservationCheckoutOperationTests
{
    [Fact]
    public async Task ExecuteAsync_WhenPrepared_ReturnsExclusiveTemplateAndPermanentVerification()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var dependencies = new Dictionary<string, string>
        {
            ["service"] = "Corte",
            ["desired_date"] = "2026-07-12"
        };
        var quote = Quote(businessId, conversationId, payableCents: 1500000);
        var service = new Mock<IReservationCheckoutPreparationService>();
        service.Setup(value => value.PrepareAsync(
                It.Is<ReservationCheckoutPreparationRequest>(request =>
                    request.BusinessId == businessId
                    && request.ConversationId == conversationId
                    && request.Service == "Corte"
                    && request.AddOnsProvided
                    && request.AddOns == "Barba"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationCheckoutPreparationResult
            {
                Success = true,
                Code = ReservationCheckoutOutcomeCodes.Prepared,
                Quote = quote,
                TemplateData = new Dictionary<string, object?>
                {
                    ["service_name"] = "Corte",
                    ["link_url"] = "https://pay.test/1"
                },
                VerificationDependencies = dependencies
            });
        var operation = new PrepareReservationCheckoutOperation(service.Object);
        using var input = JsonDocument.Parse("""{"service":"Corte","add_ons":"Barba"}""");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            Context(businessId, conversationId),
            CancellationToken.None);

        outcome.Success.Should().BeTrue();
        outcome.Code.Should().Be(ReservationCheckoutOutcomeCodes.Prepared);
        var presentation = outcome.Presentations.Should().ContainSingle().Subject;
        presentation.TemplateId.Should().Be("checkout_with_deposit");
        presentation.Mode.Should().Be(FragmentRenderMode.Exclusive);
        presentation.Priority.Should().Be(FragmentPriority.Required);
        var verification = outcome.Effects.OfType<SaveVerificationEffect>().Should().ContainSingle().Subject;
        verification.VerificationType.Should().Be(VerificationFactTypes.CheckoutPrepared);
        verification.Dependencies.Should().BeEquivalentTo(dependencies);
        verification.Ttl.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_ForNoPaymentReservation_AlsoRecordsConfirmationVerification()
    {
        var businessId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var service = new Mock<IReservationCheckoutPreparationService>();
        service.Setup(value => value.PrepareAsync(
                It.IsAny<ReservationCheckoutPreparationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReservationCheckoutPreparationResult
            {
                Success = true,
                Code = ReservationCheckoutOutcomeCodes.Prepared,
                Quote = Quote(businessId, conversationId, payableCents: 0),
                TemplateData = new Dictionary<string, object?>(),
                VerificationDependencies = new Dictionary<string, string> { ["service"] = "Corte" }
            });
        var operation = new PrepareReservationCheckoutOperation(service.Object);
        using var input = JsonDocument.Parse("""{"service":"Corte"}""");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            Context(businessId, conversationId),
            CancellationToken.None);

        outcome.Effects.OfType<SaveVerificationEffect>()
            .Select(value => value.VerificationType)
            .Should().BeEquivalentTo(
                VerificationFactTypes.CheckoutPrepared,
                VerificationFactTypes.CheckoutNoPaymentPrepared);
        outcome.Effects.OfType<SaveVerificationEffect>().Should().OnlyContain(value => value.Ttl == null);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAvailabilityIsStale_ReturnsTypedRecoverableFailureWithoutPresentation()
    {
        var service = new Mock<IReservationCheckoutPreparationService>();
        service.Setup(value => value.PrepareAsync(
                It.IsAny<ReservationCheckoutPreparationRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReservationCheckoutPreparationResult.Fail(
                ReservationCheckoutOutcomeCodes.AvailabilityStale,
                "Availability is stale.",
                recoverable: true));
        var operation = new PrepareReservationCheckoutOperation(service.Object);
        using var input = JsonDocument.Parse("""{"service":"Corte"}""");

        var outcome = await operation.ExecuteAsync(
            input.RootElement,
            Context(Guid.NewGuid(), Guid.NewGuid()),
            CancellationToken.None);

        outcome.Success.Should().BeFalse();
        outcome.Code.Should().Be(ReservationCheckoutOutcomeCodes.AvailabilityStale);
        outcome.Error!.Recoverable.Should().BeTrue();
        outcome.Error.RemediationSignal.Should().Be("reservation.check_availability");
        outcome.Presentations.Should().BeEmpty();
        outcome.Effects.Should().BeEmpty();
    }

    private static OperationContext Context(Guid businessId, Guid conversationId) => new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = businessId,
        ConversationId = conversationId,
        BusinessToday = new DateOnly(2026, 7, 10),
        BusinessNow = new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.FromHours(-5)),
        Config = new AgentConfig(),
        ConversationState = new ConversationState(),
        Facts = new Dictionary<string, string> { ["service"] = "Corte" }
    };

    private static CheckoutQuote Quote(Guid businessId, Guid conversationId, long payableCents) =>
        new(
            businessId,
            conversationId,
            CheckoutKind.Reservation,
            Guid.NewGuid(),
            "Corte",
            "Barberia",
            30,
            [new CheckoutQuoteLineItem("Corte", 30000)],
            3000000,
            payableCents,
            "COP",
            payableCents > 0 ? "wompi" : "none",
            payableCents > 0 ? "Wompi" : "Sin anticipo",
            payableCents > 0 ? 50 : null,
            payableCents > 0 ? "checkout_with_deposit" : "checkout_no_deposit",
            payableCents > 0 ? "reservation" : string.Empty,
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30));
}
