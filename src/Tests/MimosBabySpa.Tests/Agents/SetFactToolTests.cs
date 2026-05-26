using System.Text.Json;
using FluentAssertions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Identity;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using ConversationStateModel = MimosBabySpa.Domain.Models.ConversationState;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class SetFactToolTests
{
    private readonly Mock<IConversationFactsService> _facts = new();
    private readonly Mock<IAddOnCatalogService> _addOnCatalog = new();
    private readonly Mock<IIdentityAttributeService> _identityAttributes = new();
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

        _identityAttributes
            .Setup(s => s.SyncFromFactAsync(
                It.IsAny<AgentToolContext>(),
                It.IsAny<FactSchemaEntry>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<AgentToolContext, FactSchemaEntry, string, CancellationToken>((ctx, entry, value, _) =>
            {
                if (string.Equals(entry.Role, FactRoles.CustomerName, StringComparison.OrdinalIgnoreCase))
                    ctx.Conversation.CustomerName = value;
                if (string.Equals(entry.Role, FactRoles.CustomerEmail, StringComparison.OrdinalIgnoreCase))
                    ctx.Conversation.CustomerEmail = value;
            })
            .Returns(Task.CompletedTask);

        _tool = new SetFactTool(
            _facts.Object,
            new FactAccessor(),
            _addOnCatalog.Object,
            _verifications,
            _identityAttributes.Object,
            CreateUnitOfWork());
    }

    [Fact]
    public async Task ExecuteAsync_ServiceKey_PersistsWithoutAddOnPayload()
    {
        var businessId = Guid.NewGuid();
        var ctx = CreateContext(businessId);

        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");
        var json = await _tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("data").TryGetProperty("compatible_add_ons", out _).Should().BeFalse();
        ctx.Facts["service"].Should().Be("Plan Marineritos");
        _addOnCatalog.Verify(
            c => c.GetCompatibleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AttributeKey_PersistsToFacts()
    {
        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"Baby Age Months","value":"5"}""");
        var json = await _tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        json.Should().Contain("\"storage\":\"fact\"");
        ctx.Facts["baby_age_months"].Should().Be("5");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, "baby_age_months", "5", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_InvalidDate_returns_invalid_value_shape()
    {
        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"desired_date","value":"mañana"}""");
        var json = await _tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("invalid_value_shape");
        ctx.Facts.Should().NotContainKey("desired_date");
    }

    [Fact]
    public async Task ExecuteAsync_CustomerName_PersistsToConversation()
    {
        var ctx = CreateContext();
        ctx.LastUserMessage = "me llamo Richard";

        using var args = JsonDocument.Parse("""{"key":"customer_name","value":"Richard"}""");
        var json = await _tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Facts["customer_name"].Should().Be("Richard");
        ctx.Conversation.CustomerName.Should().Be("Richard");
    }

    [Fact]
    public async Task ExecuteAsync_ChannelSourcedKey_ReturnsError()
    {
        var ctx = CreateContext();

        using var args = JsonDocument.Parse("""{"key":"customer_phone","value":"3012926660"}""");
        var json = await _tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);

        json.Should().Contain("non_user_source");
        ctx.Facts.Should().NotContainKey("customer_phone");
    }

    [Fact]
    public async Task ExecuteAsync_MissingKey_ReturnsError()
    {
        var ctx = CreateContext();
        using var args = JsonDocument.Parse("""{"value":"5"}""");
        var json = await _tool.ExecuteAsync(AgentTestHelpers.Invoke(args.RootElement, ctx), CancellationToken.None);
        json.Should().Contain("missing_prerequisites");
    }

    private static IUnitOfWork CreateUnitOfWork()
    {
        var plan = new Service
        {
            ServiceId = Guid.NewGuid(),
            BusinessId = Guid.Empty,
            ServiceName = "Plan Marineritos",
            ServiceType = ServiceType.Standard,
            IsActive = true
        };

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.Setup(u => u.Services.GetActiveByBusinessIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync((Guid bid) =>
            {
                plan.BusinessId = bid;
                return new List<Service> { plan };
            });
        unitOfWork.Setup(u => u.Services.GetByBusinessIdAndNameAsync(It.IsAny<Guid>(), "Plan Marineritos"))
            .ReturnsAsync((Guid bid, string _) =>
            {
                plan.BusinessId = bid;
                return plan;
            });
        return unitOfWork.Object;
    }

    private static AgentToolContext CreateContext(Guid? businessId = null) => new()
    {
        BusinessId = businessId ?? Guid.NewGuid(),
        ConversationId = Guid.NewGuid(),
        Config = new AgentConfig { FactSchema = AgentTestHelpers.MimiFactSchema },
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
            ["customer_phone"] = "3012926660"
        };
        ConversationContactPhone.Resolve(facts, "+573001234567", AgentTestHelpers.MimiFactSchema)
            .Should().Be("3012926660");
    }

    [Fact]
    public void Resolve_FallsBackToChannelPhone()
    {
        ConversationContactPhone.Resolve(new Dictionary<string, string>(), "+573001234567", AgentTestHelpers.MimiFactSchema)
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
