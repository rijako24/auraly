using Auraly.Contracts.TenantBilling;
using Auraly.Contracts.Tenants;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Application.Identity.Services;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Billing;

public sealed class TenantProvisioningCheckoutServiceTests
{
    [Fact]
    public async Task Start_uses_server_quote_for_amount_and_capacity_and_persists_hashed_access()
    {
        var billingBusinessId = Guid.NewGuid();
        var quotes = new Mock<ITenantCommercialQuoteService>();
        quotes.Setup(value => value.QuoteAsync(It.IsAny<TenantQuoteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Quote());
        var store = new Mock<ITenantProvisioningCheckoutStore>();
        store.Setup(value => value.GetBillingBusinessIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(billingBusinessId);
        TenantProvisioningCheckoutSnapshot? persisted = null;
        byte[]? accessHash = null;
        var merchantVersion = 0;
        store.Setup(value => value.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<byte[]>(), It.IsAny<string>(),
                It.IsAny<TenantProvisioningCheckoutSnapshot>(), It.IsAny<byte[]>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, Guid, byte[], string, TenantProvisioningCheckoutSnapshot, byte[], DateTimeOffset, int, CancellationToken>(
                (_, _, hash, _, snapshot, _, _, version, _) =>
                { accessHash = hash; persisted = snapshot; merchantVersion = version; })
            .Returns(Task.CompletedTask);
        var payments = new Mock<IPaymentLinkService>();
        payments.Setup(value => value.PrepareWidgetCheckoutAsync(
                It.Is<WompiWidgetCheckoutRequest>(request =>
                    request.BusinessId == billingBusinessId && request.AmountInCents == 305_898_000),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((WompiWidgetCheckoutRequest request, CancellationToken _) =>
                new(true, "pub_test_safe", request.Reference, request.AmountInCents, "COP",
                    "signed", request.ExpiresAt?.ToString("O"), request.RedirectUrl, null, 7));
        var service = new TenantProvisioningCheckoutService(quotes.Object, LegalIdentityCatalog(), store.Object, payments.Object,
            Mock.Of<IPaymentConfirmationHandler>());

        var result = await service.StartAsync(new(Tenant(), new("business", "Annual", 2, 1, 1, 1),
            "https://app.auraly.co/register"), CancellationToken.None);

        Assert.Equal(3_058_980m, result.Quote.PayableAmountCop);
        Assert.Equal(305_898_000, result.Widget.AmountInCents);
        Assert.StartsWith("TP-", result.Widget.Reference);
        Assert.Equal(11, persisted!.Tenant.MaximumUsers);
        Assert.Equal(4, persisted.Tenant.MaximumEnrolledDevices);
        Assert.NotNull(accessHash);
        Assert.Equal(7, merchantVersion);
        Assert.Equal(32, accessHash!.Length);
        Assert.DoesNotContain(result.AccessToken, Convert.ToHexString(accessHash), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Paid_handler_provisions_once_and_marks_the_draft()
    {
        var draftId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var fulfillment = new TenantProvisioningFulfillment(draftId, paymentId,
            new(Tenant(), Quote()), "PaymentPending");
        var store = new Mock<ITenantProvisioningCheckoutStore>();
        store.SetupSequence(value => value.GetForFulfillmentAsync(draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(fulfillment)
            .ReturnsAsync(fulfillment with { Status = "Provisioned" });
        var tenants = new Mock<ITenantService>();
        tenants.Setup(value => value.ProvisionAsync(It.IsAny<ProvisionTenantRequest>(), null,
                It.IsAny<TenantQuoteDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProvisionTenantResult(Guid.NewGuid(), tenantId, Guid.NewGuid(), "@tenant",
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Provisioned"));
        var handler = new TenantProvisioningPaidCheckoutHandler(
            store.Object, tenants.Object, NullLogger<TenantProvisioningPaidCheckoutHandler>.Instance);
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = paymentId,
            Status = PaymentTransactionStatus.Confirmed,
            CheckoutKind = CheckoutKind.TenantProvisioning,
            SubjectType = "TenantProvisioning",
            SubjectId = draftId
        };

        await handler.FulfillAsync(payment, CancellationToken.None);
        Assert.True(await handler.IsFulfilledAsync(payment, CancellationToken.None));

        tenants.Verify(value => value.ProvisionAsync(It.IsAny<ProvisionTenantRequest>(), null,
            It.IsAny<TenantQuoteDto>(),
            It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(value => value.MarkProvisionedAsync(draftId, tenantId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("other-reference", 10000)]
    [InlineData("TP-expected", 9999)]
    public async Task Widget_confirmation_rejects_a_transaction_that_does_not_match_the_immutable_quote(
        string providerReference, long providerAmount)
    {
        var draftId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        const string token = "AQIDBA";
        var store = new Mock<ITenantProvisioningCheckoutStore>();
        store.Setup(value => value.GetPaymentForVerificationAsync(
                draftId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProvisioningPaymentVerification(
                draftId, Guid.NewGuid(), businessId, "TP-expected", 10000, 1));
        var payments = new Mock<IPaymentLinkService>();
        payments.Setup(value => value.VerifyTransactionAsync("provider-transaction", businessId,
                It.IsAny<CancellationToken>(), 1))
            .ReturnsAsync(new VerifiedTransactionResult(true, "provider-transaction", providerAmount,
                null, providerReference, null));
        var confirmation = new Mock<IPaymentConfirmationHandler>();
        var service = new TenantProvisioningCheckoutService(
            Mock.Of<ITenantCommercialQuoteService>(), LegalIdentityCatalog(), store.Object, payments.Object, confirmation.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ConfirmWidgetPaymentAsync(
            draftId, token, new("provider-transaction"), CancellationToken.None));

        confirmation.Verify(value => value.HandleAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>(),
            It.IsAny<PaymentTransactionSource?>()), Times.Never);
    }

    [Fact]
    public async Task Widget_confirmation_verifies_with_Wompi_then_uses_the_canonical_idempotent_handler()
    {
        var draftId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        const string token = "AQIDBA";
        var store = new Mock<ITenantProvisioningCheckoutStore>();
        store.Setup(value => value.GetPaymentForVerificationAsync(
                draftId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProvisioningPaymentVerification(
                draftId, Guid.NewGuid(), businessId, "TP-expected", 10000, 1));
        store.Setup(value => value.GetStatusAsync(draftId, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProvisioningCheckoutStatusDto(
                draftId, "Provisioned", "Confirmed", Guid.NewGuid(), "@tenant", null));
        var payments = new Mock<IPaymentLinkService>();
        payments.Setup(value => value.VerifyTransactionAsync("provider-transaction", businessId,
                It.IsAny<CancellationToken>(), 1))
            .ReturnsAsync(new VerifiedTransactionResult(true, "provider-transaction", 10000,
                null, "TP-expected", null));
        var confirmation = new Mock<IPaymentConfirmationHandler>();
        confirmation.Setup(value => value.HandleAsync("TP-expected", "provider-transaction", 10000,
                It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<PaymentTransactionSource?>()))
            .ReturnsAsync(new PaymentConfirmationResult(true, null));
        var service = new TenantProvisioningCheckoutService(
            Mock.Of<ITenantCommercialQuoteService>(), LegalIdentityCatalog(), store.Object, payments.Object, confirmation.Object);

        var result = await service.ConfirmWidgetPaymentAsync(
            draftId, token, new("provider-transaction"), CancellationToken.None);

        Assert.Equal("Provisioned", result.Status);
        confirmation.VerifyAll();
    }

    private static TenantQuoteDto Quote() => new(
        "business", "Negocio", "Annual", 299_900m, 12, 3_598_800m, .15m,
        539_820m, 0m, 3_058_980m, 254_915m, 10, 1, 4, 2_500, 30, []);

    private static ProvisionTenantRequest Tenant() => new(
        Guid.NewGuid(), "Empresa SAS", "Empresa", "Organization", "NIT", "900123456",
        TenantProvisioningRequestValidator.CalculateNitVerificationDigit("900123456").ToString(),
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Calle 1", "3001234567",
        "empresa@example.com", "R-99-PN", "Sede principal", "Calle 1", "3001234567",
        "sede@example.com", "America/Bogota", "LatestReceiptCost", "admin@example.com", 1, 0);

    private static ITenantCommercialCatalogStore LegalIdentityCatalog()
    {
        var catalog = new Mock<ITenantCommercialCatalogStore>();
        catalog.Setup(value => value.GetLegalIdentityCatalogAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantProvisioningLegalIdentityCatalogDto(
                [new("Organization", "Persona jurídica")],
                [new("NIT", "NIT", "Organization")]));
        return catalog.Object;
    }
}
