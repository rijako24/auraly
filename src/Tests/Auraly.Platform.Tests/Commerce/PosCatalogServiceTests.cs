using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Moq;
using Xunit;

namespace Auraly.Platform.Tests.Commerce;

public sealed class PosCatalogServiceTests
{
    [Fact]
    public async Task Warehouse_availability_uses_the_authenticated_enrollment_scope()
    {
        var productId = Guid.NewGuid();
        var device = Device();
        var store = new Mock<ICatalogStore>(MockBehavior.Strict);
        store.Setup(candidate => candidate.WarehouseAvailabilityAsync(
                device.DeviceId, device.TenantId, device.BusinessId,
                productId, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new PosCatalogService(store.Object, TimeProvider.System);

        await service.WarehouseAvailabilityAsync(
            device, productId, false, CancellationToken.None);

        store.VerifyAll();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Warehouse_availability_preserves_user_scope_validated_by_edge(
        bool requestOtherBusinesses)
    {
        var productId = Guid.NewGuid();
        var device = Device();
        var store = new Mock<ICatalogStore>(MockBehavior.Strict);
        store.Setup(candidate => candidate.WarehouseAvailabilityAsync(
                device.DeviceId,
                device.TenantId,
                device.BusinessId,
                productId,
                requestOtherBusinesses,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var service = new PosCatalogService(store.Object, TimeProvider.System);

        await service.WarehouseAvailabilityAsync(
            device, productId, requestOtherBusinesses, CancellationToken.None);

        store.VerifyAll();
    }

    [Fact]
    public async Task Catalog_synchronization_uses_enrollment_scope_without_a_permission_snapshot()
    {
        var device = Device();
        var expected = new CatalogSyncSessionResponse(
            Guid.NewGuid(), 0, 0, DateTimeOffset.UtcNow.AddMinutes(5));
        var store = new Mock<ICatalogStore>(MockBehavior.Strict);
        store.Setup(candidate => candidate.StartSyncAsync(
                device.DeviceId,
                device.TenantId,
                device.BusinessId,
                device.WarehouseId,
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);
        var service = new PosCatalogService(store.Object, TimeProvider.System);

        var result = await service.StartSyncAsync(device, CancellationToken.None);

        Assert.Same(expected, result);
        store.VerifyAll();
    }

    private static CatalogDeviceIdentity Device() =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
}
