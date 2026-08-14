using System.Text.Json;
using FluentAssertions;
using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Facts;
using Auraly.Platform.Application.Agents.Planning;
using Xunit;

namespace Auraly.Platform.Tests.Agents;

public sealed class FactMutationBatchProcessorTests
{
    private readonly FactMutationBatchProcessor _processor = new();

    [Fact]
    public void Apply_CommitsAllClaimsAndVersionsAsOneCalculatedBatch()
    {
        var schema = Schema(
            Fact("desired_date"),
            Fact("desired_time"));

        var result = _processor.Apply(
            schema,
            [Claim("desired_date", "2026-07-11"), Claim("desired_time", "10:00")],
            new Dictionary<string, string>());

        result.NextFacts.Should().Contain(new Dictionary<string, string>
        {
            ["desired_date"] = "2026-07-11",
            ["desired_time"] = "10:00"
        });
        result.ChangedFacts.Should().BeEquivalentTo("desired_date", "desired_time");
        result.Versions["desired_date"].Should().Be(1);
        result.Versions["desired_time"].Should().Be(1);
    }

    [Fact]
    public void Apply_InvalidatesDependentFactsTransitively()
    {
        var schema = Schema(
            Fact("service"),
            Fact("availability", dependsOn: ["service"]),
            Fact("checkout", dependsOn: ["availability"]));
        var current = new Dictionary<string, string>
        {
            ["service"] = "corte",
            ["availability"] = "verified",
            ["checkout"] = "ready"
        };

        var result = _processor.Apply(schema, [Claim("service", "barba")], current);

        result.NextFacts.Should().Contain("service", "barba");
        result.NextFacts.Should().NotContainKey("availability");
        result.NextFacts.Should().NotContainKey("checkout");
        result.InvalidatedFacts.Should().BeEquivalentTo("availability", "checkout");
    }

    [Fact]
    public void Apply_DoesNotMutateInput_WhenBatchIsInvalid()
    {
        var schema = Schema(Fact("desired_date"));
        var current = new Dictionary<string, string> { ["desired_date"] = "2026-07-10" };

        var act = () => _processor.Apply(
            schema,
            [Claim("desired_date", "2026-07-11"), Claim("desired_date", "2026-07-12")],
            current);

        act.Should().Throw<InvalidOperationException>();
        current["desired_date"].Should().Be("2026-07-10");
    }

    private static IReadOnlyDictionary<string, FactSchemaEntry> Schema(params FactSchemaEntry[] facts) =>
        facts.ToDictionary(fact => fact.Key, StringComparer.OrdinalIgnoreCase);

    private static FactSchemaEntry Fact(string key, IReadOnlyList<string>? dependsOn = null) => new()
    {
        Key = key,
        Source = "user",
        Scope = FactScopes.Request,
        DependsOn = dependsOn ?? []
    };

    private static PlannedFactClaim Claim(string key, object value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return new PlannedFactClaim
        {
            Key = key,
            Operation = TurnPlanOperations.Set,
            Value = document.RootElement.Clone(),
            Evidence = key
        };
    }
}
