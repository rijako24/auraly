using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class ProductCatalogAdminService : IProductCatalogAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCatalogSyncService _sync;

    public ProductCatalogAdminService(IUnitOfWork unitOfWork, IProductCatalogSyncService sync)
    {
        _unitOfWork = unitOfWork;
        _sync = sync;
    }

    public async Task<ProductCatalogSyncResult> SyncAsync(
        Guid tenantId, Guid businessId, ProductCatalogSyncRequest request, CancellationToken ct = default)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
        return await _sync.SyncAsync(businessId, request, ct);
    }
    public async Task<ProductIdentityRefreshResult> RefreshProductAsync(
        Guid tenantId,
        Guid businessId,
        string query,
        CancellationToken ct = default)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
        return await _sync.RefreshProductAsync(businessId, query, ct: ct);
    }

}
