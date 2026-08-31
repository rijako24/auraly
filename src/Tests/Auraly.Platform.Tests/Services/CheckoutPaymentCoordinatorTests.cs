using FluentAssertions;
using Moq;
using Auraly.Platform.Application.Agents;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class CheckoutPaymentCoordinatorTests
{
    [Fact]
    public async Task DiscardActiveCheckoutAsync_WithMatchingCreatedCheckout_DiscardsPaymentAndClearsContext()
    {
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = CheckoutKind.Order,
            Status = PaymentTransactionStatus.Created
        };
        var lifecycle = new Mock<IPaymentLifecycleService>();
        lifecycle.Setup(p => p.DiscardPendingAsync(payment, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var coordinator = CreateCoordinator(lifecycle);
        var ctx = new AgentConversationContext
        {
            ConversationId = Guid.NewGuid(),
            ActivePayment = payment
        };

        var result = await coordinator.DiscardActiveCheckoutAsync(ctx, CheckoutKind.Order, CancellationToken.None);

        result.DiscardedPayment.Should().BeSameAs(payment);
        ctx.ActivePayment.Should().BeNull();
        lifecycle.Verify(p => p.DiscardPendingAsync(payment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DiscardActiveCheckoutAsync_WithDifferentCheckoutKind_DoesNotDiscardPayment()
    {
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = CheckoutKind.Reservation,
            Status = PaymentTransactionStatus.Created
        };
        var lifecycle = new Mock<IPaymentLifecycleService>();
        var coordinator = CreateCoordinator(lifecycle);
        var ctx = new AgentConversationContext
        {
            ConversationId = Guid.NewGuid(),
            ActivePayment = payment
        };

        var result = await coordinator.DiscardActiveCheckoutAsync(ctx, CheckoutKind.Order, CancellationToken.None);

        result.DiscardedPayment.Should().BeNull();
        ctx.ActivePayment.Should().BeSameAs(payment);
        lifecycle.Verify(p => p.DiscardPendingAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EnsurePaymentLinkAsync_WithSameBillableQuote_ReusesLinkAndRefreshesSnapshot()
    {
        var quote = CreateQuote();
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = quote.CheckoutKind,
            Status = PaymentTransactionStatus.Created,
            LinkUrl = "https://pay.test/original",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            QuoteHash = "same-billable-quote",
            CheckoutSnapshotJson = "old-snapshot",
            AmountInCents = quote.PayableCents,
            Currency = quote.Currency,
            ConfirmationOutcome = quote.ConfirmationOutcome
        };

        var lifecycle = new Mock<IPaymentLifecycleService>();
        lifecycle.Setup(p => p.RefreshPendingCheckoutAsync(
                payment,
                "new-snapshot",
                "same-billable-quote",
                quote.ConfirmationOutcome,
                quote.PayableCents,
                quote.Currency,
                It.IsAny<CancellationToken>()))
            .Callback<PaymentTransaction, string, string, string, long, string, CancellationToken>((tx, snapshot, hash, outcome, amount, currency, _) =>
            {
                tx.CheckoutSnapshotJson = snapshot;
                tx.QuoteHash = hash;
                tx.ConfirmationOutcome = outcome;
                tx.AmountInCents = amount;
                tx.Currency = currency;
            })
            .Returns(Task.CompletedTask);

        var paymentLinks = new Mock<IPaymentLinkService>();
        var quotes = new Mock<ICheckoutQuoteService>();
        quotes.Setup(q => q.ComputeHash(quote)).Returns("same-billable-quote");
        var coordinator = new CheckoutPaymentCoordinator(paymentLinks.Object, lifecycle.Object, quotes.Object);
        var ctx = new AgentConversationContext
        {
            BusinessId = quote.BusinessId,
            ConversationId = quote.ConversationId,
            ActivePayment = payment
        };

        var result = await coordinator.EnsurePaymentLinkAsync(ctx, quote, "+15550000000", "new-snapshot", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.LinkUrl.Should().Be("https://pay.test/original");
        result.Payment.Should().BeSameAs(payment);
        payment.CheckoutSnapshotJson.Should().Be("new-snapshot");
        paymentLinks.Verify(p => p.GenerateAnticipoLinkAsync(It.IsAny<PaymentLinkRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        lifecycle.Verify(p => p.RefreshPendingCheckoutAsync(
            payment,
            "new-snapshot",
            "same-billable-quote",
            quote.ConfirmationOutcome,
            quote.PayableCents,
            quote.Currency,
            It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task EnsurePaymentLinkAsync_WithDifferentBillableHash_CreatesNewPaymentLink()
    {
        var quote = CreateQuote();
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = quote.CheckoutKind,
            Status = PaymentTransactionStatus.Created,
            LinkUrl = "https://pay.test/original",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            QuoteHash = "old-billable-hash",
            CheckoutSnapshotJson = "old-snapshot",
            AmountInCents = quote.PayableCents,
            Currency = quote.Currency,
            ConfirmationOutcome = quote.ConfirmationOutcome
        };
        var newPayment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = quote.CheckoutKind,
            Status = PaymentTransactionStatus.Created,
            LinkUrl = "https://pay.test/new",
            PaymentReferenceId = "test_new"
        };

        var lifecycle = new Mock<IPaymentLifecycleService>();
        lifecycle.Setup(p => p.CreatePendingCheckoutAsync(
                quote.BusinessId,
                quote.ConversationId,
                quote.CheckoutKind,
                "new-snapshot",
                "new-billable-hash",
                quote.ConfirmationOutcome,
                "test_new",
                "https://pay.test/new",
                quote.PayableCents,
                quote.Currency,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync(newPayment);

        var paymentLinks = new Mock<IPaymentLinkService>();
        paymentLinks.Setup(p => p.GenerateAnticipoLinkAsync(It.IsAny<PaymentLinkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkResult(true, "https://pay.test/new", "test_new", DateTime.UtcNow.AddMinutes(30), null));
        var quotes = new Mock<ICheckoutQuoteService>();
        quotes.Setup(q => q.ComputeHash(quote)).Returns("new-billable-hash");
        var coordinator = new CheckoutPaymentCoordinator(paymentLinks.Object, lifecycle.Object, quotes.Object);
        var ctx = new AgentConversationContext
        {
            BusinessId = quote.BusinessId,
            ConversationId = quote.ConversationId,
            ActivePayment = payment
        };

        var result = await coordinator.EnsurePaymentLinkAsync(ctx, quote, "+15550000000", "new-snapshot", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.LinkUrl.Should().Be("https://pay.test/new");
        result.Payment.Should().BeSameAs(newPayment);
        ctx.ActivePayment.Should().BeSameAs(newPayment);
        paymentLinks.Verify(p => p.GenerateAnticipoLinkAsync(It.IsAny<PaymentLinkRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        lifecycle.Verify(p => p.RefreshPendingCheckoutAsync(It.IsAny<PaymentTransaction>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        lifecycle.Verify(p => p.MarkSupersededAsync(payment, newPayment.PaymentTransactionId, It.IsAny<CancellationToken>()), Times.Once);
    }
    [Fact]
    public async Task EnsurePaymentLinkAsync_WithConfirmedSameQuote_CreatesNewPaymentLink()
    {
        var quote = CreateQuote();
        var confirmedPayment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            BusinessId = quote.BusinessId,
            ConversationId = quote.ConversationId,
            CheckoutKind = quote.CheckoutKind,
            Status = PaymentTransactionStatus.Confirmed,
            LinkUrl = "https://pay.test/confirmed",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30),
            QuoteHash = "same-billable-quote"
        };
        var newPayment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            BusinessId = quote.BusinessId,
            ConversationId = quote.ConversationId,
            CheckoutKind = quote.CheckoutKind,
            Status = PaymentTransactionStatus.Created,
            LinkUrl = "https://pay.test/new",
            PaymentReferenceId = "test_new"
        };

        var lifecycle = new Mock<IPaymentLifecycleService>();
        lifecycle.Setup(p => p.CreatePendingCheckoutAsync(
                quote.BusinessId,
                quote.ConversationId,
                quote.CheckoutKind,
                "new-snapshot",
                "same-billable-quote",
                quote.ConfirmationOutcome,
                "test_new",
                "https://pay.test/new",
                quote.PayableCents,
                quote.Currency,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<int>()))
            .ReturnsAsync(newPayment);

        var paymentLinks = new Mock<IPaymentLinkService>();
        paymentLinks.Setup(p => p.GenerateAnticipoLinkAsync(It.IsAny<PaymentLinkRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PaymentLinkResult(true, "https://pay.test/new", "test_new", DateTime.UtcNow.AddMinutes(30), null));

        var quotes = new Mock<ICheckoutQuoteService>();
        quotes.Setup(q => q.ComputeHash(quote)).Returns("same-billable-quote");
        var coordinator = new CheckoutPaymentCoordinator(paymentLinks.Object, lifecycle.Object, quotes.Object);
        var ctx = new AgentConversationContext
        {
            BusinessId = quote.BusinessId,
            ConversationId = quote.ConversationId,
            ActivePayment = confirmedPayment
        };

        var result = await coordinator.EnsurePaymentLinkAsync(ctx, quote, "+15550000000", "new-snapshot", CancellationToken.None);

        result.Success.Should().BeTrue();
        result.LinkUrl.Should().Be("https://pay.test/new");
        result.Payment.Should().BeSameAs(newPayment);
        ctx.ActivePayment.Should().BeSameAs(newPayment);
        lifecycle.Verify(p => p.RefreshPendingCheckoutAsync(It.IsAny<PaymentTransaction>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        paymentLinks.Verify(p => p.GenerateAnticipoLinkAsync(It.IsAny<PaymentLinkRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }
    private static CheckoutPaymentCoordinator CreateCoordinator(Mock<IPaymentLifecycleService> lifecycle) =>
        new(
            Mock.Of<IPaymentLinkService>(),
            lifecycle.Object,
            Mock.Of<ICheckoutQuoteService>());

    private static CheckoutQuote CreateQuote() =>
        new(
            BusinessId: Guid.NewGuid(),
            ConversationId: Guid.NewGuid(),
            CheckoutKind: CheckoutKind.Reservation,
            ServiceId: Guid.NewGuid(),
            ServiceName: "Corte basico de adulto",
            ServiceCategory: "Corte",
            DurationMinutes: 30,
            LineItems: [new CheckoutQuoteLineItem("Corte basico de adulto", 30000m)],
            TotalCents: 3000000,
            PayableCents: 3000000,
            Currency: "COP",
            PaymentMethodKey: "transfer",
            PaymentMethodLabel: "Transferencia",
            PaymentPercentage: 100,
            TemplateId: "checkout_with_deposit",
            ConfirmationOutcome: "reservation",
            RequiredFactRoles: new Dictionary<string, string>(),
            SystemFactBindings: new Dictionary<string, string>(),
            TemplateFactBindings: new Dictionary<string, string>(),
            IssuedAtUtc: DateTime.UtcNow,
            ExpiresAtUtc: DateTime.UtcNow.AddMinutes(30));
}
