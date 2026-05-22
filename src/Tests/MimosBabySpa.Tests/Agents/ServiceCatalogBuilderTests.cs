using FluentAssertions;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

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
                AddOnName = "Decoración Sencilla",
                AddOnDescription = "Globos temáticos",
                AddOnPrice = 35000,
                CompatibleWithServiceName = "Plan Marineritos"
            },
            new()
            {
                AddOnName = "Decoración Bouquet Personalizado",
                AddOnDescription = "Flores personalizadas",
                AddOnPrice = 120000,
                CompatibleWithServiceName = "Otro Plan"
            }
        };

        var catalog = ServiceCatalogBuilder.Build(services, addOnRules, []);

        catalog.Should().Contain("Plan Marineritos");
        catalog.Should().Contain("Complementos compatibles:");
        catalog.Should().Contain("Decoración Sencilla");
        catalog.Should().NotContain("Decoración Bouquet Personalizado");
        catalog.Should().NotContain("### Complementos");
    }
}
