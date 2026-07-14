using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Planning;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CommerceTurnPlanSafetyTests
{
    [Fact]
    public void CatalogInquiry_ReplacesInventedAddWithCatalogQuery()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("pechuga", 1, "tienes pechuga")),
            Context("¿Tienes pechuga?"));

        normalized.Signals.Should().ContainSingle();
        var signal = normalized.Signals[0];
        signal.Type.Should().Be("catalog_query");
        signal.Value.GetProperty("queries")[0].GetString().Should().Be("pechuga");
        signal.Evidence.Should().Be("tienes pechuga");
    }

    [Fact]
    public void CatalogInquiry_WithExplicitMutation_KeepsCartCommand()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("pechuga", 2, "agrega 2 pechugas")),
            Context("¿Tienes pechuga? agrega 2 pechugas"));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
    }

    [Fact]
    public void CatalogSelectionWithoutQuantity_DropsInventedAddOfOne()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("TROZOS DE PECHUGA DE POLLO", 1, "trozos de pechuga")),
            Context("trozos de pechuga", catalogFollowUp: true));

        normalized.Signals.Should().BeEmpty();
    }

    [Fact]
    public void CatalogSelectionWithLeadingQuantity_KeepsCartCommand()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("TROZOS DE PECHUGA DE POLLO", 2, "2 trozos de pechuga")),
            Context("2 trozos de pechuga", catalogFollowUp: true));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
    }

    [Fact]
    public void CatalogSelectionNamedWithPackNumbers_DoesNotTreatPresentationAsRequestedQuantity()
    {
        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(OrderChanges("SALCHICHA RANCHERA SUPER X 525 GR X 7 UND", 1, "ranchera super x 525 gr x 7 und")),
            Context("ranchera super x 525 gr x 7 und", catalogFollowUp: true));

        normalized.Signals.Should().BeEmpty();
    }


    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductListWithTrailingQuantities_PreservesEveryProductAndQuantity(bool catalogFollowUp)
    {
        const string message = "pechuga mac pollo 5, salchicha ranchera super 3, pechuga criolla 1";
        var orderChanges = new PlannedSignal
        {
            Type = "order_changes",
            Value = JsonSerializer.SerializeToElement(new object[]
            {
                new { operation = "add", productText = "pechuga mac pollo", quantity = 5m, destinationReference = (string?)null },
                new { operation = "add", productText = "salchicha ranchera super", quantity = 3m, destinationReference = (string?)null },
                new { operation = "add", productText = "pechuga criolla", quantity = 1m, destinationReference = (string?)null }
            }),
            Evidence = message,
            Confidence = 0.95
        };

        var normalized = CommerceTurnPlanSafety.Normalize(
            Plan(orderChanges),
            Context(message, catalogFollowUp));

        normalized.Signals.Should().ContainSingle(signal => signal.Type == "order_changes");
        var commands = normalized.Signals[0].Value;
        commands.GetArrayLength().Should().Be(3);
        commands.EnumerateArray()
            .Select(command => (
                command.GetProperty("productText").GetString(),
                command.GetProperty("quantity").GetDecimal()))
            .Should().Equal(
                ("pechuga mac pollo", 5m),
                ("salchicha ranchera super", 3m),
                ("pechuga criolla", 1m));
    }
    private static TurnPlan Plan(params PlannedSignal[] signals) => new()
    {
        Signals = signals,
        Response = new TurnPlanResponseDirective()
    };

    private static PlannedSignal OrderChanges(string productText, decimal quantity, string evidence) => new()
    {
        Type = "order_changes",
        Value = JsonSerializer.SerializeToElement(new[]
        {
            new
            {
                operation = "add",
                productText,
                quantity,
                destinationReference = (string?)null
            }
        }),
        Evidence = evidence,
        Confidence = 0.95
    };

    private static TurnPlanningContext Context(string message, bool catalogFollowUp = false)
    {
        var signals = new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_changes"] = new() { Type = "order_changes" },
            ["catalog_query"] = new() { Type = "catalog_query" }
        };
        IReadOnlyDictionary<string, JsonElement>? structuredContext = catalogFollowUp
            ? new Dictionary<string, JsonElement>
            {
                ["shoppingContext"] = JsonSerializer.SerializeToElement(new
                {
                    interaction = new { expected_reply = "catalog_follow_up" }
                })
            }
            : null;

        return new TurnPlanningContext(
            new AgentConfig(),
            new AgentFlowStage(),
            new TurnPlanScope(
                new Dictionary<string, FactSchemaEntry>(),
                signals),
            new Dictionary<string, string>(),
            message,
            DateTimeOffset.Parse("2026-07-14T10:00:00-05:00"),
            [],
            structuredContext);
    }
}
