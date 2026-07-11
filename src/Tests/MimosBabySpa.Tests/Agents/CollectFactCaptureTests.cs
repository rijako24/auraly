using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class CollectFactCaptureTests
{
    [Fact]
    public void NumberFacts_AreLeftToTheLlmEvenWithConfiguredUnit()
    {
        var entry = new FactSchemaEntry
        {
            Key = "baby_age_months",
            Type = "number",
            Source = "user",
            Aliases = ["meses"]
        };

        var captured = AgentConversationService.TryExtractDeterministicFactValue(
            entry,
            "en 2 dias voy, la edad de mi bebe son 2 meses",
            new DateOnly(2026, 7, 10),
            out _);

        captured.Should().BeFalse();
    }

    [Theory]
    [InlineData("desired_date", "date", "hoy no puedo, manana si")]
    [InlineData("desired_time", "time", "a las 10")]
    public void TemporalFacts_AreLeftToTheLlm(string key, string type, string message)
    {
        var entry = new FactSchemaEntry
        {
            Key = key,
            Type = type,
            Source = "user"
        };

        var captured = AgentConversationService.TryExtractDeterministicFactValue(
            entry,
            message,
            new DateOnly(2026, 7, 10),
            out _);

        captured.Should().BeFalse();
    }
    [Fact]
    public void BooleanFacts_AreCapturedFromConfiguredAliases()
    {
        var entry = new FactSchemaEntry
        {
            Key = "customer_confirmed",
            Role = "confirmation.verbal",
            Type = "boolean",
            Source = "user",
            Aliases = ["confirmo pedido"]
        };

        var captured = AgentConversationService.TryExtractDeterministicFactValue(
            entry,
            "listo, confirmo pedido con esos datos",
            new DateOnly(2026, 7, 10),
            out var value);

        captured.Should().BeTrue();
        value.Should().Be("true");
    }
    [Theory]
    [InlineData("order_finalized", "order.finalized", "solo eso", "solo eso")]
    [InlineData("cart_review_confirmed", "order.cart_review_confirmed", "correcto", "esta correcto")]
    public void BooleanCheckpoints_AreCapturedFromConfiguredAliases(string key, string role, string alias, string message)
    {
        var entry = new FactSchemaEntry
        {
            Key = key,
            Role = role,
            Type = "boolean",
            Source = "user",
            Aliases = [alias]
        };

        var captured = AgentConversationService.TryExtractDeterministicFactValue(
            entry,
            message,
            new DateOnly(2026, 7, 10),
            out var value);

        captured.Should().BeTrue();
        value.Should().Be("true");
    }

    [Fact]
    public void BooleanFacts_WithoutAliasMatch_AreLeftToTheLlm()
    {
        var entry = new FactSchemaEntry
        {
            Key = "customer_confirmed",
            Role = "confirmation.verbal",
            Type = "boolean",
            Source = "user",
            Aliases = ["confirmo pedido"]
        };

        var captured = AgentConversationService.TryExtractDeterministicFactValue(
            entry,
            "todavia tengo una pregunta",
            new DateOnly(2026, 7, 10),
            out _);

        captured.Should().BeFalse();
    }
}
