using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools.Impl;
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
    private readonly Mock<ILeadService> _leadService = new();
    private readonly Mock<IServiceRepository> _services = new();
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

        _services
            .Setup(s => s.GetActiveByBusinessIdAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<Service>());

        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(u => u.Services).Returns(_services.Object);
        var resolver = new ServiceSelectionResolver(unitOfWork.Object, NullLogger<ServiceSelectionResolver>.Instance);

        _tool = new SetFactTool(
            _facts.Object,
            resolver,
            _addOnCatalog.Object,
            _verifications,
            _leadService.Object);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceKey_ResolvesAndPersistsCanonicalService()
    {
        var ctx = CreateContext();
        _services.Setup(s => s.GetActiveByBusinessIdAsync(ctx.BusinessId))
            .ReturnsAsync([new Service { BusinessId = ctx.BusinessId, ServiceName = "Plan Marineritos", IsActive = true }]);

        using var args = JsonDocument.Parse("""{"key":"service","value":"Plan Marineritos"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"selection_status\":\"resolved\"");
        ctx.Facts[ConversationFactKeys.Service].Should().Be("Plan Marineritos");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, ConversationFactKeys.Service, "Plan Marineritos",
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceKey_ResolvesWithoutInspectingReservationIntent()
    {
        var ctx = CreateContext();
        _services.Setup(s => s.GetActiveByBusinessIdAsync(ctx.BusinessId))
            .ReturnsAsync([new Service { BusinessId = ctx.BusinessId, ServiceName = "Corte premium de adulto", IsActive = true }]);
        ctx.LatestUserMessage = "Quiero cambiar el servicio a corte premium de adulto";
        ctx.ManageableReservations =
        [
            new Reservation
            {
                Status = ReservationStatus.Confirmed,
                ReservationDateTime = new DateTime(2026, 9, 2, 11, 0, 0),
                Service = new Service { ServiceName = "Corte basico de adulto" }
            }
        ];

        using var args = JsonDocument.Parse("""{"key":"service","value":"Corte premium de adulto"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"selection_status\":\"resolved\"");
        ctx.Facts[ConversationFactKeys.Service].Should().Be("Corte premium de adulto");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, ConversationFactKeys.Service, "Corte premium de adulto",
            It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
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
            ctx.ConversationId, ctx.BusinessId, "baby_age_months", "5", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_NumberValue_PersistsAsString()
    {
        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "baby_age_months", Label = "edad del bebe", Type = "number", Source = "user" }
            ]
        };

        using var args = JsonDocument.Parse("""{"key":"baby_age_months","value":5}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Facts["baby_age_months"].Should().Be("5");
        _facts.Verify(f => f.SetAsync(
            ctx.ConversationId, ctx.BusinessId, "baby_age_months", "5", It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task ExecuteAsync_DesiredTimeChange_InvalidatesAvailabilityVerificationLazily()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-27";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "08:00";

        var deps = VerificationSnapshot.Of(ctx.Facts,
            ConversationFactKeys.Service,
            ConversationFactKeys.DesiredDate,
            ConversationFactKeys.DesiredTime);
        _verifications.Record(ctx, VerificationFactTypes.AvailabilityChecked, deps, null);
        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, ctx.Facts)
            .Should().BeTrue();

        using var args = JsonDocument.Parse("""{"key":"desired_time","value":"09:00"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, ctx.Facts)
            .Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_DesiredTimeSameValue_KeepsAvailabilityVerificationActive()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.DesiredTime] = "09:00";

        var deps = VerificationSnapshot.Of(ctx.Facts, ConversationFactKeys.DesiredTime);
        _verifications.Record(ctx, VerificationFactTypes.AvailabilityChecked, deps, null);

        using var args = JsonDocument.Parse("""{"key":"desired_time","value":"09:00"}""");
        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, ctx.Facts)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AddOnsChange_DoesNotInvalidateAvailabilityVerification()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";

        var deps = VerificationSnapshot.Of(ctx.Facts,
            ConversationFactKeys.Service,
            ConversationFactKeys.DesiredDate,
            ConversationFactKeys.DesiredTime);
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-27";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "08:00";
        deps = VerificationSnapshot.Of(ctx.Facts,
            ConversationFactKeys.Service,
            ConversationFactKeys.DesiredDate,
            ConversationFactKeys.DesiredTime);
        _verifications.Record(ctx, VerificationFactTypes.AvailabilityChecked, deps, null);

        using var args = JsonDocument.Parse("""{"key":"add_ons","value":"Decoración Sencilla"}""");
        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, ctx.Facts)
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

    [Fact]
    public async Task ExecuteAsync_EmptyAddOnsValue_ReturnsInvalidValue()
    {
        var ctx = CreateContext();
        ctx.Config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "add_ons", Label = "complementos", Source = "user" },
                new FactSchemaEntry { Key = "service", Source = "user" }
            ]
        };
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";

        using var args = JsonDocument.Parse("""{"key":"add_ons","value":""}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("invalid_value");
        ctx.Facts.Should().NotContainKey(ConversationFactKeys.AddOns);
    }

    [Fact]
    public async Task ExecuteAsync_AddOnsNingunoLiteral_AcceptsWithoutCatalogValidation()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";

        using var args = JsonDocument.Parse("""{"key":"add_ons","value":"ninguno"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"ok\":true");
        ctx.Facts[ConversationFactKeys.AddOns].Should().Be("ninguno");
        _addOnCatalog.Verify(
            c => c.ValidateAsync(ctx.BusinessId, "Plan Marineritos", "ninguno", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void BuildParametersSchema_EmitsEnumWithCanonicalKeys()
    {
        var config = new AgentConfig
        {
            FactSchema =
            [
                new FactSchemaEntry { Key = "desired_date", Label = "fecha", Type = "date", Source = "user" },
                new FactSchemaEntry { Key = "desired_time", Label = "hora", Type = "time", Source = "user" }
            ]
        };

        using var doc = JsonDocument.Parse(_tool.BuildParametersSchema(config));
        var enumValues = doc.RootElement
            .GetProperty("properties")
            .GetProperty("key")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(e => e.GetString())
            .ToList();

        enumValues.Should().Contain("desired_date");
        enumValues.Should().Contain("desired_time");
    }

    [Fact]
    public async Task ExecuteAsync_AddOnsSameValue_ReturnsUnchangedWithoutCatalogValidation()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.AddOns] = "Decoración Sencilla";

        using var args = JsonDocument.Parse("""{"key":"add_ons","value":"Decoración Sencilla"}""");
        var json = await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        json.Should().Contain("\"unchanged\":true");
        _addOnCatalog.Verify(
            c => c.ValidateAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_AddOnsChange_InvalidatesCheckoutPreparedVerificationLazily()
    {
        var ctx = CreateContext();
        ctx.Config = CreateMimiLikeSchema();
        ctx.Facts[ConversationFactKeys.Service] = "Plan Marineritos";
        ctx.Facts[ConversationFactKeys.DesiredDate] = "2026-05-27";
        ctx.Facts[ConversationFactKeys.DesiredTime] = "08:00";
        ctx.Facts[ConversationFactKeys.AddOns] = "Decoración Sencilla";

        var checkoutDeps = VerificationSnapshot.Of(ctx.Facts,
            ConversationFactKeys.Service,
            ConversationFactKeys.DesiredDate,
            ConversationFactKeys.DesiredTime,
            ConversationFactKeys.AddOns);
        _verifications.Record(ctx, VerificationFactTypes.CheckoutPrepared, checkoutDeps, null);
        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.CheckoutPrepared, ctx.Facts)
            .Should().BeTrue();

        using var args = JsonDocument.Parse("""{"key":"add_ons","value":"Decoración Bouquet Personalizado"}""");
        await _tool.ExecuteAsync(args.RootElement, ctx, CancellationToken.None);

        _verifications.IsActive(ctx.ConversationState, VerificationFactTypes.CheckoutPrepared, ctx.Facts)
            .Should().BeFalse();
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
