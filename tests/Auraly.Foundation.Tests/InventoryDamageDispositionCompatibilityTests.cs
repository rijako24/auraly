using System.Text.Json;
using Auraly.Contracts.Inventory;

namespace Auraly.Foundation.Tests;

public sealed class InventoryDamageDispositionCompatibilityTests
{
    [Fact]
    public void Legacy_damage_request_omits_new_disposition_from_its_idempotency_payload()
    {
        var request = new ConfirmInventoryDamageRequest(Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow,
            "DAMAGE", null, null, [new(1, Guid.NewGuid(), 1m)]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacyJson = JsonSerializer.Serialize(request, options);
        Assert.DoesNotContain("disposition", legacyJson,
            StringComparison.OrdinalIgnoreCase);
        var irreversibleJson = JsonSerializer.Serialize(
            request with { Disposition = "Irreversible" }, options);
        Assert.Contains("\"disposition\":\"Irreversible\"",
            irreversibleJson);
    }
}
