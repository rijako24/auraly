using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

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
}
