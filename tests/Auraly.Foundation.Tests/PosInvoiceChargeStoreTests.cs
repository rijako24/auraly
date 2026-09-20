using Auraly.Contracts.Sales;
using Auraly.Pos.Edge.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Auraly.Foundation.Tests;

public sealed class PosInvoiceChargeStoreTests
{
    [Fact]
    public async Task Interrupted_download_keeps_previous_charges_and_cursor_until_atomic_promotion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auraly-charges-{Guid.NewGuid():N}.db");
        try
        {
            var store = new PosCatalogStore($"Data Source={path}");
            await store.InitializeAsync();
            var businessId = Guid.NewGuid();
            var original = Charge(businessId);
            await store.StageInvoiceChargePageAsync(businessId, 1, new([original], 1, 100, 1, 1));
            await store.PromoteInvoiceChargesAsync(businessId, 1, 1);
            var changed = original with { Version = 2, Value = 9000 };
            await store.StageInvoiceChargePageAsync(businessId, 2, new([changed], 1, 100, 2, 1));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.PromoteInvoiceChargesAsync(businessId, 2, 2));
            var reopened = new PosCatalogStore($"Data Source={path}");
            Assert.Equal(5000, Assert.Single((await reopened.InvoiceChargesAsync(businessId)).Items).Value);
            Assert.Equal(1, await reopened.ConfigurationCursorAsync());
            var second = Charge(businessId) with { Code = "AGOTADOS", Name = "Agotados", InclusionMode = "Never", InvoiceAmountLimit = null };
            await reopened.StageInvoiceChargePageAsync(businessId, 2, new([changed, second], 1, 100, 2, 1));
            await reopened.PromoteInvoiceChargesAsync(businessId, 2, 2);
            Assert.Equal(2, await reopened.ConfigurationCursorAsync());
            var page = await reopened.InvoiceChargesAsync(businessId);
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(9000, page.Items.Single(x => x.ChargeId == original.ChargeId).Value);
            Assert.Empty((await reopened.InvoiceChargesAsync(Guid.NewGuid())).Items);

            // A completed empty snapshot removes charges inactivated at source.
            await reopened.StageInvoiceChargePageAsync(businessId, 3, new([], 1, 100, 0, 0));
            await reopened.PromoteInvoiceChargesAsync(businessId, 3, 0);
            Assert.Empty((await reopened.InvoiceChargesAsync(businessId)).Items);
            Assert.Equal(3, await reopened.ConfigurationCursorAsync());
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public async Task Cross_business_configuration_is_rejected_before_local_writes()
    {
        var store = new PosCatalogStore("Data Source=:memory:");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.StageInvoiceChargePageAsync(
            Guid.NewGuid(), 1, new([Charge(Guid.NewGuid())], 1, 100, 1, 1)));
    }

    private static InvoiceChargeDefinition Charge(Guid businessId) => new(
        Guid.NewGuid(), businessId, 1, "DOM", "Domicilio", true, 0, "Fixed", 5000,
        "UpToInvoiceAmount", 80000, Guid.NewGuid(), "Domicilios", Guid.NewGuid(), "TEST", "Gasto de prueba",
        null, null, Guid.NewGuid(), "Impuesto de prueba", "01", 0, [],
        [new(Guid.NewGuid(), "Proveedor de prueba", "TEST", 30, true)]);
}
