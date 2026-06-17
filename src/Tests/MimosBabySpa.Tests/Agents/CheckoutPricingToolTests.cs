using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class ResolvePricingToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsCatalogTotalWithoutDepositFields()
    {
        var businessId = Guid.NewGuid();
        var services = new[]
        {
            new Service { BusinessId = businessId, ServiceName = "Plan Marineritos", Price = 100000 },
            new Service { BusinessId = businessId, ServiceName = "Decoracion Sencilla", Price = 35000 }
        };

        var tool = CreateTool(businessId, services, out var addOnCatalog);
        addOnCatalog.Setup(c => c.ValidateAsync(
                businessId,
                "Plan Marineritos",
                "Decoracion Sencilla",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddOnValidationResult.Ok("Decoracion Sencilla"));

        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            Config = new AgentConfig { Checkout = new CheckoutDefinitions { Currency = "COP" } }
        };

        using var args = JsonDocument.Parse("""
            {"service":"Plan Marineritos","add_ons":"Decoracion Sencilla"}
            """);
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"total_cents\":13500000");
        json.Should().Contain("\"currency\":\"COP\"");
        json.Should().NotContain("deposit_required");
        json.Should().NotContain("deposit_cents");
    }

    [Fact]
    public async Task ExecuteAsync_WhenAddOnIsInformational_DoesNotAddItToTotal()
    {
        var businessId = Guid.NewGuid();
        var services = new[]
        {
            new Service { BusinessId = businessId, ServiceName = "Plan Marineritos", Price = 100000 },
            new Service
            {
                BusinessId = businessId,
                ServiceName = "Fotografia",
                Price = 50000,
                IncludeInCheckoutTotal = false
            }
        };

        var tool = CreateTool(businessId, services, out var addOnCatalog);
        addOnCatalog.Setup(c => c.ValidateAsync(
                businessId,
                "Plan Marineritos",
                "Fotografia",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddOnValidationResult.Ok("Fotografia"));

        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            Config = new AgentConfig { Checkout = new CheckoutDefinitions { Currency = "COP" } }
        };

        using var args = JsonDocument.Parse("""
            {"service":"Plan Marineritos","add_ons":"Fotografia"}
            """);
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"total_cents\":10000000");
        json.Should().Contain("\"price\":50000");
        json.Should().Contain("\"include_in_checkout_total\":false");
    }

    [Fact]
    public async Task ExecuteAsync_WhenServiceNotFound_ReturnsError()
    {
        var businessId = Guid.NewGuid();
        var tool = CreateTool(businessId, [], out _);
        var ctx = new AgentToolContext { BusinessId = businessId };

        using var args = JsonDocument.Parse("""{"service":"Unknown"}""");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("service_not_found");
    }

    private static ResolvePricingTool CreateTool(
        Guid businessId,
        IReadOnlyList<Service> services,
        out Mock<IAddOnCatalogService> addOnCatalog)
    {
        var serviceRepo = new Mock<IServiceRepository>();
        serviceRepo.Setup(r => r.GetActiveByBusinessIdAsync(businessId))
            .ReturnsAsync(services);
        serviceRepo.Setup(r => r.GetByBusinessIdAndNameAsync(businessId, It.IsAny<string>()))
            .ReturnsAsync((Guid _, string name) =>
                services.FirstOrDefault(s => s.ServiceName.Equals(name, StringComparison.OrdinalIgnoreCase)));

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(serviceRepo.Object);

        var nameResolver = new ServiceNameResolver(unitOfWork.Object, NullLogger<ServiceNameResolver>.Instance);
        var pricing = new ReservationPricingResolver(unitOfWork.Object, nameResolver, NullLogger<ReservationPricingResolver>.Instance);
        addOnCatalog = new Mock<IAddOnCatalogService>();
        return new ResolvePricingTool(pricing, addOnCatalog.Object);
    }
}
