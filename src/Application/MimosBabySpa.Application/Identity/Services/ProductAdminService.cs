using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public sealed class ProductAdminService : IProductAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;

    public ProductAdminService(IUnitOfWork unitOfWork, IAuditService auditService)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
    }

    public async Task<PagedResponse<ProductDto>> GetPagedByBusinessIdAsync(
        Guid tenantId,
        Guid businessId,
        PagedRequest request,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var (items, totalCount) = await _unitOfWork.Products.GetPagedByBusinessIdAsync(
            businessId,
            request.Page,
            request.PageSize,
            request.Search,
            includeInactive,
            ct);

        return new PagedResponse<ProductDto>(
            items.Select(MapToDto).ToList(),
            totalCount,
            request.Page,
            request.PageSize);
    }

    public async Task<ProductDto> UpdateStatusAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        UpdateProductStatusRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var product = await _unitOfWork.Products.GetByIdAsync(businessId, productId, ct)
            ?? throw new NotFoundException(nameof(Product), productId);

        var oldState = MapToDto(product);
        product.IsActive = request.IsActive;
        product.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Products.UpdateAsync(product, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var updated = MapToDto(product);
        await _auditService.LogAsync(
            request.IsActive ? "Activate" : "Deactivate",
            "Product",
            product.ProductId.ToString(),
            oldState,
            updated,
            ct);

        return updated;
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static ProductDto MapToDto(Product p) => new(
        p.ProductId,
        p.BusinessId,
        p.IntegrationConnectionId,
        p.ExternalProductId,
        p.Source,
        p.Sku,
        p.Name,
        p.Description,
        p.CategoryName,
        p.UnitPrice,
        p.Currency,
        p.ManageStock,
        p.StockQuantity,
        p.IsActive,
        p.CreatedAt,
        p.UpdatedAt,
        p.LastSyncedAt);
}