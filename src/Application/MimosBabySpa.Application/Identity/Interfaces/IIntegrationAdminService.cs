using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IIntegrationAdminService
{
    Task<IntegrationSettingsDto> GetSettingsAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<IntegrationSettingsDto> UpdateGoogleCalendarAsync(
        Guid tenantId,
        Guid businessId,
        UpdateGoogleCalendarIntegrationRequest request,
        CancellationToken ct = default);
    Task<IntegrationSettingsDto> UpdateWompiAsync(
        Guid tenantId,
        Guid businessId,
        UpdateWompiIntegrationRequest request,
        CancellationToken ct = default);
}
