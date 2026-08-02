using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Organization;

namespace Auraly.Foundation.Tests;

public sealed class OrganizationTests
{
    [Fact]
    public void Warehouse_owns_the_negative_stock_policy()
    {
        var warehouse = new Warehouse(
            new WarehouseId(Guid.NewGuid()),
            new BusinessId(Guid.NewGuid()),
            "B01",
            "Principal",
            allowNegativeStockSales: false);

        warehouse.SetNegativeStockPolicy(true);

        Assert.True(warehouse.AllowNegativeStockSales);
        Assert.DoesNotContain(
            typeof(Warehouse).GetProperties(),
            property => property.Name.Contains("Device", StringComparison.OrdinalIgnoreCase));
    }
}