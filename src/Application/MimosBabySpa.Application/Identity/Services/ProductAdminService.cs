using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Catalog;
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

    public async Task<ProductDto> UpdateAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        UpdateProductRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var product = await _unitOfWork.Products.GetByIdAsync(businessId, productId, ct)
            ?? throw new NotFoundException(nameof(Product), productId);

        var name = request.Name?.Trim() ?? string.Empty;
        var currency = request.Currency?.Trim().ToUpperInvariant() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Name", "El nombre del producto es obligatorio.");
        if (name.Length > 200)
            throw new DomainValidationException("Name", "El nombre no puede superar 200 caracteres.");
        if (request.UnitPrice < 0)
            throw new DomainValidationException("UnitPrice", "El precio no puede ser negativo.");
        if (currency.Length != 3 || !currency.All(char.IsLetter))
            throw new DomainValidationException("Currency", "La moneda debe ser un codigo de tres letras.");

        var description = NormalizeOptional(request.Description);
        var categoryName = NormalizeOptional(request.CategoryName);
        if (description?.Length > 2000)
            throw new DomainValidationException("Description", "La descripcion no puede superar 2000 caracteres.");
        if (categoryName?.Length > 150)
            throw new DomainValidationException("CategoryName", "La categoria no puede superar 150 caracteres.");
        var category = await ResolveCategoryAsync(product, categoryName, ct);


        var searchIndexChanged =
            !string.Equals(product.Name, name, StringComparison.Ordinal)
            || !string.Equals(product.Description, description, StringComparison.Ordinal)
            || !string.Equals(product.CategoryName, categoryName, StringComparison.Ordinal);
        var productChanged = searchIndexChanged
            || product.UnitPrice != request.UnitPrice
            || !string.Equals(product.Currency, currency, StringComparison.Ordinal)
            || product.ProductCategoryId != category?.ProductCategoryId;

        var oldState = MapToDto(product);
        if (!productChanged)
            return oldState;

        product.Name = name;
        product.Description = description;
        product.CategoryName = categoryName;
        product.UnitPrice = request.UnitPrice;
        product.Currency = currency;
        product.UpdatedAt = DateTime.UtcNow;
        product.ProductCategoryId = category?.ProductCategoryId;

        await _unitOfWork.Products.UpdateAsync(product, ct);
        if (searchIndexChanged)
            await _unitOfWork.Products.ReplaceSearchTermsAsync(product, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var updated = MapToDto(product);
        await _auditService.LogAsync(
            "Update",
            "Product",
            product.ProductId.ToString(),
            oldState,
            updated,
            ct);

        return updated;
    }

    public async Task<IReadOnlyList<string>> GetSearchTermsAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var product = await _unitOfWork.Products.GetByIdAsync(businessId, productId, ct)
            ?? throw new NotFoundException(nameof(Product), productId);
        return ProductSearchText.GetVisibleProductTerms(
            product.Name,
            product.Sku,
            product.ExternalProductId,
            product.CategoryName);
    }

    private async Task<ProductCategory?> ResolveCategoryAsync(
        Product product,
        string? categoryName,
        CancellationToken ct)
    {
        if (categoryName is null)
            return null;
        var category = await _unitOfWork.ProductCategories.GetByNameAsync(
            product.BusinessId,
            product.IntegrationConnectionId,
            categoryName,
            ct);
        if (category is null)
        {
            var now = DateTime.UtcNow;
            category = new ProductCategory
            {
                ProductCategoryId = Guid.NewGuid(),
                BusinessId = product.BusinessId,
                IntegrationConnectionId = product.IntegrationConnectionId,
                Name = categoryName,
                DisplayOrder = 0,
                IsActive = true,
                IsBrowsable = true,
                LastSyncedAt = now,
                CreatedAt = now
            };
            await _unitOfWork.ProductCategories.CreateAsync(category, ct);
            return category;
        }

        if (category.IsActive && category.IsBrowsable)
            return category;
        category.IsActive = true;
        category.IsBrowsable = true;
        category.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.ProductCategories.UpdateAsync(category, ct);
        return category;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
