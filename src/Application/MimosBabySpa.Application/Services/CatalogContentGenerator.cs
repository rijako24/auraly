using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Prompts.Templates;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación de ICatalogContentGenerator.
///
/// Carga servicios, categorías y reglas de add-on desde IUnitOfWork y delega la
/// construcción del markdown a ServiceCatalogBuilder.
///
/// Diseño: stateless, inyectable. Sin caché deliberado — el contenido es pequeño y
/// la frescura es más importante que el ahorro de una query ligera.
/// </summary>
public class CatalogContentGenerator : ICatalogContentGenerator
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CatalogContentGenerator> _logger;

    public CatalogContentGenerator(IUnitOfWork unitOfWork, ILogger<CatalogContentGenerator> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(Guid businessId, CancellationToken ct = default)
    {
        try
        {
            var services      = await _unitOfWork.Services.GetByBusinessIdAsync(businessId);
            var categories    = await _unitOfWork.ServiceCategories.GetByBusinessIdAsync(businessId);
            var addOnRules    = await _unitOfWork.ServiceAddOnRules.GetByBusinessIdAsync(businessId);

            var serviceInfos = services
                .Where(s => s.IsActive)
                .Select(s => new ServiceInfo
                {
                    Name                 = s.ServiceName,
                    Description          = s.Description,
                    DurationMinutes      = s.DurationMinutes,
                    Price                = s.Price,
                    IsActive             = s.IsActive,
                    CategoryId           = s.CategoryId,
                    CategoryName         = s.ServiceCategory?.Name ?? string.Empty,
                    CategoryDisplayOrder = s.ServiceCategory?.DisplayOrder ?? 0,
                    Tier                 = s.Tier,
                    ServiceType          = s.ServiceType,
                    BundleItems          = s.BundleItems
                        .OrderBy(b => b.DisplayOrder)
                        .Select(b => new BundleItemInfo
                        {
                            Name         = b.IncludedService.ServiceName,
                            Description  = b.IncludedService.Description,
                            Price        = b.IncludedService.Price,
                            DisplayOrder = b.DisplayOrder
                        })
                        .ToList()
                })
                .ToList();

            var categoryInfos = categories
                .Select(sc => new CategoryInfo
                {
                    CategoryId   = sc.ServiceCategoryId,
                    Name         = sc.Name,
                    Description  = sc.Description,
                    DisplayOrder = sc.DisplayOrder
                })
                .ToList();

            var addOnRuleInfos = addOnRules
                .Select(r => new AddOnRuleInfo
                {
                    AddOnName                 = r.AddOnService.ServiceName,
                    AddOnDescription          = r.AddOnService.Description,
                    AddOnPrice                = r.AddOnService.Price,
                    DisplayOrder              = r.DisplayOrder,
                    CompatibleWithServiceName = r.CompatibleService?.ServiceName,
                    CompatibleCategoryId      = r.CompatibleService?.CategoryId,
                    CompatibleCategoryName    = r.CompatibleService?.ServiceCategory?.Name
                })
                .OrderBy(r => r.DisplayOrder)
                .ThenBy(r => r.AddOnName)
                .ToList();

            return ServiceCatalogBuilder.Build(serviceInfos, addOnRuleInfos, categoryInfos);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generando catálogo dinámico para BusinessId={BusinessId}", businessId);
            return string.Empty;
        }
    }
}
