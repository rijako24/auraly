using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class ProductAliasAdminService : IProductAliasAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProductAliasService _aliases;

    public ProductAliasAdminService(
        IUnitOfWork unitOfWork,
        IProductAliasService aliases)
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

    public async Task<ProductAliasDto> ReviewAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        Guid productAliasId,
        ReviewProductAliasRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId);
        var oldState = (await _aliases.GetByProductAsync(businessId, productId, ct))
            .SingleOrDefault(alias => alias.ProductAliasId == productAliasId);
        var result = await _aliases.ReviewAsync(businessId, productId, productAliasId, request, ct);
        return result;
    }

    public async Task<ProductAliasDto> PromoteAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        Guid productAliasId,
        PromoteProductAliasRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId);
        var oldState = (await _aliases.GetByProductAsync(businessId, productId, ct))
            .SingleOrDefault(alias => alias.ProductAliasId == productAliasId);
        var result = await _aliases.PromoteAsync(businessId, productId, productAliasId, request, ct);
        return result;
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
