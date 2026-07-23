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
    Task<IntegrationSettingsDto> UpdateOperationalModeAsync(
        Guid tenantId,
        Guid businessId,
        UpdateOperationalModeRequest request,
        CancellationToken ct = default);
    Task<IntegrationSettingsDto> UpdateSiigoCommerceAsync(
        Guid tenantId,
        Guid businessId,
        UpdateSiigoCommerceIntegrationRequest request,
        CancellationToken ct = default);
    Task<IntegrationSettingsDto> UpdateMantisAsync(
        Guid tenantId,
        Guid businessId,
        UpdateMantisIntegrationRequest request,
        CancellationToken ct = default);
    Task<IntegrationSettingsDto> UpdateXionAsync(
        Guid tenantId,
        Guid businessId,
        UpdateXionIntegrationRequest request,
        CancellationToken ct = default);
    Task<IReadOnlyList<MantisChannelWarehouseDto>> GetMantisChannelWarehousesAsync(
        Guid tenantId,
        Guid businessId,
        CancellationToken ct = default);
    Task<IReadOnlyList<MantisChannelWarehouseDto>> UpdateMantisChannelWarehousesAsync(
        Guid tenantId,
        Guid businessId,
        UpdateMantisChannelWarehousesRequest request,
        CancellationToken ct = default);
}
