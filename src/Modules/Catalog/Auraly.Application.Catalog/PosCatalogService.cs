using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public sealed class PosCatalogService(ICatalogStore store, TimeProvider timeProvider)
{
    public Task<CatalogSyncSessionResponse> StartSyncAsync(CatalogDeviceIdentity device, CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        return store.StartSyncAsync(
            device.DeviceId,
            device.TenantId,
            device.BusinessId,
            device.WarehouseId,
            timeProvider.GetUtcNow(),
            ct);
    }

    public Task<CatalogBootstrapPage> BootstrapPageAsync(
        CatalogDeviceIdentity device,
        Guid sessionId,
        string? cursor,
        int pageSize,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        ValidatePageSize(pageSize);
        return store.BootstrapPageAsync(device.DeviceId, sessionId, cursor, pageSize, ct);
    }

    public Task<CatalogDeltaPage> ChangesAsync(
        CatalogDeviceIdentity device,
        long cursor,
        int pageSize,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        if (cursor < 0) throw new CatalogValidationException("The catalog cursor cannot be negative.");
        ValidatePageSize(pageSize);
        return store.ChangesAsync(device.DeviceId, device.TenantId, device.BusinessId,
            device.WarehouseId, cursor, pageSize, ct);
    }

    public Task<PosPricingSnapshot> PricingSnapshotAsync(
        CatalogDeviceIdentity device,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        return store.PricingSnapshotAsync(device.DeviceId, device.TenantId, device.BusinessId, device.WarehouseId, ct);
    }
    public Task<InventoryAvailabilityResponse> AvailabilityAsync(
        CatalogDeviceIdentity device,
        InventoryAvailabilityRequest request,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        if (request.WarehouseId != device.WarehouseId)
            throw new CatalogForbiddenException("The requested warehouse is not available in this operational context.");
        if (request.ProductId == Guid.Empty || request.OperationId == Guid.Empty || request.Quantity <= 0)
            throw new CatalogValidationException("Product, operation and a positive quantity are required.");
        return store.AvailabilityAsync(
            device.DeviceId,
            device.TenantId,
            device.BusinessId,
            request,
            ct);
    }

    public Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> WarehouseAvailabilityAsync(
        CatalogDeviceIdentity device,
        Guid productId,
        bool includeOtherBusinesses,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        if (productId == Guid.Empty)
            throw new CatalogValidationException("ProductId is required.");
        return store.WarehouseAvailabilityAsync(
            device.DeviceId, device.TenantId, device.BusinessId, productId,
            includeOtherBusinesses, ct);
    }

    private static void ValidateEnrolledScope(CatalogDeviceIdentity device)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (device.DeviceId == Guid.Empty || device.TenantId == Guid.Empty ||
            device.BusinessId == Guid.Empty)
            throw new CatalogForbiddenException(
                "The enrolled POS device context is incomplete.");
    }

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > 2000)
            throw new CatalogValidationException("PageSize must be between 1 and 2000.");
    }
}
