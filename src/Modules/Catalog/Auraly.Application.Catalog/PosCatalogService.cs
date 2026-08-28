using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public sealed class PosCatalogService(ICatalogStore store, TimeProvider timeProvider)
{
    private const string InventoryReadPermission = "inventory.read";
    private const string BusinessesReadPermission = "businesses.read";
    public Task<CatalogSyncSessionResponse> StartSyncAsync(CatalogDeviceIdentity device, CancellationToken ct)
    {
        RequireSync(device);
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
        RequireSync(device);
        ValidatePageSize(pageSize);
        return store.BootstrapPageAsync(device.DeviceId, sessionId, cursor, pageSize, ct);
    }

    public Task<CatalogDeltaPage> ChangesAsync(
        CatalogDeviceIdentity device,
        long cursor,
        int pageSize,
        CancellationToken ct)
    {
        RequireSync(device);
        if (cursor < 0) throw new CatalogValidationException("The catalog cursor cannot be negative.");
        ValidatePageSize(pageSize);
        return store.ChangesAsync(device.DeviceId, device.TenantId, device.BusinessId, cursor, pageSize, ct);
    }

    public Task<PosPricingSnapshot> PricingSnapshotAsync(
        CatalogDeviceIdentity device,
        CancellationToken ct)
    {
        RequireSync(device);
        return store.PricingSnapshotAsync(device.DeviceId, device.TenantId, device.BusinessId, device.WarehouseId, ct);
    }
    public Task<InventoryAvailabilityResponse> AvailabilityAsync(
        CatalogDeviceIdentity device,
        InventoryAvailabilityRequest request,
        CancellationToken ct)
    {
        RequireSync(device);
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
        RequireSync(device);
        if (!device.Permissions.Contains(InventoryReadPermission))
            throw new CatalogForbiddenException($"Permission '{InventoryReadPermission}' is required.");
        if (productId == Guid.Empty)
            throw new CatalogValidationException("ProductId is required.");
        return store.WarehouseAvailabilityAsync(
            device.DeviceId, device.TenantId, device.BusinessId, productId,
            includeOtherBusinesses && device.Permissions.Contains(BusinessesReadPermission), ct);
    }

    private static void RequireSync(CatalogDeviceIdentity device)
    {
        if (!device.Permissions.Contains(CatalogPermissionCodes.Sync))
            throw new CatalogForbiddenException($"Permission '{CatalogPermissionCodes.Sync}' is required.");
    }

    private static void ValidatePageSize(int pageSize)
    {
        if (pageSize is < 1 or > 2000)
            throw new CatalogValidationException("PageSize must be between 1 and 2000.");
    }
}
