using Auraly.Contracts.TenantBilling;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Application.Services;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Billing;

public sealed class TenantSubscriptionCheckoutServiceTests
{
    [Fact]
    public async Task Start_uses_the_current_immutable_order_amount_and_platform_billing_account()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var order = Order("Draft");
        var orders = new Mock<ITenantRenewalOrderStore>();
        orders.Setup(value => value.GetCurrentAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        var store = new Mock<ITenantSubscriptionCheckoutStore>();
        store.Setup(value => value.GetBillingBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(businessId);
        var payments = new Mock<IPaymentLinkService>();
        payments.Setup(value => value.PrepareWidgetCheckoutAsync(
                It.Is<WompiWidgetCheckoutRequest>(request =>
                    request.BusinessId == businessId && request.AmountInCents == 72_828_000),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WompiWidgetCheckoutRequest request, CancellationToken _) =>
                new(true, "pub", request.Reference, request.AmountInCents, "COP",
                    "signature", null, null, null));
        var service = Create(orders.Object, store.Object, payments.Object,
            Mock.Of<IPaymentConfirmationHandler>());

        var result = await service.StartAsync(tenantId, new(), default);

        Assert.Equal(order.RenewalOrderId, result.RenewalOrderId);
        Assert.Equal(72_828_000, result.Widget.AmountInCents);
        store.Verify(value => value.CreatePaymentAsync(tenantId,
            It.IsAny<Guid>(), order.RenewalOrderId,
            $"TS-{order.RenewalOrderId:N}", 72_828_000,
            It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("wrong", 72828000)]
    [InlineData("TS-expected", 100)]
    public async Task Confirm_rejects_provider_data_that_differs_from_the_order(
        string reference, long amount)
    {
        var tenantId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var store = new Mock<ITenantSubscriptionCheckoutStore>();
        store.Setup(value => value.GetPaymentForVerificationAsync(
                tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSubscriptionPaymentVerification(
                orderId, Guid.NewGuid(), businessId, "TS-expected", 72_828_000,
                DateTimeOffset.UtcNow.AddHours(1), 0, 1));
        var payments = new Mock<IPaymentLinkService>();
        payments.Setup(value => value.VerifyTransactionAsync(
                "transaction", businessId, It.IsAny<CancellationToken>(), 1))
            .ReturnsAsync(new VerifiedTransactionResult(
                true, "transaction", amount, null, reference, null));
        var confirmation = new Mock<IPaymentConfirmationHandler>();
        var service = Create(Mock.Of<ITenantRenewalOrderStore>(), store.Object,
            payments.Object, confirmation.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmAsync(
            tenantId, orderId, new("transaction"), default));

        confirmation.Verify(value => value.HandleAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<long>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<Platform.Domain.Enums.PaymentTransactionSource?>()),
            Times.Never);
    }

    [Fact]
    public async Task Start_pending_payment_reuses_the_same_reference_without_creating_another_payment()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var order = Order("PendingPayment");
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        var orders = new Mock<ITenantRenewalOrderStore>();
        orders.Setup(value => value.GetCurrentAsync(tenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);
        var store = new Mock<ITenantSubscriptionCheckoutStore>();
        store.Setup(value => value.GetBillingBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(businessId);
        store.Setup(value => value.GetPaymentForVerificationAsync(
                tenantId, order.RenewalOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSubscriptionPaymentVerification(
                order.RenewalOrderId, Guid.NewGuid(), businessId,
                $"TS-{order.RenewalOrderId:N}", 72_828_000, expiresAt, 0, 1));
        var payments = new Mock<IPaymentLinkService>();
        payments.Setup(value => value.PrepareWidgetCheckoutAsync(
                It.Is<WompiWidgetCheckoutRequest>(request =>
                    request.Reference == $"TS-{order.RenewalOrderId:N}" &&
                    request.ExpiresAt == expiresAt), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WompiWidgetCheckoutRequest request, CancellationToken _) =>
                new(true, "pub", request.Reference, request.AmountInCents, "COP",
                    "signature", request.ExpiresAt?.ToString("O"), null, null));
        var service = Create(orders.Object, store.Object, payments.Object,
            Mock.Of<IPaymentConfirmationHandler>());

        var result = await service.StartAsync(tenantId, new(), default);

        Assert.Equal($"TS-{order.RenewalOrderId:N}", result.Widget.Reference);
        store.Verify(value => value.CreatePaymentAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RecordManualPayment_uses_the_same_confirmation_and_settlement_pipeline()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var store = new Mock<ITenantSubscriptionCheckoutStore>();
        store.Setup(value => value.CreateManualPaymentAsync(
                tenantId, actorId, It.IsAny<Guid>(), orderId,
                It.Is<RecordTenantSubscriptionPaymentRequest>(request =>
                    request.PaymentMethodCode == "Transfer" && request.Reference == "BANCO-9001"),
                It.Is<string>(snapshot => snapshot.Contains("BANCO-9001", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantSubscriptionManualPaymentPreparation(
                "TSM-internal", 72_828_000, "BANCO-9001"));
        var receipt = new TenantSubscriptionReceiptDto(
            Guid.NewGuid(), "FV-100", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddYears(1), "Annual", "COP", 612_000m,
            116_280m, 728_280m, "Transfer", "BANCO-9001", null, "PendingGeneration", []);
        store.Setup(value => value.GetReceiptAsync(tenantId, orderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(receipt);
        var confirmation = new Mock<IPaymentConfirmationHandler>();
        confirmation.Setup(value => value.HandleAsync(
                "TSM-internal", "BANCO-9001", 72_828_000, It.IsAny<string>(),
                It.IsAny<CancellationToken>(), Platform.Domain.Enums.PaymentTransactionSource.Manual))
            .ReturnsAsync(new PaymentConfirmationResult(true, null));
        var service = Create(Mock.Of<ITenantRenewalOrderStore>(), store.Object,
            Mock.Of<IPaymentLinkService>(), confirmation.Object);

        var result = await service.RecordManualPaymentAsync(
            tenantId, actorId, orderId,
            new("transfer", " BANCO-9001 ", DateTimeOffset.UtcNow, "Consignación"), default);

        Assert.Equal("FV-100", result.DocumentNumber);
        confirmation.VerifyAll();
    }

    private static TenantSubscriptionCheckoutService Create(
        ITenantRenewalOrderStore orders,
        ITenantSubscriptionCheckoutStore store,
        IPaymentLinkService payments,
        IPaymentConfirmationHandler confirmation) => new(
            new TenantRenewalOrderService(Mock.Of<ITenantCommercialQuoteService>(), orders),
            store, payments, confirmation);

    private static TenantRenewalOrderDto Order(string status)
    {
        var now = DateTimeOffset.UtcNow;
        return new(Guid.NewGuid(), 1, status, true, now, now.AddYears(1), now,
            new("starter", "Inicio", "Annual", 60_000m, 12, 720_000m, .15m,
                108_000m, 116_280m, 728_280m, 60_690m,
                1, 0, 1, 100, 0, []),
            new(1, 0, 1, 0));
    }
}
