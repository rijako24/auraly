using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.Time;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public class AgentPromptComposerTests
{
    private static readonly AgentConfig DefaultConfig = new()
    {
        AgentId = Guid.NewGuid(),
        BusinessId = Guid.NewGuid(),
        Name = "Mimo",
        Persona = "## ROL\nEres Mimo.",
        FirstTurnGreetingHint = "¡Hola! Soy Mimo de Mimo's Baby Spa."
    };

    private static readonly TemporalReferenceContext DefaultTemporal = new TemporalReferenceBuilder()
        .Build(CreateSnapshot(new DateOnly(2026, 5, 21), new TimeOnly(9, 30)));

    private static readonly AgentPromptComposer Composer = new(
        new FlowStageDetector(),
        new GuardEvaluator(new ConversationVerificationService(), new ToolPreconditionProvider()));

    private static BusinessClockSnapshot CreateSnapshot(DateOnly today, TimeOnly time)
    {
        var tz = BusinessTimeZoneResolver.Resolve(BusinessClock.DefaultTimeZoneId);
        var local = today.ToDateTime(time);
        return new BusinessClockSnapshot(
            Guid.NewGuid(),
            new DateTimeOffset(local, tz.GetUtcOffset(local)),
            today,
            tz);
    }

    private static string Compose(
        AgentConfig config,
        IEnumerable<Message> history,
        AgentToolContext? session = null,
        BookingPolicyParams? bookingPolicy = null,
        PaymentTransaction? latestPayment = null,
        EngagementContext engagement = EngagementContext.FirstEver) =>
        Composer.Compose(new PromptCompositionInput
        {
            Config = config,
            History = history,
            Temporal = DefaultTemporal,
            Session = session,
            BookingPolicy = bookingPolicy,
            LatestPayment = latestPayment,
            Engagement = engagement
        });

    [Fact]
    public void Compose_WhenHistoryIsEmpty_InjectsFirstTurnInstructions()
    {
        var result = Compose(DefaultConfig, []);

        result.Should().Contain("## CONTEXTO TEMPORAL");
        result.Should().Contain("2026-05-21");
        result.Should().Contain("mañana → 2026-05-22");
        result.Should().Contain("## CONTEXTO DE ESTE TURNO");
        result.Should().Contain("primer mensaje");
        result.Should().Contain(DefaultConfig.FirstTurnGreetingHint);
        result.Should().Contain("Eres Mimo");
    }

    [Fact]
    public void Compose_WhenOnlyUserMessages_InjectsFirstTurnInstructions()
    {
        var history = new[] { new Message { Sender = "user", MessageText = "hola" } };
        var result = Compose(DefaultConfig, history);

        result.Should().Contain("primer mensaje");
        result.Should().Contain("CONTEXTO TEMPORAL");
    }

    [Theory]
    [InlineData("bot")]
    [InlineData("assistant")]
    [InlineData("Bot")]
    public void Compose_WhenBotAlreadyReplied_InjectsNoRepeatGreetingRule(string sender)
    {
        var history = new[]
        {
            new Message { Sender = "user", MessageText = "hola" },
            new Message { Sender = sender, MessageText = "¡Hola! Soy Mimo." }
        };

        var result = Compose(DefaultConfig, history);

        result.Should().Contain("NO repitas saludo completo");
        result.Should().NotContain("Plantilla sugerida");
        result.Should().Contain("CONTEXTO TEMPORAL");
    }

    [Fact]
    public void Compose_WithFacts_RendersSessionState()
    {
        var session = new AgentToolContext
        {
            Conversation = new Conversation { CustomerName = "Ana" },
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["baby_age_months"] = "5",
                [ConversationFactKeys.Service] = "Plan Marineritos",
                [ConversationFactKeys.AddOns] = "Decoración Sencilla"
            },
            ActiveReservation = new Reservation
            {
                Service = new Service { ServiceName = "Plan Marineritos" }
            }
        };

        var result = Compose(DefaultConfig, [], session);

        result.Should().Contain("## ESTADO ACTUAL");
        result.Should().Contain("baby_age_months: 5");
        result.Should().Contain("servicio: Plan Marineritos");
        result.Should().Contain("add_ons: Decoración Sencilla");
        result.Should().Contain("cliente: Ana");
    }

    [Fact]
    public void Compose_WithServiceFact_DoesNotShowAddOnOfferBlock()
    {
        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ConversationFactKeys.Service] = "Plan Marineritos"
            }
        };

        var result = Compose(DefaultConfig, [], session);

        result.Should().Contain("servicio: Plan Marineritos");
        result.Should().NotContain("Complementos disponibles");
    }

    [Fact]
    public void Compose_WithBookingPolicy_ShowsDepositRequired()
    {
        var policy = new BookingPolicyParams
        {
            DepositRequired = true,
            DepositPercentage = 50
        };

        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            policy);

        result.Should().Contain("anticipo: requerido (50%");
    }

    [Fact]
    public void Compose_WithBookingPolicyWithoutDeposit_ShowsExplicitNoDeposit()
    {
        var policy = new BookingPolicyParams { DepositRequired = false };

        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            policy);

        result.Should().Contain("anticipo: no requerido");
        result.Should().NotContain("pago:");
    }

    [Fact]
    public void Compose_WithPendingPayment_ShowsGranularPaymentState()
    {
        var policy = new BookingPolicyParams { DepositRequired = true, DepositPercentage = 50 };
        var payment = new PaymentTransaction
        {
            Status = PaymentTransactionStatus.Created,
            AmountInCents = 6750000,
            Currency = "COP",
            ExpiresAt = DateTime.UtcNow.AddMinutes(45),
            LinkUrl = "https://pay.example/link"
        };

        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            policy,
            payment);

        result.Should().Contain("pago: link generado");
        result.Should().Contain("COP $67,500");
    }

    [Fact]
    public void Compose_WithConfirmedPayment_ShowsConfirmedState()
    {
        var policy = new BookingPolicyParams { DepositRequired = true, DepositPercentage = 50 };
        var payment = new PaymentTransaction
        {
            Status = PaymentTransactionStatus.Confirmed,
            AmountInCents = 6750000,
            Currency = "COP"
        };

        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            policy,
            payment);

        result.Should().Contain("pago: confirmado");
    }

    [Fact]
    public void Compose_WithoutFacts_ShowsEmptyPlaceholders()
    {
        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] });

        result.Should().Contain("## ESTADO ACTUAL");
        result.Should().Contain("cliente: —");
        result.Should().NotContain("baby_age_months");
    }

    [Fact]
    public void Compose_WithoutGreetingHint_UsesAgentName()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            Persona = DefaultConfig.Persona
        };

        var result = Compose(config, []);

        result.Should().Contain("Preséntate como **Mimo**");
    }

    // ── BuildEagerCaptureBlock ────────────────────────────────────────────────

    [Fact]
    public void Compose_WithEagerFactsMissing_EmitsEagerCaptureBlock()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            FactSchema =
            [
                new FactSchemaEntry { Key = "baby_name", Label = "nombre del bebé", CaptureMode = "eager" },
                new FactSchemaEntry { Key = "baby_age_months", Label = "edad del bebé", CaptureMode = "eager" },
                new FactSchemaEntry { Key = "service", Label = "plan", CaptureMode = "onDemand" }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = Compose(config, [], session);

        result.Should().Contain("## CAPTURA INMEDIATA");
        result.Should().Contain("nombre del bebé (baby_name)");
        result.Should().Contain("edad del bebé (baby_age_months)");
        result.Should().NotContain("plan (service)");
    }

    [Fact]
    public void Compose_WithEagerFactsAlreadyPresent_NoEagerCaptureBlock()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            FactSchema =
            [
                new FactSchemaEntry { Key = "baby_name", Label = "nombre del bebé", CaptureMode = "eager" }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["baby_name"] = "Lucía"
            }
        };

        var result = Compose(config, [], session);

        result.Should().NotContain("## CAPTURA INMEDIATA");
    }

    [Fact]
    public void Compose_WithOnlyOnDemandFacts_NoEagerCaptureBlock()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            FactSchema =
            [
                new FactSchemaEntry { Key = "service", Label = "plan", CaptureMode = "onDemand" }
            ]
        };

        var result = Compose(config, []);

        result.Should().NotContain("## CAPTURA INMEDIATA");
    }

    // ── BuildReentryBlock ─────────────────────────────────────────────────────

    [Fact]
    public void Compose_WithNoStageSnapshots_NoReentryBlock()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "checkout",
                        ReentryOnFactChanged = ["service", "desired_date"]
                    }
                ]
            }
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "Plan Marineritos"
            }
        };

        var result = Compose(config, [], session);

        result.Should().NotContain("## ATENCIÓN");
    }

    [Fact]
    public void Compose_WhenFactUnchangedFromSnapshot_NoReentryBlock()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage { Id = "checkout", ReentryOnFactChanged = ["service"] },
                    new AgentFlowStage { Id = "closure", AdvanceWhenFacts = [] }
                ]
            }
        };

        var state = new ConversationState();
        state.StageFactSnapshots["checkout"] = new Dictionary<string, string>
        {
            ["service"] = "Plan Marineritos"
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = state,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "Plan Marineritos"
            }
        };

        var result = Compose(config, [], session);

        result.Should().NotContain("## ATENCIÓN");
    }

    [Fact]
    public void Compose_WhenFactChangedAfterSnapshot_EmitsReentryAlert()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimo",
            FactSchema =
            [
                new FactSchemaEntry { Key = "service", Label = "plan" }
            ],
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage { Id = "checkout", ReentryOnFactChanged = ["service"] },
                    new AgentFlowStage { Id = "closure", AdvanceWhenFacts = [] }
                ]
            }
        };

        var state = new ConversationState();
        state.StageFactSnapshots["checkout"] = new Dictionary<string, string>
        {
            ["service"] = "Plan Pequeñines"
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = state,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "Plan Marineritos"
            }
        };

        var result = Compose(config, [], session);

        result.Should().Contain("## ATENCIÓN: DATOS MODIFICADOS");
        result.Should().Contain("Plan Pequeñines");
        result.Should().Contain("Plan Marineritos");
    }
}
