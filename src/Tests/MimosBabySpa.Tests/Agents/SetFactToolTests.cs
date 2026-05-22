using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class SetFactToolTests
{
    private readonly Mock<IConversationFactsService> _facts = new();
    private readonly Mock<IAddOnCatalogService> _addOnCatalog = new();
    private readonly ConversationVerificationService _verifications = new();
    private readonly SetFactTool _tool;

    public SetFactToolTests()
    {
        _addOnCatalog
            .Setup(c => c.ValidateAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string? csv, CancellationToken _) =>
                AddOnValidationResult.Ok(csv));

        _addOnCatalog
            .Setup(c => c.GetCompatibleAsync(
                It.IsAny<Guid>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AddOnRuleInfo>());

        _tool = new SetFactTool(_facts.Object, _addOnCatalog.Object, _verifications);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceKey_ReturnsCompatibleAddOnsFromCatalog()
    {
        var businessId = Guid.NewGuid();
        var ctx = CreateContext(businessId);

        _addOnCatalog
            .Setup(c => c.GetCompatibleAsync(businessId, "Plan Marineritos", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AddOnRuleInfo>
            {
                new()
                {
                    AddOnName = "Decoración Sencilla",
                    AddOnDescription = "Globos temáticos",
                    AddOnPrice = 35000
                },
                new()
                {
                    AddOnName = "Decoración Bouquet Personalizado",
                    AddOnPrice = 120000
                }
            });

        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        var addOns = doc.RootElement.GetProperty("data").GetProperty("compatible_add_ons");
        addOns.GetArrayLength().Should().Be(2);
        addOns[0].GetProperty("name").GetString().Should().Be("Decoración Sencilla");
        addOns[0].GetProperty("price").GetDecimal().Should().Be(35000);
        addOns[1].GetProperty("name").GetString().Should().Be("Decoración Bouquet Personalizado");
        ctx.Facts[ConversationFactKeys.Service].Should().Be("Plan Marineritos");
    }

    [Fact]
    public async Task ExecuteAsync_ServiceKey_WithNoAddOns_ReturnsEmptyList()
    {
        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"compatible_add_ons\":[]");
    }

    [Fact]
    public async Task ExecuteAsync_AttributeKey_PersistsToFacts()
    {
        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"Baby Age Months","value":"5"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"storage\":\"fact\"");
        ctx.Facts["baby_age_months"].Should().Be("5");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, "baby_age_months", "5", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_CustomerName_PersistsToConversation()
    {
        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"customer_name","value":"Richard"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Facts[ConversationFactKeys.CustomerName].Should().Be("Richard");
        ctx.Conversation.CustomerName.Should().Be("Richard");
    }

    [Fact]
    public async Task ExecuteAsync_CustomerPhone_PersistsToFacts()
    {
        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"customer_phone","value":"3012926660"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Facts[ConversationFactKeys.CustomerPhone].Should().Be("3012926660");
    }

    [Fact]
    public async Task ExecuteAsync_MissingKey_ReturnsError()
    {
        var ctx = CreateContext();
        using var args = JsonDocument.Parse("""{"value":"5"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);
        json.Should().Contain("missing_prerequisites");
    }

    private static AgentToolContext CreateContext(Guid? businessId = null) => new()
    {
        BusinessId = businessId ?? Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        ConversationState = new ConversationStateModel(),
        Conversation = new Conversation(),
        Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    };
}

public class ConversationContactPhoneTests
{
    [Fact]
    public void Resolve_PrefersFactPhoneOverChannel()
    {
        var facts = new Dictionary<string, string>
        {
            [ConversationFactKeys.CustomerPhone] = "3012926660"
        };
        ConversationContactPhone.Resolve(facts, "+573001234567").Should().Be("3012926660");
    }

    [Fact]
    public void Resolve_FallsBackToChannelPhone()
    {
        ConversationContactPhone.Resolve(new Dictionary<string, string>(), "+573001234567")
            .Should().Be("+573001234567");
    }
}

public class FactKeyNormalizerTests
{
    [Theory]
    [InlineData("baby_age_months", "baby_age_months")]
    [InlineData("BabyAge", "baby_age")]
    [InlineData("party size", "party_size")]
    public void TryNormalizeKey_ProducesSnakeCase(string input, string expected)
    {
        FactKeyNormalizer.TryNormalizeKey(input, out var key).Should().BeTrue();
        key.Should().Be(expected);
    }
}
