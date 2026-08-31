using System.Net;
using System.Security.Cryptography;
using System.Text;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class WompiPaymentLinkServiceTests
{
    [Fact]
    public async Task Widget_signature_uses_reference_amount_currency_expiration_and_integrity_secret()
    {
        var businessId = Guid.NewGuid();
        var expiresAt = new DateTimeOffset(2026, 8, 30, 17, 45, 12, 345, TimeSpan.Zero);
        var service = CreateService(businessId, new WompiIntegration
        {
            PublicKey = "pub_test_safe",
            IntegritySecret = "test_integrity_secret"
        });

        var result = await service.PrepareWidgetCheckoutAsync(new(
            businessId, "tenant-draft-123", 1_019_660, "cop", expiresAt,
            "https://app.auraly.co/provisioning/status"));

        var expiration = "2026-08-30T17:45:12.345Z";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"tenant-draft-1231019660COP{expiration}test_integrity_secret"))).ToLowerInvariant();
        Assert.True(result.Success);
        Assert.Equal("pub_test_safe", result.PublicKey);
        Assert.Equal(expected, result.IntegritySignature);
        Assert.Equal(expiration, result.ExpirationTime);
        Assert.DoesNotContain("integrity_secret", System.Text.Json.JsonSerializer.Serialize(result),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Polling_correlates_widget_transaction_by_reference()
    {
        var businessId = Guid.NewGuid();
        var response = """
            {"data":[{"id":"tx-widget","status":"APPROVED","amount_in_cents":11990000,"reference":"tenant-draft-123","payment_link_id":null}]}
            """;
        var service = CreateService(businessId, new WompiIntegration
        {
            PrivateKey = "prv_test_safe"
        }, new StaticResponseHandler(response));

        var result = await service.CheckPaymentStatusAsync("tenant-draft-123", businessId);

        Assert.True(result.IsApproved);
        Assert.Equal("tx-widget", result.TransactionId);
        Assert.Equal(11_990_000, result.AmountInCents);
    }

    [Theory]
    [InlineData("", 100, "COP")]
    [InlineData("reference", 0, "COP")]
    [InlineData("reference", 100, "USD")]
    public async Task Widget_rejects_invalid_unsigned_input(string reference, long amount, string currency)
    {
        var businessId = Guid.NewGuid();
        var service = CreateService(businessId, new WompiIntegration
        {
            PublicKey = "pub_test_safe",
            IntegritySecret = "test_integrity_secret"
        });

        var result = await service.PrepareWidgetCheckoutAsync(new(
            businessId, reference, amount, currency));

        Assert.False(result.Success);
        Assert.Null(result.IntegritySignature);
    }

    private static WompiPaymentLinkService CreateService(
        Guid businessId,
        WompiIntegration wompi,
        HttpMessageHandler? handler = null)
    {
        var integrations = new Mock<IIntegrationsConfigProvider>();
        integrations.Setup(value => value.GetWompiAsync(
                businessId, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(wompi);
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(value => value.CreateClient(It.IsAny<string>()))
            .Returns(new HttpClient(handler ?? new StaticResponseHandler("{}")));
        return new WompiPaymentLinkService(
            clients.Object, integrations.Object, NullLogger<WompiPaymentLinkService>.Instance);
    }

    private sealed class StaticResponseHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
    }
}
