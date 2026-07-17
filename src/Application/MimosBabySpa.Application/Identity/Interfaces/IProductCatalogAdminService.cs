using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IProductCatalogAdminService
{
    Task<ProductCatalogSyncResult> SyncAsync(Guid tenantId, Guid businessId, ProductCatalogSyncRequest request, CancellationToken ct = default);
    Task<ProductIdentityRefreshResult> RefreshProductAsync(
        Guid tenantId, Guid businessId, string query, CancellationToken ct = default);
}
