using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using Moq;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class GetServiceCatalogToolTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsCatalogWithTemplateWithoutParameters()
    {
        var businessId = Guid.NewGuid();
        var unitOfWork = CreateUnitOfWork(businessId,
        [
            new Service
            {
                BusinessId = businessId,
                ServiceName = "Plan Marineritos",
                Description = "Plan acuático",
                DurationMinutes = 45,
                Price = 120000,
                IsActive = true,
                ServiceType = ServiceType.Standard
            }
        ]);
        var addOns = new FakeAddOnCatalogService([]);
        var tool = new GetServiceCatalogTool(unitOfWork.Object, addOns);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationState = new ConversationStateModel()
        };

        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"template_id\":\"service_catalog_summary\"");
        json.Should().Contain("Plan Marineritos");
        json.Should().Contain("120000");
        addOns.WasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithServiceParameter_ReturnsCompatibleAddOnsWithTemplate()
    {
        var businessId = Guid.NewGuid();
        var unitOfWork = CreateUnitOfWork(businessId, []);
        var addOns = new FakeAddOnCatalogService(
        [
            new AddOnRuleInfo
            {
                AddOnName = "Decoración Sencilla",
                AddOnDescription = "Globos temáticos",
                AddOnPrice = 35000
            },
            new AddOnRuleInfo
            {
                AddOnName = "Decoración Bouquet Personalizado",
                AddOnDescription = "Bouquet floral",
                AddOnPrice = 55000
            }
        ]);
        var tool = new GetServiceCatalogTool(unitOfWork.Object, addOns);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationState = new ConversationStateModel()
        };

        using var args = JsonDocument.Parse("""{"service":"Plan Marineritos"}""");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"template_id\":\"addons_compatible_list\"");
        json.Should().Contain("Plan Marineritos");
        json.Should().Contain("Sencilla");
        json.Should().Contain("35000");
        addOns.WasCalled.Should().BeTrue();
        addOns.LastServiceName.Should().Be("Plan Marineritos");
    }

    [Fact]
    public async Task ExecuteAsync_WithUnknownService_ReturnsEmptyAddOnsListWithTemplate()
    {
        var businessId = Guid.NewGuid();
        var unitOfWork = CreateUnitOfWork(businessId, []);
        var addOns = new FakeAddOnCatalogService([]);
        var tool = new GetServiceCatalogTool(unitOfWork.Object, addOns);
        var ctx = new AgentToolContext
        {
            BusinessId = businessId,
            ConversationState = new ConversationStateModel()
        };

        using var args = JsonDocument.Parse("""{"service":"Plan Inexistente"}""");
        var json = await tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"template_id\":\"addons_compatible_list\"");
        json.Should().Contain("\"add_ons\":[]");
        addOns.WasCalled.Should().BeTrue();
    }

    private static Mock<IUnitOfWork> CreateUnitOfWork(Guid businessId, IEnumerable<Service> services)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        var serviceRepo = new Mock<IServiceRepository>();
        serviceRepo
            .Setup(r => r.GetByBusinessIdAsync(businessId))
            .ReturnsAsync(services.ToList());
        unitOfWork.Setup(u => u.Services).Returns(serviceRepo.Object);
        return unitOfWork;
    }

    private sealed class FakeAddOnCatalogService(IReadOnlyList<AddOnRuleInfo> addOns) : IAddOnCatalogService
    {
        public bool WasCalled { get; private set; }
        public string? LastServiceName { get; private set; }

        public Task<IReadOnlyList<AddOnRuleInfo>> GetCompatibleAsync(
            Guid businessId,
            string serviceName,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastServiceName = serviceName;
            return Task.FromResult(addOns);
        }

        public Task<AddOnValidationResult> ValidateAsync(
            Guid businessId,
            string serviceName,
            string? addOnsCsv,
            CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
