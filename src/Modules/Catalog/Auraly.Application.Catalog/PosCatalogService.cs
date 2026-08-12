using Auraly.Contracts.Catalog;

namespace Auraly.Application.Catalog;

public sealed class PosCatalogService(ICatalogStore store, TimeProvider timeProvider)
{
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
        return store.PricingSnapshotAsync(device.DeviceId, device.TenantId, device.BusinessId, ct);
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
