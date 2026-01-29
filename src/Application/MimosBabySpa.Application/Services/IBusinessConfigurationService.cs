using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Services;

public interface IBusinessConfigurationService
{
    Task<BusinessConfigurationDto> GetConfigurationAsync(Guid businessId);
    Task<string> GetBusinessConfigurationValueAsync(Guid businessId, BusinessConfigurationKey key);
    Task<SystemConfigurationDto> GetAllSystemConfigurationsAsync();
    Task<string> GetSystemConfigurationAsync(SystemConfigurationKey key);
    
    /// <summary>
    /// [OBSOLETO] Usar SystemPromptProvider + LoadedBusinessContext en su lugar.
    /// </summary>
    [Obsolete("Este método es obsoleto. Usar SystemPromptProvider + LoadedBusinessContext para generar prompts dinámicos.", false)]
    Task<string> BuildSystemPromptAsync(Guid businessId);
}
