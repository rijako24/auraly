using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public sealed class ProductAliasAdminService : IProductAliasAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductAliasService _aliases;

    public ProductAliasAdminService(IUnitOfWork unitOfWork, IProductAliasService aliases)
    {
        _unitOfWork = unitOfWork;
        _aliases = aliases;
    }

    public async Task<IReadOnlyList<ProductAliasDto>> GetByProductAsync(
        Guid tenantId, Guid businessId, Guid productId, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId);
        return await _aliases.GetByProductAsync(businessId, productId, ct);
    }

    public async Task<ProductAliasImportResult> ImportAsync(
        Guid tenantId, Guid businessId, ProductAliasImportRequest request, CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId);
        return await _aliases.ImportAsync(businessId, request, ct);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
