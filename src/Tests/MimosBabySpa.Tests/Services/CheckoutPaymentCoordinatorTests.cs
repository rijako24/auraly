using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Services;

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
        var ctx = new AgentToolContext
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
        var ctx = new AgentToolContext
        {
            ConversationId = Guid.NewGuid(),
            ActivePayment = payment
        };

        var result = await coordinator.DiscardActiveCheckoutAsync(ctx, CheckoutKind.Order, CancellationToken.None);

        result.DiscardedPayment.Should().BeNull();
        ctx.ActivePayment.Should().BeSameAs(payment);
        lifecycle.Verify(p => p.DiscardPendingAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CheckoutPaymentCoordinator CreateCoordinator(Mock<IPaymentLifecycleService> lifecycle) =>
        new(
            Mock.Of<IPaymentLinkService>(),
            lifecycle.Object,
            Mock.Of<ICheckoutQuoteService>());
}
