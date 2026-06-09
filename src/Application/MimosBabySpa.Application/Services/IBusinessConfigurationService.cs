using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

public interface IBusinessConfigurationService
{
    Task<BusinessConfigurationDto> GetConfigurationAsync(Guid businessId);
    Task<SystemConfigurationDto> GetAllSystemConfigurationsAsync();
    Task<string> GetSystemConfigurationAsync(SystemConfigurationKey key);
}
