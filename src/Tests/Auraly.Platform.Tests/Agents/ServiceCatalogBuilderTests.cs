using FluentAssertions;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Enums;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public class ServiceCatalogBuilderTests
{
    [Fact]
    public void Build_ListsCompatibleAddOnsUnderEachService()
    {
        var serviceId = Guid.NewGuid();
        var services = new List<ServiceInfo>
        {
            new()
            {
                Name = "Plan Marineritos",
                Description = "Experiencia completa",
                DurationMinutes = 60,
                Price = 100000,
                IsActive = true,
                CategoryId = serviceId,
                ServiceType = ServiceType.Standard
            }
        };

        var addOnRules = new List<AddOnRuleInfo>
        {
            new()
            {
                AddOnName = "Decoracion Sencilla",
                AddOnDescription = "Globos tematicos",
                AddOnPrice = 35000,
                CompatibleWithServiceName = "Plan Marineritos"
            },
            new()
            {
                AddOnName = "Decoracion Bouquet Personalizado",
                AddOnDescription = "Flores personalizadas",
                AddOnPrice = 120000,
                CompatibleWithServiceName = "Otro Plan"
            }
        };

        var catalog = ServiceCatalogBuilder.Build(services, addOnRules, [], includeAddOns: true);

        catalog.Should().Contain("Plan Marineritos");
        catalog.Should().Contain("Complementos compatibles:");
        catalog.Should().Contain("Decoracion Sencilla");
        catalog.Should().NotContain("Decoracion Bouquet Personalizado");
        catalog.Should().NotContain("### Complementos");
    }

    [Fact]
    public void Build_WhenServiceHasNoCompatibleAddOns_StatesNoneExplicitly()
    {
        var serviceId = Guid.NewGuid();
        var services = new List<ServiceInfo>
        {
            new()
            {
                Name = "Taller Grupal - 3 dias/semana",
                Description = "Taller por edades",
                DurationMinutes = 60,
                Price = 330000,
                IsActive = true,
                CategoryId = serviceId,
                ServiceType = ServiceType.Standard
            }
        };

        var catalog = ServiceCatalogBuilder.Build(services, [], [], includeAddOns: true);

        catalog.Should().Contain("Taller Grupal - 3 dias/semana");
        catalog.Should().Contain("Complementos compatibles: ninguno");
    }


    [Fact]
    public void Build_WhenNoStandardServices_ReturnsExplicitEmptyCatalogMessage()
    {
        var services = new List<ServiceInfo>
        {
            new()
            {
                Name = "Mascarilla de carbono",
                Description = "Adicional para cortes",
                IsActive = true,
                ServiceType = ServiceType.AddOn
            }
        };

        var catalog = ServiceCatalogBuilder.Build(services, [], []);

        catalog.Should().Contain("## CATALOGO DE SERVICIOS");
        catalog.Should().Contain("No se encontraron servicios principales activos para esta consulta.");
        catalog.Should().NotBe("## CATALOGO DE SERVICIOS");
    }

    [Fact]
    public void BuildCategoryOverview_WhenNoStandardServices_ReturnsExplicitEmptyCategoriesMessage()
    {
        var catalog = ServiceCatalogBuilder.BuildCategoryOverview([], []);

        catalog.Should().Contain("## CATEGORIAS DE SERVICIOS");
        catalog.Should().Contain("No se encontraron categorias con servicios principales activos.");
        catalog.Should().NotBe("## CATEGORIAS DE SERVICIOS");
    }
    [Fact]
    public void BuildCategoryOverview_ListsOnlyStandardServiceCategories()
    {
        var cutCategoryId = Guid.NewGuid();
        var addOnCategoryId = Guid.NewGuid();
        var services = new List<ServiceInfo>
        {
            new()
            {
                Name = "Corte basico de adulto",
                Description = "Corte profesional",
                IsActive = true,
                CategoryId = cutCategoryId,
                ServiceType = ServiceType.Standard
            },
            new()
            {
                Name = "Mascarilla de carbono",
                Description = "Adicional para cortes",
                IsActive = true,
                CategoryId = addOnCategoryId,
                ServiceType = ServiceType.AddOn
            }
        };
        var categories = new List<CategoryInfo>
        {
            new() { CategoryId = cutCategoryId, Name = "Corte de Cabello", Description = "Cortes de cabello", DisplayOrder = 1 },
            new() { CategoryId = addOnCategoryId, Name = "Adicionales para cortes", DisplayOrder = 2 }
        };

        var catalog = ServiceCatalogBuilder.BuildCategoryOverview(services, categories);

        catalog.Should().Contain("## CATEGORIAS DE SERVICIOS");
        catalog.Should().Contain("**Corte de Cabello**");
        catalog.Should().NotContain("Adicionales para cortes");
        catalog.Should().NotContain("Mascarilla de carbono");
    }
}
