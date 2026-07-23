using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IProductAdminService
{
    Task<PagedResponse<ProductDto>> GetPagedByBusinessIdAsync(
        Guid tenantId,
        Guid businessId,
        PagedRequest request,
        bool includeInactive = false,
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
}
