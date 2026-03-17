using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IBusinessConfigurationAdminService
{
    Task<BusinessConfigurationDto> GetConfigurationAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<BusinessConfigurationDto> UpdateConfigurationAsync(
        Guid tenantId, Guid businessId, UpdateBusinessConfigurationRequest request, CancellationToken ct = default);
}
