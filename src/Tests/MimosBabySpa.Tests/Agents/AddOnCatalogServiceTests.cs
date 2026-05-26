using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class AddOnCatalogServiceTests
{
    private readonly Guid _businessId = Guid.NewGuid();
    private readonly Guid _planDeluxeId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private readonly Guid _planMarineritosId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _masajeExtraId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private readonly Guid _decoracionId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public async Task GetCompatibleAsync_ReturnsOnlyRulesForSelectedServiceOrGlobal()
    {
        var planMarineritos = CreateService(_planMarineritosId, "Plan Marineritos");
        var deluxe = CreateService(_planDeluxeId, "Plan Deluxe");
        var masajeExtra = CreateAddOn(_masajeExtraId, "Masaje Extra 15m");
        var decoracion = CreateAddOn(_decoracionId, "Decoración Sencilla");

        var rules = new List<ServiceAddOnRule>
        {
            new()
            {
                BusinessId = _businessId,
                CompatibleServiceId = _planDeluxeId,
                AddOnServiceId = _masajeExtraId,
                AddOnService = masajeExtra,
                CompatibleService = deluxe,
                DisplayOrder = 1
            },
            new()
            {
                BusinessId = _businessId,
                CompatibleServiceId = null,
                AddOnServiceId = _decoracionId,
                AddOnService = decoracion,
                DisplayOrder = 2
            }
        };

        var sut = CreateSut(planMarineritos, rules);

        var compatible = await sut.GetCompatibleAsync(_businessId, "Plan Marineritos");

        compatible.Should().ContainSingle(a => a.AddOnName == "Decoración Sencilla");
        compatible.Should().NotContain(a => a.AddOnName == "Masaje Extra 15m");
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnknownAddOnNames()
    {
        var planMarineritos = CreateService(_planMarineritosId, "Plan Marineritos");
        var decoracion = CreateAddOn(_decoracionId, "Decoración Sencilla");
        var rules = new List<ServiceAddOnRule>
        {
            new()
            {
                BusinessId = _businessId,
                AddOnServiceId = _decoracionId,
                AddOnService = decoracion,
                DisplayOrder = 1
            }
        };

        var sut = CreateSut(planMarineritos, rules);

        var result = await sut.ValidateAsync(_businessId, "Plan Marineritos", "Masaje Inventado");

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Masaje Inventado");
    }

    [Fact]
    public async Task ValidateAsync_NormalizesValidAddOnNames()
    {
        var planMarineritos = CreateService(_planMarineritosId, "Plan Marineritos");
        var decoracion = CreateAddOn(_decoracionId, "Decoración Sencilla");
        var rules = new List<ServiceAddOnRule>
        {
            new()
            {
                BusinessId = _businessId,
                AddOnServiceId = _decoracionId,
                AddOnService = decoracion,
                DisplayOrder = 1
            }
        };

        var sut = CreateSut(planMarineritos, rules);

        var result = await sut.ValidateAsync(_businessId, "Plan Marineritos", "decoracion sencilla");

        result.IsValid.Should().BeTrue();
        result.NormalizedCsv.Should().Be("Decoración Sencilla");
    }

    private AddOnCatalogService CreateSut(Service selectedService, List<ServiceAddOnRule> rules)
    {
        var allServices = new List<Service> { selectedService };
        allServices.AddRange(rules.Select(r => r.AddOnService));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Services.GetByBusinessIdAndNameAsync(_businessId, selectedService.ServiceName))
            .ReturnsAsync(selectedService);
        unitOfWork.Setup(u => u.Services.GetActiveByBusinessIdAsync(_businessId))
            .ReturnsAsync(allServices);
        unitOfWork.Setup(u => u.ServiceAddOnRules.GetByBusinessIdAsync(_businessId))
            .ReturnsAsync(rules);

        return new AddOnCatalogService(unitOfWork.Object);
    }

    private Service CreateService(Guid id, string name) => new()
    {
        ServiceId = id,
        BusinessId = _businessId,
        ServiceName = name,
        ServiceType = ServiceType.Standard,
        IsActive = true
    };

    private Service CreateAddOn(Guid id, string name) => new()
    {
        ServiceId = id,
        BusinessId = _businessId,
        ServiceName = name,
        ServiceType = ServiceType.AddOn,
        IsActive = true,
        Price = 35000
    };
}
