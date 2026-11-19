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
        if (cursor < 0) throw new CatalogValidationException("La posición de sincronización del catálogo no puede ser negativa.");
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
    public Task<long> PricingCursorAsync(CatalogDeviceIdentity device, CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        return store.PricingCursorAsync(device.DeviceId, device.TenantId, device.BusinessId, ct);
    }

    public Task<PosCustomerBootstrapPage> CustomerBootstrapPageAsync(
        CatalogDeviceIdentity device, string? cursor, int pageSize, CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        ValidatePageSize(pageSize);
        return store.CustomerBootstrapPageAsync(
            device.DeviceId, device.TenantId, device.BusinessId, cursor, pageSize, ct);
    }

    public Task<PosCustomerDeltaPage> CustomerChangesAsync(
        CatalogDeviceIdentity device, long cursor, int pageSize, CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        if (cursor < 0) throw new CatalogValidationException("La posición de sincronización de clientes no puede ser negativa.");
        ValidatePageSize(pageSize);
        return store.CustomerChangesAsync(
            device.DeviceId, device.TenantId, device.BusinessId, cursor, pageSize, ct);
    }
    public Task<InventoryAvailabilityResponse> AvailabilityAsync(
        CatalogDeviceIdentity device,
        InventoryAvailabilityRequest request,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        if (request.WarehouseId != device.WarehouseId)
            throw new CatalogForbiddenException("La bodega solicitada no está disponible en este contexto operativo.");
        if (request.ProductId == Guid.Empty || request.OperationId == Guid.Empty || request.Quantity <= 0)
            throw new CatalogValidationException("Selecciona un producto, una operación y una cantidad mayor que cero.");
        return store.AvailabilityAsync(
            device.DeviceId,
            device.TenantId,
            device.BusinessId,
            request,
            ct);
    }

    public Task<IReadOnlyList<InventoryAvailabilityResponse>> AvailabilityBatchAsync(
        CatalogDeviceIdentity device,
        InventoryAvailabilityBatchRequest request,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        if (request.WarehouseId != device.WarehouseId)
            throw new CatalogForbiddenException("La bodega solicitada no está disponible en este contexto operativo.");
        if (request.OperationId == Guid.Empty || request.Items is null or { Count: < 1 or > 500 } ||
            request.Items.Any(item => item is null || item.ProductId == Guid.Empty || item.Quantity <= 0) ||
            request.Items.Select(item => item.ProductId).Distinct().Count() != request.Items.Count)
            throw new CatalogValidationException("Selecciona entre 1 y 500 productos distintos con cantidades mayores que cero y una operación.");
        return store.AvailabilityBatchAsync(
            device.DeviceId, device.TenantId, device.BusinessId, request, ct);
    }

    public Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> WarehouseAvailabilityAsync(
        CatalogDeviceIdentity device,
        Guid productId,
        bool includeOtherBusinesses,
        CancellationToken ct)
    {
        ValidateEnrolledScope(device);
        if (productId == Guid.Empty)
            throw new CatalogValidationException("Se requiere un producto válido.");
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
            throw new CatalogValidationException("La cantidad de registros por página debe estar entre 1 y 2.000.");
    }
}
