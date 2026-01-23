using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ResourceConfigurationService : IResourceConfigurationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ResourceConfigurationService> _logger;

    public ResourceConfigurationService(
        IUnitOfWork unitOfWork,
        ILogger<ResourceConfigurationService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ResourceModel> GetResourceModelAsync(Guid businessId)
    {
        _logger.LogDebug("Obteniendo modelo de recursos desde BD para negocio {BusinessId}", businessId);

        // 1. Obtener recursos disponibles del negocio
        var businessResources = await _unitOfWork.BusinessResources.GetByBusinessIdAsync(businessId);
        var availableResources = businessResources.ToDictionary(
            r => r.ResourceName,
            r => r.Quantity);

        // 2. Obtener servicios activos del negocio
        var services = await _unitOfWork.Services.GetActiveByBusinessIdAsync(businessId);
        
        // 3. Construir uso de recursos por servicio
        var serviceResourceUsage = new Dictionary<string, ResourceUsage>();
        foreach (var service in services)
        {
            var usage = new ResourceUsage
            {
                Resources = service.ResourceUsages.ToDictionary(
                    ru => ru.BusinessResource.ResourceName,
                    ru => ru.Quantity)
            };
            serviceResourceUsage[service.ServiceName] = usage;
        }

        // 4. Obtener reglas de coexistencia
        var coexistenceRules = await _unitOfWork.ServiceCoexistenceRules.GetByBusinessIdAsync(businessId);
        var rules = coexistenceRules
            .Where(r => r.CanCoexist) // Solo reglas que permiten coexistencia
            .Select(r => new CoexistenceRule
            {
                Services = new List<string> { r.Service1.ServiceName, r.Service2.ServiceName }
            })
            .ToList();

        var model = new ResourceModel
        {
            AvailableResources = availableResources,
            ServiceResourceUsage = serviceResourceUsage,
            CoexistenceRules = rules
        };

        _logger.LogInformation(
            "Modelo de recursos obtenido: {ResourceCount} recursos, {ServiceCount} servicios, {RuleCount} reglas de coexistencia",
            availableResources.Count, serviceResourceUsage.Count, rules.Count);

        return model;
    }
}
