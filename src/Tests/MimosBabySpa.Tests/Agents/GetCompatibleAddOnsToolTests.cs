using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class GetCompatibleAddOnsToolTests
{
    [Fact]
    public async Task ExecuteAsync_WhenServiceHasNoCompatibleAddOns_ReturnsZeroCount()
    {
        var businessId = Guid.NewGuid();
        var addOnCatalog = new Mock<IAddOnCatalogService>();
        addOnCatalog
            .Setup(c => c.GetCompatibleAsync(
                businessId,
                "Taller Grupal - 3 dias/semana",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new GetCompatibleAddOnsTool(addOnCatalog.Object);
        var ctx = CreateContext(businessId);

        using var args = JsonDocument.Parse("""{"service":"Taller Grupal - 3 dias/semana"}""");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(0);
        doc.RootElement.GetProperty("data").GetProperty("add_ons").GetArrayLength().Should().Be(0);
        ctx.Facts.Should().NotContainKey(ConversationFactKeys.AddOns);
    }

    [Fact]
    public async Task ExecuteAsync_WhenServiceOmitted_UsesServiceFact()
    {
        var businessId = Guid.NewGuid();
        var addOnCatalog = new Mock<IAddOnCatalogService>();
        addOnCatalog
            .Setup(c => c.GetCompatibleAsync(
                businessId,
                "Plan Marineritos",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AddOnRuleInfo
                {
                    AddOnName = "Decoracion Sencilla",
                    AddOnDescription = "Globos tematicos",
                    AddOnPrice = 35000
                }
            ]);

        var tool = new GetCompatibleAddOnsTool(addOnCatalog.Object);
        var ctx = CreateContext(businessId);
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";

        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data");
        data.GetProperty("count").GetInt32().Should().Be(1);
        data.GetProperty("add_ons")[0].GetProperty("name").GetString().Should().Be("Decoracion Sencilla");
        ctx.Facts.Should().NotContainKey(ConversationFactKeys.AddOns);
    }

    [Fact]
    public async Task ExecuteAsync_WithManageableReservation_UsesCatalogWithoutIntentHardcode()
    {
        var businessId = Guid.NewGuid();
        var addOnCatalog = new Mock<IAddOnCatalogService>();
        addOnCatalog
            .Setup(c => c.GetCompatibleAsync(
                businessId,
                "Corte premium de adulto",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var tool = new GetCompatibleAddOnsTool(addOnCatalog.Object);
        var ctx = CreateContext(businessId);
        ctx.LatestUserMessage = "Quiero cambiar el servicio a corte premium de adulto";
        ctx.Facts[ConversationFactKeys.Service] = "Corte premium de adulto";
        ctx.ManageableReservations =
        [
            new Reservation
            {
                Status = ReservationStatus.Confirmed,
                ReservationDateTime = new DateTime(2026, 9, 2, 11, 0, 0),
                Service = new Service { ServiceName = "Corte basico de adulto" }
            }
        ];

        using var args = JsonDocument.Parse("{}");
        var json = await tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").GetProperty("count").GetInt32().Should().Be(0);
        addOnCatalog.Verify(c => c.GetCompatibleAsync(
            businessId, "Corte premium de adulto", It.IsAny<CancellationToken>()), Times.Once);
    }
    private static AgentToolContext CreateContext(Guid businessId) => new()
    {
        BusinessId = businessId,
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}
