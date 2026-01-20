using System.Text;
using System.Linq;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

public class BusinessConfigurationService : IBusinessConfigurationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BusinessConfigurationService> _logger;

    public BusinessConfigurationService(
        IUnitOfWork unitOfWork,
        ILogger<BusinessConfigurationService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<BusinessConfigurationDto> GetConfigurationAsync(Guid businessId)
    {
        var configurations = await _unitOfWork.BusinessConfigurations
            .GetActiveByBusinessIdAsync(businessId);

        var dto = new BusinessConfigurationDto();
        
        foreach (var config in configurations)
        {
            dto.Configurations[config.Key] = config.Value;
        }

        return dto;
    }

    public async Task<string> GetBusinessConfigurationValueAsync(Guid businessId, BusinessConfigurationKey key)
    {
        var config = await _unitOfWork.BusinessConfigurations
            .GetByBusinessIdAndKeyAsync(businessId, key);
        return config?.Value ?? string.Empty;
    }

    public async Task<SystemConfigurationDto> GetAllSystemConfigurationsAsync()
    {
        var configurations = await _unitOfWork.SystemConfigurations.GetAllActiveAsync();
        
        var dto = new SystemConfigurationDto();
        
        foreach (var config in configurations)
        {
            var key = (SystemConfigurationKey)config.SystemConfigurationId;
            dto.Configurations[key] = config.Value;
        }
        
        return dto;
    }

    public async Task<string> GetSystemConfigurationAsync(SystemConfigurationKey key)
    {
        var config = await _unitOfWork.SystemConfigurations.GetByKeyAsync(key);
        if (config == null)
        {
            _logger.LogWarning("SystemConfiguration con key {Key} no encontrada en la base de datos.", key);
            return string.Empty;
        }
        return config.Value;
    }

    public async Task<string> BuildSystemPromptAsync(Guid businessId)
    {
        var businessConfig = await GetConfigurationAsync(businessId);
        var toneAndStyle = await GetSystemConfigurationAsync(SystemConfigurationKey.ToneAndStyle);

        var promptBuilder = new StringBuilder();
        
        // FECHA ACTUAL (siempre primero para que la IA sepa qué día es hoy)
        var today = DateTime.UtcNow;
        // Colombia está en UTC-5
        var colombiaOffset = TimeSpan.FromHours(-5);
        var todayColombia = today.Add(colombiaOffset);
        promptBuilder.AppendLine($"FECHA Y HORA ACTUAL: Hoy es {todayColombia:dddd, dd 'de' MMMM 'de' yyyy} (formato: {todayColombia:yyyy-MM-dd}). La hora actual es {todayColombia:HH:mm} (hora de Colombia, UTC-5).");
        promptBuilder.AppendLine();
        
        // TONO Y ESTILO (genérico del sistema)
        if (!string.IsNullOrEmpty(toneAndStyle))
        {
            promptBuilder.AppendLine(toneAndStyle);
            promptBuilder.AppendLine();
        }

        if (businessConfig.HasKey(BusinessConfigurationKey.BusinessInformation))
        {
            promptBuilder.AppendLine(businessConfig.GetValue(BusinessConfigurationKey.BusinessInformation));
        }

        return promptBuilder.ToString();
    }
}
