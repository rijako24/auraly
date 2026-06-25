using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
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
        Name = "Mimi",
        Persona = "## ROL\nEres Mimi."
    };

    private static readonly TemporalReferenceContext DefaultTemporal = new TemporalReferenceBuilder()
        .Build(CreateSnapshot(new DateOnly(2026, 5, 21), new TimeOnly(9, 30)));

    private static readonly AgentPromptComposer Composer = new(
        new FlowStageDetector(),
        new GuardEvaluator(new ConversationVerificationService()));

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
        PaymentTransaction? latestPayment = null) =>
        Composer.Compose(new PromptCompositionInput
        {
            Config = config,
            History = history,
            Temporal = DefaultTemporal,
            Session = session,
            LatestPayment = latestPayment
        });

    [Fact]
    public void Compose_AlwaysIncludesTemporalBlock()
    {
        var result = Compose(DefaultConfig, []);

        result.Should().Contain("## CONTEXTO TEMPORAL");
        result.Should().Contain("2026-05-21");
        result.Should().Contain("mañana → 2026-05-22");
        result.Should().Contain("la autoridad temporal de este turno es este bloque");
        result.Should().Contain("Eres Mimi");
    }

    [Fact]
    public void Compose_WithUserOnlyHistory_IncludesTemporalBlock()
    {
        var history = new[] { new Message { Sender = "user", MessageText = "hola" } };
        var result = Compose(DefaultConfig, history);

        result.Should().Contain("CONTEXTO TEMPORAL");
    }

    [Fact]
    public void Compose_WhenNoBotHistory_DoesNotPushGreetingOrQuestion()
    {
        var result = Compose(DefaultConfig, []);

        result.Should().Contain("## POLITICA DEL TURNO");
        result.Should().Contain("primera respuesta visible");
        result.Should().NotContain("saludo breve");
        result.Should().NotContain("pregunta abierta");
    }

    [Fact]
    public void Compose_WithBotHistory_IncludesTemporalBlock()
    {
        var history = new[]
        {
            new Message { Sender = "user",      MessageText = "hola" },
            new Message { Sender = "assistant", MessageText = "¡Hola! Soy Mimi." }
        };

        var result = Compose(DefaultConfig, history);

        result.Should().Contain("CONTEXTO TEMPORAL");
        result.Should().Contain("Eres Mimi");
    }

    [Fact]
    public void Compose_WithFacts_RendersSessionState()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            FactSchema =
            [
                new FactSchemaEntry { Key = "baby_age_months", Label = "edad del bebé (meses)", Type = "number", Source = "user" },
                new FactSchemaEntry { Key = "service", Label = "plan / servicio", Source = "user" },
                new FactSchemaEntry { Key = "add_ons", Label = "complementos", Source = "user" }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation { CustomerName = "Ana" },
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["baby_age_months"] = "5",
                ["service"]         = "Plan Marineritos",
                ["add_ons"]         = "Decoración Sencilla"
            },
            ManageableReservations =
            [
                new Reservation
                {
                    Service = new Service { ServiceName = "Plan Marineritos" }
                }
            ]
        };

        var result = Compose(config, [], session);

        result.Should().Contain("## ESTADO ACTUAL");
        result.Should().Contain("edad del bebé (meses): 5");
        result.Should().Contain("plan / servicio: Plan Marineritos");
        result.Should().Contain("complementos: Decoración Sencilla");
        result.Should().NotContain("## RESERVAS GESTIONABLES");
        result.Should().NotContain("hay_reservas_gestionables");
        result.Should().NotContain("get_customer_reservations");
        result.Should().NotContain("si el cliente pide cambiar servicio");
    }

    [Fact]
    public void Compose_SessionFactsWithNoSchema_RenderAsRawKeys()
    {
        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "Plan Marineritos"
            }
        };

        var result = Compose(DefaultConfig, [], session);

        result.Should().Contain("service: Plan Marineritos");
    }

    [Fact]
    public void Compose_WithPendingPayment_ShowsCompactPaymentStateOnly()
    {
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
            payment);

        result.Should().Contain("pago: link generado");
        result.Should().Contain("COP $67,500");
        result.Should().NotContain("## CHECKOUT PENDIENTE");
        result.Should().NotContain("hay_link_pendiente: true");
        result.Should().NotContain("link_actual: https://pay.example/link");
        result.Should().NotContain("si el cliente cambia servicio");
        result.Should().NotContain("prepare_checkout");
    }

    [Fact]
    public void Compose_WithConfirmedPayment_ShowsConfirmedState()
    {
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
            payment);

        result.Should().Contain("pago: confirmado");
    }

    [Fact]
    public void Compose_WithConfirmedPaymentRequiringReschedule_DoesNotRenderOperationalPaymentBlock()
    {
        var paymentId = Guid.NewGuid();
        var payment = new PaymentTransaction
        {
            PaymentTransactionId = paymentId,
            Status = PaymentTransactionStatus.Confirmed,
            RequiresRescheduling = true,
            AmountInCents = 6750000,
            Currency = "COP"
        };

        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            payment);

        result.Should().Contain("pago: confirmado");
        result.Should().NotContain("## ESTADO PAGO");
        result.Should().NotContain("pago_confirmado_sin_slot: true");
        result.Should().NotContain($"payment_transaction_id: {paymentId}");
        result.Should().NotContain("assign_paid_slot");
    }

    [Fact]
    public void Compose_WithEmptyFacts_NoStateActualBlock()
    {
        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] });

        // Sin facts y sin schema, no hay bloque ESTADO ACTUAL
        result.Should().NotContain("## ESTADO ACTUAL");
    }


    [Fact]
    public void Compose_GreetingStage_FirstEver_RendersVariantHint()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            Flow = new AgentFlowDefinition
            {
                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "greeting",
                        Goal = "Saludar al cliente",
                        Variants = new Dictionary<string, AgentFlowStageVariant>
                        {
                            ["firstEver"] = new() { Hint = "¡Hola! Soy Mimi de Mimo's Baby Spa." }
                        }
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
                ["session.engagement"] = "firstEver"
            }
        };

        var result = Compose(config, [], session);

        result.Should().Contain("## ETAPA ACTUAL");
        result.Should().Contain("greeting");
        result.Should().Contain("¡Hola! Soy Mimi de Mimo's Baby Spa.");
    }

    [Fact]
    public void Compose_GreetingStage_ContinuingSession_Skipped()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            Flow = new AgentFlowDefinition
            {
                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "greeting",
                        Goal = "Saludar al cliente",
                        Variants = new Dictionary<string, AgentFlowStageVariant>
                        {
                            ["firstEver"]         = new() { Hint = "¡Hola! Soy Mimi." },
                            ["returningCustomer"] = new() { Hint = "¡Bienvenido de vuelta!" }
                        }
                    },
                    new AgentFlowStage
                    {
                        Id = "discovery",
                        Goal = "Conocer el plan",
                        AdvanceWhenFacts = ["service"]
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
                ["session.engagement"] = "continuingSession"
            }
        };

        var result = Compose(config, [], session);

        result.Should().Contain("discovery");
        result.Should().NotContain("greeting");
        result.Should().NotContain("¡Hola! Soy Mimi.");
    }

    [Fact]
    public void Compose_MimiStyleFlow_DiscoveryFirstStage_PersonaHasGreetingRules()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = "## ROL\nEres Mimi.\n\n## CÓMO ABRES LA CONVERSACIÓN\n- En tu primer mensaje: saludo.\n- Si conoces el nombre del cliente, salúdalo por nombre.",
            FactSchema =
            [
                new FactSchemaEntry { Key = "baby_name", Source = "user" },
                new FactSchemaEntry { Key = "baby_age_months", Source = "user" },
                new FactSchemaEntry { Key = "service", Source = "user" }
            ],
            Flow = new AgentFlowDefinition
            {
                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "discovery",
                        Goal = "Conocer al bebé y elegir servicio",
                        AllowedTools = ["get_service_catalog", "set_fact"],
                        AdvanceWhenFacts = ["baby_name", "baby_age_months", "service"]
                    }
                ]
            }
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = Compose(config, [], session);

        result.Should().Contain("CÓMO ABRES LA CONVERSACIÓN");
        result.Should().Contain("etapa: discovery");
        result.Should().NotContain("etapa: greeting");
        result.Should().Contain("criterio_de_avance: la etapa se completa cuando estén presentes estos datos del flujo");
        result.Should().Contain("datos_para_completar_etapa: baby_name (baby_name), baby_age_months (baby_age_months), service (service)");
        result.Should().Contain("usa estos datos como próximos datos útiles solo cuando la intención actual los requiera");
        result.Should().NotContain("si el cliente solo saluda");
        result.Should().NotContain("facts_pendientes");
        result.Should().NotContain("el sistema te llevará automáticamente al siguiente paso");
    }

    [Fact]
    public void Compose_StageWithAdvanceWhenFacts_IncludesOrchestrationContract()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            FactSchema =
            [
                new FactSchemaEntry { Key = "service", Source = "user" }
            ],
            Flow = new AgentFlowDefinition
            {
                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "discovery",
                        Goal = "Elegir plan",
                        AdvanceWhenFacts = ["service"]
                    }
                ]
            }
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = Compose(config, [], session);

        result.Should().Contain("criterio_de_avance: la etapa se completa cuando estén presentes estos datos del flujo");
        result.Should().Contain("datos_para_completar_etapa: service (service)");
        result.Should().NotContain("facts_pendientes");
        result.Should().NotContain("el sistema te llevará automáticamente al siguiente paso");
    }

    [Fact]
    public void Compose_FinalizationStage_OmitsOrchestrationContract()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            Flow = new AgentFlowDefinition
            {
                StageDetection = "automatic",
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "discovery",
                        Goal = "Elegir plan",
                        AdvanceWhenFacts = ["service"]
                    },
                    new AgentFlowStage
                    {
                        Id = "finalization",
                        Goal = "Cerrar reserva",
                        AdvanceWhenFacts = []
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
                ["service"] = "Plan Marineritos",
                ["baby_name"] = "Thomas",
                ["baby_age_months"] = "5",
                ["add_ons"] = "ninguno",
                ["desired_date"] = "2026-08-15",
                ["desired_time"] = "10:00",
                ["customer_name"] = "Ana",
                ["baby_birth_date"] = "2026-03-15"
            }
        };

        var result = Compose(config, [], session);

        result.Should().Contain("etapa: finalization");
        result.Should().NotContain("el sistema te llevará automáticamente al siguiente paso");
    }

    // ── BuildEagerCaptureBlock ────────────────────────────────────────────────

    [Fact]
    public void Compose_WithGlobalActions_RendersTransversalActionsBlock()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "manage_existing_reservation",
                    Priority = 900,
                    Goal = "Gestionar reservas existentes.",
                    Hint = "Usa get_customer_reservations antes de modificar.",
                    AllowedTools = ["get_customer_reservations", "confirm_reservation_change"]
                }
            ]
        };

        var result = Compose(config, [], new AgentToolContext
        {
            Conversation = new Conversation(),
            Facts = []
        });

        result.Should().Contain("## ACCIONES TRANSVERSALES");
        result.Should().Contain("manage_existing_reservation");
        result.Should().Contain("get_customer_reservations, confirm_reservation_change");
    }
    [Fact]
    public void Compose_WithHumanEscalationGlobalAction_RendersViaGlobalActions()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "human_escalation",
                    Priority = 1000,
                    Goal = "Notificar al equipo humano sin desactivar el bot.",
                    Hint = "Escala cuando el cliente pida hablar con una persona o cuando el caso sea sensible.",
                    AllowedTools = ["escalate_to_human"]
                }
            ]
        };

        var result = Compose(config, []);

        result.Should().Contain("## ACCIONES TRANSVERSALES");
        result.Should().Contain("human_escalation");
        result.Should().Contain("Escala cuando el cliente pida hablar con una persona");
        result.Should().Contain("escalate_to_human");
        result.Should().NotContain("## ESCALACION A HUMANO");
    }

    [Fact]
    public void Compose_WithEagerFactsMissing_EmitsEagerCaptureBlock()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            FactSchema =
            [
                new FactSchemaEntry { Key = "baby_name", Label = "nombre del bebé", CaptureMode = "eager", Source = "user" },
                new FactSchemaEntry { Key = "baby_age_months", Label = "edad del bebé", CaptureMode = "eager", Source = "user" },
                new FactSchemaEntry { Key = "service", Label = "plan", CaptureMode = "onDemand", Source = "user" }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

        var result = Compose(config, [], session);

        result.Should().Contain("## CAPTURA INMEDIATA");
        result.Should().Contain("Guarda únicamente datos expresados o confirmados por el cliente");
        result.Should().Contain("Mantén objetivos internos y marcadores de estado fuera de facts de usuario");
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
            Name = "Mimi",
            FactSchema =
            [
                new FactSchemaEntry { Key = "baby_name", Label = "nombre del bebé", CaptureMode = "eager", Source = "user" }
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
            Name = "Mimi",
            FactSchema =
            [
                new FactSchemaEntry { Key = "service", Label = "plan", CaptureMode = "onDemand", Source = "user" }
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
            Name = "Mimi",
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "scheduling",
                        ReentryOnFactChanged = ["desired_date", "desired_time"]
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
            Name = "Mimi",
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "scheduling",
                        AdvanceWhenFacts = ["desired_date", "desired_time"],
                        ReentryOnFactChanged = ["desired_date", "desired_time"]
                    },
                    new AgentFlowStage { Id = "finalization", AdvanceWhenFacts = [] }
                ]
            }
        };

        var state = new ConversationState();
        state.StageFactSnapshots["scheduling"] = new Dictionary<string, string>
        {
            ["desired_date"] = "2026-05-27",
            ["desired_time"] = "08:00"
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = state,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["desired_date"] = "2026-05-27",
                ["desired_time"] = "08:00"
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
            Name = "Mimi",
            FactSchema =
            [
                new FactSchemaEntry { Key = "desired_date", Label = "fecha deseada", Source = "user" },
                new FactSchemaEntry { Key = "desired_time", Label = "hora deseada", Source = "user" }
            ],
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "scheduling",
                        AdvanceWhenFacts = ["desired_date", "desired_time"],
                        ReentryOnFactChanged = ["desired_date", "desired_time"]
                    },
                    new AgentFlowStage { Id = "finalization", AdvanceWhenFacts = [] }
                ]
            }
        };

        var state = new ConversationState();
        state.StageFactSnapshots["scheduling"] = new Dictionary<string, string>
        {
            ["desired_date"] = "2026-05-27",
            ["desired_time"] = "08:00"
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = state,
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["desired_date"] = "2026-05-28",
                ["desired_time"] = "10:00"
            }
        };

        var result = Compose(config, [], session);

        result.Should().Contain("## ATENCIÓN: DATOS MODIFICADOS");
        result.Should().Contain("2026-05-27");
        result.Should().Contain("2026-05-28");
    }

}
