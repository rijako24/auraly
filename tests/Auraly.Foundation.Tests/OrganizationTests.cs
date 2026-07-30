using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Organization;

namespace Auraly.Foundation.Tests;

public sealed class OrganizationTests
{
    [Fact]
    public void Register_stores_only_the_warehouse_link_not_a_duplicate_negative_policy()
    {
        var businessId = new BusinessId(Guid.NewGuid());
        var warehouse = new Warehouse(
            new WarehouseId(Guid.NewGuid()),
            businessId,
            "B01",
            "Principal",
            allowNegativeStockSales: false);
        var register = new Register(
            new RegisterId(Guid.NewGuid()),
            businessId,
            warehouse.Id,
            "C01");

        warehouse.SetNegativeStockPolicy(true);

        Assert.Equal(warehouse.Id, register.WarehouseId);
        Assert.True(warehouse.AllowNegativeStockSales);
        Assert.DoesNotContain(
            typeof(Register).GetProperties(),
            property => property.Name.Contains("Negative", StringComparison.OrdinalIgnoreCase));
    }
}
