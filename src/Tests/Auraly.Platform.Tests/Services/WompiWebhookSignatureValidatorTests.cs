using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Platform.Infrastructure.Services;
using Xunit;

namespace Auraly.Platform.Tests.Services;

public sealed class WompiWebhookSignatureValidatorTests
{
    [Fact]
    public void Validate_rejects_when_events_secret_is_missing()
    {
        using var document = JsonDocument.Parse(ValidPayload("unused"));

        Assert.False(new WompiWebhookSignatureValidator().Validate(document.RootElement, string.Empty));
    }

    [Fact]
    public void Validate_accepts_the_documented_dynamic_property_signature()
    {
        const string secret = "test_events_regression";
        using var document = JsonDocument.Parse(ValidPayload(secret));

        Assert.True(new WompiWebhookSignatureValidator().Validate(document.RootElement, secret));
    }

    [Fact]
    public void Validate_rejects_unknown_signed_property_instead_of_treating_it_as_empty()
    {
        const string secret = "test_events_regression";
        using var document = JsonDocument.Parse(ValidPayload(secret).Replace(
            "transaction.amount_in_cents",
            "transaction.missing",
            StringComparison.Ordinal));

        Assert.False(new WompiWebhookSignatureValidator().Validate(document.RootElement, secret));
    }

    private static string ValidPayload(string secret)
    {
        const string transactionId = "tx-regression";
        const string status = "APPROVED";
        const string amount = "450000";
        const string timestamp = "1788105600000";
        var checksum = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(transactionId + status + amount + timestamp + secret)))
            .ToLowerInvariant();
        return JsonSerializer.Serialize(new
        {
            @event = "transaction.updated",
            data = new { transaction = new { id = transactionId, status, amount_in_cents = long.Parse(amount) } },
            signature = new
            {
                properties = new[] { "transaction.id", "transaction.status", "transaction.amount_in_cents" },
                checksum
            },
            timestamp = long.Parse(timestamp)
        });
    }
}
