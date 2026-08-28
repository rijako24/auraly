using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class PosCatalogServiceTests
{
    [Fact]
    public async Task Warehouse_availability_requires_inventory_read()
    {
        var store = new Mock<ICatalogStore>(MockBehavior.Strict);
        var service = new PosCatalogService(store.Object, TimeProvider.System);

        await Assert.ThrowsAsync<CatalogForbiddenException>(() =>
            service.WarehouseAvailabilityAsync(
                Device(CatalogPermissionCodes.Sync), Guid.NewGuid(), true, CancellationToken.None));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Warehouse_availability_expands_businesses_only_with_permission(
        bool requestOtherBusinesses,
        bool hasBusinessesRead)
    {
        var productId = Guid.NewGuid();
        var device = Device(
            CatalogPermissionCodes.Sync,
            "inventory.read",
            hasBusinessesRead ? "businesses.read" : null);
        var store = new Mock<ICatalogStore>(MockBehavior.Strict);
        store.Setup(candidate => candidate.WarehouseAvailabilityAsync(
                device.DeviceId,
                device.TenantId,
                device.BusinessId,
                productId,
                requestOtherBusinesses && hasBusinessesRead,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new PosCatalogService(store.Object, TimeProvider.System);

        await service.WarehouseAvailabilityAsync(
            device, productId, requestOtherBusinesses, CancellationToken.None);

        store.VerifyAll();
    }

    private static CatalogDeviceIdentity Device(params string?[] permissions) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            permissions.Where(static permission => permission is not null)
                .Select(static permission => permission!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));
}
