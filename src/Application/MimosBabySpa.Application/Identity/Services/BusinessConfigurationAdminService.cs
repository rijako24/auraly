using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class BusinessConfigurationAdminService : IBusinessConfigurationAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<BusinessConfigurationAdminService> _logger;

    public BusinessConfigurationAdminService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<BusinessConfigurationAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<BusinessConfigurationDto> GetConfigurationAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var configurations = await _unitOfWork.BusinessConfigurations
            .GetActiveByBusinessIdAsync(businessId);

        var dto = new BusinessConfigurationDto();
        foreach (var config in configurations)
            dto.Configurations[config.Key] = config.Value;

        return dto;
    }

    public async Task<BusinessConfigurationDto> UpdateConfigurationAsync(
        Guid tenantId, Guid businessId, UpdateBusinessConfigurationRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        foreach (var (key, value) in request.Configurations)
        {
            var config = await _unitOfWork.BusinessConfigurations
                .GetByBusinessIdAndKeyAsync(businessId, key);

            if (config is null)
            {
                config = new BusinessConfiguration
                {
                    BusinessConfigurationId = Guid.NewGuid(),
                    BusinessId = businessId,
                    Key = key,
                    Value = value,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _unitOfWork.BusinessConfigurations.CreateAsync(config);
            }
            else
            {
                config.Value = value;
                config.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.BusinessConfigurations.UpdateAsync(config);
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Update", "BusinessConfiguration", businessId.ToString(), null, request, ct);
        _logger.LogInformation("Business configuration updated for {BusinessId}, keys: {Keys} [CorrelationId: {CorrelationId}]",
            businessId, string.Join(", ", request.Configurations.Keys), _correlationIdProvider.CorrelationId);

        return await GetConfigurationAsync(tenantId, businessId, ct);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
