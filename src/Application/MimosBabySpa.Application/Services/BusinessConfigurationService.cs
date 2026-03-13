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
}
