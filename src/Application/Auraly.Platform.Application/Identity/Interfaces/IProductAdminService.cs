using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IProductAdminService
{
    Task<PagedResponse<ProductDto>> GetPagedByBusinessIdAsync(
        Guid tenantId,
        Guid businessId,
        PagedRequest request,
        bool includeInactive = false,
        ProductListFilters? filters = null,
        CancellationToken ct = default);

    Task<ProductDto> UpdateStatusAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        UpdateProductStatusRequest request,
        CancellationToken ct = default);

    Task<ProductDto> UpdateAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        UpdateProductRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<ProductCategoryAdminDto>> GetCategoriesAsync(Guid tenantId, Guid businessId, bool includeInactive = false, CancellationToken ct = default);

    Task<ProductCategoryAdminDto> CreateCategoryAsync(Guid tenantId, Guid businessId, CreateProductCategoryRequest request, CancellationToken ct = default);

    Task<ProductCategoryAdminDto> UpdateCategoryAsync(Guid tenantId, Guid businessId, Guid productCategoryId, UpdateProductCategoryRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<string>> GetSearchTermsAsync(
        Guid tenantId,
        Guid businessId,
        Guid productId,
        CancellationToken ct = default);
}
