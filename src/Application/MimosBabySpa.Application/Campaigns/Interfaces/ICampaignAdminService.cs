using MimosBabySpa.Application.Campaigns.DTOs;
using MimosBabySpa.Application.Common.DTOs;

namespace MimosBabySpa.Application.Campaigns.Interfaces;

public interface ICampaignAdminService
{
    Task<PagedResponse<CampaignDto>> GetPagedByBusinessIdAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid businessId,
        PagedRequest request,
        CancellationToken ct = default);

    Task<CampaignDto> GetByIdAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid campaignId,
        CancellationToken ct = default);

    Task<CampaignDto> CreateAsync(
        Guid tenantId,
        bool canAccessAllTenants,
        Guid userId,
        CreateCampaignRequest request,
        CancellationToken ct = default);
}
