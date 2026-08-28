using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Common.Interfaces;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Catalog;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class ProductAdminService : IProductAdminService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductAdminService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
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
        var categories = await _unitOfWork.ProductCategories.ListAsync(businessId, true, ct);
        var categoryById = categories.ToDictionary(category => category.ProductCategoryId);

        string? ResolveArea(Guid? categoryId)
        {
            if (categoryId is null) return null;
            ProductCategory? area = null;
            var currentId = categoryId;
            var visited = new HashSet<Guid>();
            while (currentId is { } id && visited.Add(id) && categoryById.TryGetValue(id, out var current))
            {
                area = current;
                currentId = current.ParentProductCategoryId;
            }
            return area?.Name;
        }

        return new PagedResponse<ProductDto>(
            items.Select(product => MapToDto(product, ResolveArea(product.ProductCategoryId))).ToList(),
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
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainValidationException("Name", "El nombre del producto es obligatorio.");
        if (name.Length > 200)
            throw new DomainValidationException("Name", "El nombre no puede superar 200 caracteres.");

        var reference = NormalizeOptional(request.Reference);
        var description = NormalizeOptional(request.Description);
        var categoryName = NormalizeOptional(request.CategoryName);
        if (reference?.Length > 120)
            throw new DomainValidationException("Reference", "La referencia no puede superar 120 caracteres.");
        if (description?.Length > 2000)
            throw new DomainValidationException("Description", "La descripcion no puede superar 2000 caracteres.");
        if (categoryName?.Length > 150)
            throw new DomainValidationException("CategoryName", "La categoria no puede superar 150 caracteres.");
        var category = await ResolveCategoryAsync(product, categoryName, ct);


        var searchIndexChanged =
            !string.Equals(product.Name, name, StringComparison.Ordinal)
            || !string.Equals(product.Reference, reference, StringComparison.Ordinal)
            || !string.Equals(product.Description, description, StringComparison.Ordinal)
            || !string.Equals(product.CategoryName, categoryName, StringComparison.Ordinal);
        var productChanged = searchIndexChanged
            || product.ProductCategoryId != category?.ProductCategoryId;
        var oldState = MapToDto(product);
        if (!productChanged)
            return oldState;

        product.Name = name;
        product.Reference = reference;
        product.Sku = reference;
        product.Description = description;
        product.CategoryName = categoryName;
        product.UpdatedAt = DateTime.UtcNow;
        product.ProductCategoryId = category?.ProductCategoryId;
        await _unitOfWork.Products.UpdateAsync(product, ct);
        if (searchIndexChanged)
            await _unitOfWork.Products.ReplaceSearchTermsAsync(product, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        var updated = MapToDto(product);

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

    public async Task<IReadOnlyList<ProductCategoryAdminDto>> GetCategoriesAsync(
        Guid tenantId,
        Guid businessId,
        bool includeInactive = false,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var categories = await _unitOfWork.ProductCategories.ListAsync(businessId, includeInactive, ct);
        return MapCategoryTree(categories);
    }

    public async Task<ProductCategoryAdminDto> CreateCategoryAsync(
        Guid tenantId,
        Guid businessId,
        CreateProductCategoryRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var name = RequiredCategoryName(request.Name);
        var categories = await _unitOfWork.ProductCategories.ListAsync(businessId, true, ct);
        EnsureUniqueCategoryName(categories, name, null);
        EnsureValidCategoryParent(categories, request.ParentProductCategoryId, null);
        var now = DateTime.UtcNow;
        var category = new ProductCategory
        {
            ProductCategoryId = Guid.NewGuid(),
            BusinessId = businessId,
            ParentProductCategoryId = request.ParentProductCategoryId,
            Name = name,
            DisplayOrder = request.DisplayOrder,
            IsActive = true,
            IsBrowsable = request.IsBrowsable,
            CreatedAt = now,
            UpdatedAt = now
        };
        await _unitOfWork.ProductCategories.CreateAsync(category, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        var updated = await _unitOfWork.ProductCategories.ListAsync(businessId, true, ct);
        return MapCategoryTree(updated).Single(item => item.ProductCategoryId == category.ProductCategoryId);
    }

    public async Task<ProductCategoryAdminDto> UpdateCategoryAsync(
        Guid tenantId,
        Guid businessId,
        Guid productCategoryId,
        UpdateProductCategoryRequest request,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var categories = await _unitOfWork.ProductCategories.ListAsync(businessId, true, ct);
        var category = await _unitOfWork.ProductCategories.GetByIdAsync(businessId, productCategoryId, ct)
            ?? throw new NotFoundException(nameof(ProductCategory), productCategoryId);
        var name = RequiredCategoryName(request.Name);
        EnsureUniqueCategoryName(categories, name, productCategoryId);
        EnsureValidCategoryParent(categories, request.ParentProductCategoryId, productCategoryId);
        var oldName = category.Name;
        category.ParentProductCategoryId = request.ParentProductCategoryId;
        category.Name = name;
        category.DisplayOrder = request.DisplayOrder;
        category.IsActive = request.IsActive;
        category.IsBrowsable = request.IsBrowsable;
        category.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.ProductCategories.UpdateAsync(category, ct);
        if (!string.Equals(oldName, name, StringComparison.Ordinal))
            await _unitOfWork.Products.UpdateCategoryNameAsync(businessId, productCategoryId, name, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        var updated = await _unitOfWork.ProductCategories.ListAsync(businessId, true, ct);
        return MapCategoryTree(updated).Single(item => item.ProductCategoryId == productCategoryId);
    }

    private static string RequiredCategoryName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length is < 1 or > 150)
            throw new DomainValidationException("Name", "La categoría debe tener entre 1 y 150 caracteres.");
        return name;
    }

    private static void EnsureUniqueCategoryName(
        IReadOnlyList<ProductCategory> categories,
        string name,
        Guid? currentId)
    {
        if (categories.Any(category => category.ProductCategoryId != currentId
            && string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase)))
            throw new DomainValidationException("Name", "Ya existe una categoría con este nombre en el negocio.");
    }

    private static void EnsureValidCategoryParent(
        IReadOnlyList<ProductCategory> categories,
        Guid? parentId,
        Guid? currentId)
    {
        if (parentId is null) return;
        if (parentId == currentId)
            throw new DomainValidationException("ParentProductCategoryId", "Una categoría no puede ser su propio padre.");
        var byId = categories.ToDictionary(category => category.ProductCategoryId);
        if (!byId.TryGetValue(parentId.Value, out var parent))
            throw new DomainValidationException("ParentProductCategoryId", "La categoría padre no pertenece al negocio.");
        var depth = 2;
        var visited = new HashSet<Guid> { parent.ProductCategoryId };
        while (parent.ParentProductCategoryId is Guid ancestorId)
        {
            if (ancestorId == currentId || !visited.Add(ancestorId))
                throw new DomainValidationException("ParentProductCategoryId", "La jerarquía de categorías contiene un ciclo.");
            if (!byId.TryGetValue(ancestorId, out var nextParent))
                throw new DomainValidationException("ParentProductCategoryId", "La jerarquía de categorías está incompleta.");
            parent = nextParent;
            depth++;
        }
        if (depth > 4)
            throw new DomainValidationException("ParentProductCategoryId", "La clasificación admite Área, Línea, Grupo y Subgrupo.");
    }

    private static IReadOnlyList<ProductCategoryAdminDto> MapCategoryTree(
        IReadOnlyList<ProductCategory> categories)
    {
        var byId = categories.ToDictionary(category => category.ProductCategoryId);
        (int Depth, string Path) Resolve(ProductCategory category, HashSet<Guid> visited)
        {
            if (!visited.Add(category.ProductCategoryId))
                return (0, category.Name);
            if (category.ParentProductCategoryId is not Guid parentId
                || !byId.TryGetValue(parentId, out var parent))
                return (0, category.Name);
            var resolved = Resolve(parent, visited);
            return (Math.Min(3, resolved.Depth + 1), $"{resolved.Path} > {category.Name}");
        }
        return categories.Select(category =>
            {
                var resolved = Resolve(category, []);
                return new ProductCategoryAdminDto(
                    category.ProductCategoryId,
                    category.ParentProductCategoryId,
                    category.Name,
                    category.DisplayOrder,
                    category.IsActive,
                    category.IsBrowsable,
                    resolved.Depth,
                    resolved.Path);
            })
            .OrderBy(category => category.Path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
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

    private static ProductDto MapToDto(Product p, string? areaName = null) => new(
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
        p.LastSyncedAt,
        p.ProductCode,
        areaName,
        p.Reference);
}
