using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
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
    private readonly Mock<ILeadService> _leadService = new();
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

        _tool = new SetFactTool(_facts.Object, _addOnCatalog.Object, _verifications, _leadService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceKey_PersistsWithoutAddOnPayload()
    {
        var businessId = Guid.NewGuid();
        var ctx = CreateContext(businessId);

        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").TryGetProperty("compatible_add_ons", out _).Should().BeFalse();
        ctx.Facts[ConversationFactKeys.Service].Should().Be("Plan Marineritos");
        _addOnCatalog.Verify(
            c => c.GetCompatibleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
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

    [Fact]
    public async Task ExecuteAsync_UnknownKey_WhenSchemaDefined_ReturnsUnknownFactKeyError()
    {
        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "desired_time", Label = "hora", Type = "time", Source = "user" },
                new FactSchemaEntry { Key = "service", Label = "servicio", Source = "user" }
            ]
        };

        using var args = JsonDocument.Parse("""{"key":"hour_desired","value":"09:00"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("unknown_fact_key");
        json.Should().Contain("desired_time");
        ctx.Facts.Should().NotContainKey("hour_desired");
    }

    [Fact]
    public async Task ExecuteAsync_DesiredTimeChange_RevokesAvailabilityVerification()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-27";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "08:00";

        var oldScope = SlotVerificationScope.Build("Plan Marineritos", "2026-05-27", "08:00");
        _verifications.Record(ctx, VerificationFactTypes.AvailabilityChecked, oldScope, null);
        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, oldScope)
            .Should().BeTrue();

        using var args = JsonDocument.Parse("""{"key":"desired_time","value":"09:00"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, oldScope)
            .Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DesiredTimeSameValue_DoesNotRevokeAvailabilityVerification()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";

        var scope = SlotVerificationScope.Build("Plan Marineritos", "2026-05-27", "09:00");
        _verifications.Record(ctx, VerificationFactTypes.AvailabilityChecked, scope, null);

        using var args = JsonDocument.Parse("""{"key":"desired_time","value":"09:00"}""");
        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, scope)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AddOnsChange_DoesNotRevokeAvailabilityVerification()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";

        var scope = SlotVerificationScope.Build("Plan Marineritos", "2026-05-27", "08:00");
        _verifications.Record(ctx, VerificationFactTypes.AvailabilityChecked, scope, null);

        using var args = JsonDocument.Parse("""{"key":"add_ons","value":"Decoración Sencilla"}""");
        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, scope)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AliasKey_WhenSchemaDefined_ResolvesToCanonicalKey()
    {
        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry
                {
                    Key = "desired_time",
                    Label = "hora",
                    Type = "time",
                    Source = "user",
                    Aliases = ["hora"]
                }
            ]
        };

        using var args = JsonDocument.Parse("""{"key":"hora","value":"09:00"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Facts["desired_time"].Should().Be("09:00");
    }

    private static AgentConfig CreateMimiLikeSchema() => new()
    {
        FactSchema =
        [
            new FactSchemaEntry { Key = "service", Role = "booking.service", Source = "user" },
            new FactSchemaEntry { Key = "desired_date", Role = "booking.date", Type = "date", Source = "user" },
            new FactSchemaEntry { Key = "desired_time", Role = "booking.time", Type = "time", Source = "user" },
            new FactSchemaEntry { Key = "add_ons", Role = "booking.addons", Source = "user" }
        ]
    };

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
