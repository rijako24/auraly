using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IPromotionAdminService
{
    Task<PromotionDto> GetByIdAsync(Guid tenantId, Guid businessId, Guid promotionId, CancellationToken ct = default);
    Task<PagedResponse<PromotionDto>> GetPagedByBusinessIdAsync(Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct = default);
    Task<PromotionDto> CreateAsync(Guid tenantId, Guid businessId, CreatePromotionRequest request, CancellationToken ct = default);
    Task<PromotionDto> UpdateAsync(Guid tenantId, Guid businessId, Guid promotionId, UpdatePromotionRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, Guid businessId, Guid promotionId, CancellationToken ct = default);
}
