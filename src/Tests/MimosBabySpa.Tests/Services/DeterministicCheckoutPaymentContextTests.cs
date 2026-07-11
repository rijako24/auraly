using FluentAssertions;
using Moq;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Services;

public sealed class DeterministicCheckoutPaymentContextTests
{
    [Fact]
    public async Task EnsurePaymentLinkAsync_WithNeutralContext_ReusesMatchingPaymentWithoutToolContext()
    {
        var quote = CreateQuote();
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = quote.CheckoutKind,
            Status = PaymentTransactionStatus.Created,
            LinkUrl = "https://pay.test/reused",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            QuoteHash = "stable-hash",
            AmountInCents = quote.PayableCents,
            Currency = quote.Currency,
            ConfirmationOutcome = quote.ConfirmationOutcome
        };
        var lifecycle = new Mock<IPaymentLifecycleService>();
        lifecycle.Setup(value => value.RefreshPendingCheckoutAsync(
                payment,
                "new-snapshot",
                "stable-hash",
                quote.ConfirmationOutcome,
                quote.PayableCents,
                quote.Currency,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var links = new Mock<IPaymentLinkService>();
        var quotes = new Mock<ICheckoutQuoteService>();
        quotes.Setup(value => value.ComputeHash(quote)).Returns("stable-hash");
        var coordinator = new CheckoutPaymentCoordinator(links.Object, lifecycle.Object, quotes.Object);

        var result = await coordinator.EnsurePaymentLinkAsync(
            new CheckoutPaymentContext(quote.BusinessId, quote.ConversationId, payment),
            quote,
            "+573001112233",
            "new-snapshot",
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Payment.Should().BeSameAs(payment);
        result.LinkUrl.Should().Be("https://pay.test/reused");
        links.Verify(value => value.GenerateAnticipoLinkAsync(
            It.IsAny<PaymentLinkRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DiscardActiveCheckoutAsync_WithNeutralContext_OnlyDiscardsMatchingKind()
    {
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = CheckoutKind.Reservation,
            Status = PaymentTransactionStatus.Created
        };
        var lifecycle = new Mock<IPaymentLifecycleService>();
        lifecycle.Setup(value => value.DiscardPendingAsync(payment, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var coordinator = new CheckoutPaymentCoordinator(
            Mock.Of<IPaymentLinkService>(),
            lifecycle.Object,
            Mock.Of<ICheckoutQuoteService>());
        var context = new CheckoutPaymentContext(Guid.NewGuid(), Guid.NewGuid(), payment);

        var differentKind = await coordinator.DiscardActiveCheckoutAsync(
            context,
            CheckoutKind.Order,
            CancellationToken.None);
        var matchingKind = await coordinator.DiscardActiveCheckoutAsync(
            context,
            CheckoutKind.Reservation,
            CancellationToken.None);

        differentKind.DiscardedPayment.Should().BeNull();
        matchingKind.DiscardedPayment.Should().BeSameAs(payment);
        lifecycle.Verify(value => value.DiscardPendingAsync(payment, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static CheckoutQuote CreateQuote() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            CheckoutKind.Reservation,
            Guid.NewGuid(),
            "Corte",
            "Barberia",
            30,
            [new CheckoutQuoteLineItem("Corte", 30000m)],
            3000000,
            1500000,
            "COP",
            "deposit",
            "Anticipo",
            50,
            "checkout_with_deposit",
            "reservation",
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            new Dictionary<string, string>(),
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30));
}
