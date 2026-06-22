using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IPromotionAdminService
{
    Task<PromotionDto> GetByIdAsync(Guid tenantId, Guid businessId, Guid promotionId, CancellationToken ct = default);
    Task<PagedResponse<PromotionDto>> GetPagedByBusinessIdAsync(Guid tenantId, Guid businessId, PagedRequest request, CancellationToken ct = default);
    Task<PromotionDto> CreateAsync(Guid tenantId, CreatePromotionRequest request, CancellationToken ct = default);
    Task<PromotionDto> UpdateAsync(Guid tenantId, Guid businessId, Guid promotionId, UpdatePromotionRequest request, CancellationToken ct = default);
    Task DeactivateAsync(Guid tenantId, Guid businessId, Guid promotionId, CancellationToken ct = default);
}
