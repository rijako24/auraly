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
    public async Task AbandonActiveCheckoutAsync_WithMatchingCreatedCheckout_AbandonsPaymentAndClearsContext()
    {
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            CheckoutKind = CheckoutKind.Order,
            Status = PaymentTransactionStatus.Created
        };
        var lifecycle = new Mock<IPaymentLifecycleService>();
        lifecycle.Setup(p => p.MarkAbandonedAsync(payment, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var coordinator = CreateCoordinator(lifecycle);
        var ctx = new AgentToolContext
        {
            ConversationId = Guid.NewGuid(),
            ActivePayment = payment
        };

        var result = await coordinator.AbandonActiveCheckoutAsync(ctx, CheckoutKind.Order, CancellationToken.None);

        result.AbandonedPayment.Should().BeSameAs(payment);
        ctx.ActivePayment.Should().BeNull();
        lifecycle.Verify(p => p.MarkAbandonedAsync(payment, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AbandonActiveCheckoutAsync_WithDifferentCheckoutKind_DoesNotAbandonPayment()
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

        var result = await coordinator.AbandonActiveCheckoutAsync(ctx, CheckoutKind.Order, CancellationToken.None);

        result.AbandonedPayment.Should().BeNull();
        ctx.ActivePayment.Should().BeSameAs(payment);
        lifecycle.Verify(p => p.MarkAbandonedAsync(It.IsAny<PaymentTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CheckoutPaymentCoordinator CreateCoordinator(Mock<IPaymentLifecycleService> lifecycle) =>
        new(
            Mock.Of<IPaymentLinkService>(),
            lifecycle.Object,
            Mock.Of<ICheckoutQuoteService>());
}
