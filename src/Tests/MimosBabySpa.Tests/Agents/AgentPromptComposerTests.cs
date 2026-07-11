using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
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
        new FlowStageDetector());

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
        PaymentTransaction? latestPayment = null,
        IReadOnlyList<IAgentTool>? enabledTools = null) =>
        Composer.Compose(new PromptCompositionInput
        {
            Config = config,
            History = history,
            Temporal = DefaultTemporal,
            Session = session,
            LatestPayment = latestPayment,
            EnabledTools = enabledTools ?? []
        });

    [Fact]
    public void Compose_AlwaysIncludesTemporalBlock()
    {
        var result = Compose(DefaultConfig, []);

        result.Should().Contain("## CONTEXTO TEMPORAL");
        result.Should().Contain("2026-05-21");
        result.Should().Contain("2026-05-22");
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

        result.Should().Contain("## CONTEXTO DEL TURNO");
        result.Should().Contain("motivo_apertura: primera respuesta visible");
        result.Should().NotContain("saludo breve");
        result.Should().NotContain("pregunta abierta");
    }

    [Fact]
    public void Compose_WithBotHistory_IncludesTemporalBlock()
    {
        var history = new[]
        {
            new Message { Sender = "user",      MessageText = "hola" },
            new Message { Sender = "assistant", MessageText = "Ãƒâ€šÃ‚Â¡Hola! Soy Mimi." }
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
                new FactSchemaEntry { Key = "baby_age_months", Label = "edad del bebÃƒÆ’Ã‚Â© (meses)", Type = "number", Source = "user", Required = true },
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
                ["add_ons"]         = "DecoraciÃƒÆ’Ã‚Â³n Sencilla"
            },
            ManageableReservations =
            [
                new Reservation
                {
                    Status = ReservationStatus.Confirmed,
                    ReservationDateTime = new DateTime(2026, 5, 22, 10, 0, 0),
                    Service = new Service { ServiceName = "Plan Marineritos" }
                }
            ]
        };

        var result = Compose(config, [], session);

        result.Should().Contain("## ESTADO ACTUAL");
        result.Should().Contain("edad del bebÃƒÆ’Ã‚Â© (meses): 5");
        result.Should().Contain("plan / servicio: Plan Marineritos");
        result.Should().Contain("complementos: DecoraciÃƒÆ’Ã‚Â³n Sencilla");
        result.Should().Contain("## ESTADO RESERVA");
        result.Should().Contain("Reservas activas del cliente:");
        result.Should().Contain("Plan Marineritos");
        result.Should().NotContain("## RESERVAS GESTIONABLES");
        result.Should().NotContain("hay_reservas_gestionables");
    }

    [Fact]
    public void Compose_WithMultipleManageableReservations_RendersDisambiguationRuleWithoutIds()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            BusinessToday = new DateOnly(2026, 5, 21),
            ManageableReservations =
            [
                new Reservation
                {
                    ReservationId = firstId,
                    Status = ReservationStatus.Confirmed,
                    ReservationDateTime = new DateTime(2026, 5, 22, 10, 0, 0),
                    Service = new Service { ServiceName = "Plan Marineritos" }
                },
                new Reservation
                {
                    ReservationId = secondId,
                    Status = ReservationStatus.Confirmed,
                    ReservationDateTime = new DateTime(2026, 5, 23, 11, 0, 0),
                    Service = new Service { ServiceName = "Plan Ballenitas" }
                }
            ]
        };

        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = DefaultConfig.Name,
            Persona = DefaultConfig.Persona,
            ReservationManagement = new ReservationManagementDefinitions
            {
                ManageableReservationGuidance = "cuando el cliente pida cambiar, cancelar o confirmar una reserva sin identificar, pide elegir por fecha y servicio"
            }
        };

        var result = Compose(config, [], session);

        result.Should().Contain("Plan Marineritos");
        result.Should().Contain("Plan Ballenitas");
        result.Should().Contain("cuando el cliente pida cambiar, cancelar o confirmar una reserva sin identificar");
        result.Should().NotContain(firstId.ToString());
        result.Should().NotContain(secondId.ToString());
        result.Should().NotContain("id_reserva");
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
            new AgentToolContext { Conversation = new Conversation(), Facts = [], ActivePayment = payment });

        result.Should().Contain("payment: status=created");
        result.Should().Contain("COP $67,500");
        result.Should().NotContain("## CHECKOUT PENDIENTE");
        result.Should().NotContain("hay_link_pendiente: true");
        result.Should().NotContain("link_actual: https://pay.example/link");
        result.Should().NotContain("si el cliente cambia servicio");
        result.Should().NotContain("prepare_checkout");
    }

    [Fact]
    public void Compose_WithExpiredLatestPayment_RendersExpiredPaymentState()
    {
        var payment = new PaymentTransaction
        {
            Status = PaymentTransactionStatus.Created,
            AmountInCents = 2500000,
            Currency = "COP",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext { Conversation = new Conversation(), Facts = [] },
            payment);

        result.Should().Contain("payment: status=expired");
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

        result.Should().Contain("payment: status=confirmed");
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

        result.Should().Contain("payment: status=confirmed");
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

        // Sin facts ni reservas activas, no hay bloques de estado.
        result.Should().NotContain("## ESTADO ACTUAL");
        result.Should().NotContain("## ESTADO RESERVA");
    }


    [Fact]
    public void Compose_WithExpiredReservation_DoesNotRenderReservationStateBlock()
    {
        var result = Compose(
            DefaultConfig,
            [],
            new AgentToolContext
            {
                BusinessToday = new DateOnly(2026, 5, 21),
                Conversation = new Conversation(),
                Facts = [],
                ManageableReservations =
                [
                    new Reservation
                    {
                        Status = ReservationStatus.Confirmed,
                        ReservationDateTime = new DateTime(2026, 5, 20, 10, 0, 0),
                        Service = new Service { ServiceName = "Plan Marineritos" }
                    },
                    new Reservation
                    {
                        Status = ReservationStatus.Cancelled,
                        ReservationDateTime = new DateTime(2026, 5, 22, 10, 0, 0),
                        Service = new Service { ServiceName = "Plan Ballenitas" }
                    }
                ]
            });

        result.Should().NotContain("## ESTADO RESERVA");
        result.Should().NotContain("Plan Marineritos");
        result.Should().NotContain("Plan Ballenitas");
    }


    [Fact]
    public void Compose_WithBusinessDayRollover_ReintroducesAndKeepsCurrentFacts()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = DefaultConfig.Persona,
            FactSchema =
            [
                new FactSchemaEntry { Key = "service", Label = "servicio", Source = "user" },
                new FactSchemaEntry { Key = "desired_date", Label = "fecha", Source = "user" },
                new FactSchemaEntry { Key = "desired_time", Label = "hora", Source = "user" }
            ]
        };

        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            BusinessDayRollover = true,
            PreviousBusinessDay = new DateOnly(2026, 6, 17),
            RolloverClearedFacts = ["desired_time"],
            Facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["service"] = "Corte infantil",
                ["desired_date"] = "2026-06-18"
            }
        };

        var history = new[]
        {
            new Message { Sender = "assistant", MessageText = "Hola, soy Mimi." }
        };

        var result = Compose(config, history, session);

        result.Should().Contain("## CONTEXTO DEL TURNO");
        result.Should().Contain("motivo_apertura: nuevo dia operativo");
        result.Should().Contain("## ESTADO ACTUAL");
        result.Should().Contain("servicio: Corte infantil");
        result.Should().Contain("fecha: 2026-06-18");
        result.Should().NotContain("## RETOMA DE DIA");
        result.Should().NotContain("datos_vencidos_o_recalculables");
        result.Should().NotContain("no saludes como conversacion nueva");
    }

    [Fact]
    public void Compose_GreetingStage_FirstEver_RendersVariantConversationGuidance()
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
                            ["firstEver"] = new() { ConversationGuidance = "Ãƒâ€šÃ‚Â¡Hola! Soy Mimi de Mimo's Baby Spa." }
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
        result.Should().Contain("Ãƒâ€šÃ‚Â¡Hola! Soy Mimi de Mimo's Baby Spa.");
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
                            ["firstEver"]         = new() { ConversationGuidance = "Ãƒâ€šÃ‚Â¡Hola! Soy Mimi." },
                            ["returningCustomer"] = new() { ConversationGuidance = "Ãƒâ€šÃ‚Â¡Bienvenido de vuelta!" }
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
        result.Should().NotContain("Ãƒâ€šÃ‚Â¡Hola! Soy Mimi.");
    }

    [Fact]
    public void Compose_MimiStyleFlow_DiscoveryFirstStage_PersonaHasGreetingRules()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Mimi",
            Persona = "## ROL\nEres Mimi.\n\n## CÃƒÆ’Ã¢â‚¬Å“MO ABRES LA CONVERSACIÃƒÆ’Ã¢â‚¬Å“N\n- En tu primer mensaje: saludo.\n- Si conoces el nombre del cliente, salÃƒÆ’Ã‚Âºdalo por nombre.",
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
                        Goal = "Conocer al bebÃƒÆ’Ã‚Â© y elegir servicio",
                        AllowedActions = ["get_service_catalog", "set_fact"],
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

        result.Should().Contain("CÃƒÆ’Ã¢â‚¬Å“MO ABRES LA CONVERSACIÃƒÆ’Ã¢â‚¬Å“N");
        result.Should().Contain("etapa: discovery");
        result.Should().NotContain("etapa: greeting");
        result.Should().Contain("criterio_de_avance: la etapa se completa");
        result.Should().Contain("datos_para_completar_etapa: baby_name (baby_name), baby_age_months (baby_age_months), service (service)");
        result.Should().Contain("usa estos datos como");
        result.Should().NotContain("si el cliente solo saluda");
        result.Should().NotContain("facts_pendientes");
        result.Should().NotContain("el sistema te llevarÃƒÆ’Ã‚Â¡ automÃƒÆ’Ã‚Â¡ticamente al siguiente paso");
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

        result.Should().Contain("criterio_de_avance: la etapa se completa");
        result.Should().Contain("datos_para_completar_etapa: service (service)");
        result.Should().NotContain("facts_pendientes");
        result.Should().NotContain("el sistema te llevarÃƒÆ’Ã‚Â¡ automÃƒÆ’Ã‚Â¡ticamente al siguiente paso");
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
        result.Should().NotContain("el sistema te llevarÃƒÆ’Ã‚Â¡ automÃƒÆ’Ã‚Â¡ticamente al siguiente paso");
    }

    // BuildReentryBlock ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬ÃƒÂ¢Ã¢â‚¬ÂÃ¢â€šÂ¬

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

        result.Should().NotContain("## ATENCIÃƒÆ’Ã¢â‚¬Å“N");
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

        result.Should().NotContain("## ATENCIÃƒÆ’Ã¢â‚¬Å“N");
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

        result.Should().Contain("## ATENCION: DATOS MODIFICADOS");
        result.Should().Contain("2026-05-27");
        result.Should().Contain("2026-05-28");
    }

    [Fact]
    public void Compose_WhenOutsideOperatingHours_ReturnsClosedBusinessPrompt()
    {
        var session = new AgentToolContext
        {
            Conversation = new Conversation(),
            OperatingHours = new OperatingHoursTurnContext(
                true,
                true,
                "hoy de 1:00 p. m. a 9:00 p. m.")
        };

        var result = Compose(DefaultConfig, [], session);

        result.Should().Contain("## DISPONIBILIDAD ACTUAL");
        result.Should().Contain("fuera de horario laboral");
        result.Should().Contain("proximo_horario_habil: hoy de 1:00 p. m. a 9:00 p. m.");
        result.Should().Contain("no repitas literalmente la misma plantilla");
        result.Should().Contain("agradece el contacto");
        result.Should().Contain("Si empieza por hoy, no agregues fecha ni dia");
        result.Should().Contain("Eres Mimi");
        result.Should().Contain("gestiones operativas");
        result.Should().Contain("No solicites datos");
        result.Should().Contain("no termines con preguntas");
        result.Should().NotContain("comprar");
        result.Should().NotContain("agendar");
        result.Should().NotContain("productos");
        result.Should().NotContain("pedido");
    }


    [Fact]
    public void ProjectHistoryForTurn_FiltersBeforeBoundaryAndRedactsInactivePaymentLink()
    {
        var conversationId = Guid.NewGuid();
        var inactivePayment = new PaymentTransaction
        {
            PaymentTransactionId = Guid.NewGuid(),
            Status = PaymentTransactionStatus.Created,
            LinkUrl = "https://pay.example/expired",
            ExpiresAt = DateTime.UtcNow.AddMinutes(-5)
        };

        var history = new List<Message>
        {
            new()
            {
                ConversationId = conversationId,
                Sender = "bot",
                MessageText = "old https://pay.example/expired",
                Timestamp = new DateTime(2026, 7, 4, 10, 0, 0, DateTimeKind.Utc)
            },
            new()
            {
                ConversationId = conversationId,
                Sender = "bot",
                MessageText = "Paga aqui https://pay.example/expired",
                Timestamp = new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc)
            }
        };

        var method = typeof(AgentConversationService).GetMethod(
            "ProjectHistoryForTurn",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var projected = ((IReadOnlyList<Message>)method!.Invoke(
            null,
            new object?[] { history, new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc), null, inactivePayment })!).ToList();

        projected.Should().HaveCount(1);
        projected[0].MessageText.Should().Be("Paga aqui [link de pago no vigente]");
    }

    [Fact]
    public void Compose_WithToolScopedStage_RendersConversationalGuidanceOnly()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Luis",
            Persona = "Eres Luis.",
            Flow = new AgentFlowDefinition
            {
                Stages =
                [
                    new AgentFlowStage
                    {
                        Id = "discovery",
                        Goal = "Elegir servicio",
                        Collect = ["service"],
                        AllowedActions = ["resolve_service_selection"],
                        ConversationGuidance = "Presenta solo opciones oficiales.",
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

        var result = Compose(config, [], session, enabledTools: [new TestTool("resolve_service_selection")]);

        result.Should().Contain("guia_conversacional: Presenta solo opciones oficiales.");
        result.Should().NotContain("pregunta_conversacional");
        result.Should().Contain("datos_que_debe_capturar_si_el_cliente_los_menciona: service");
        result.Should().Contain("regla_collect: si el ultimo mensaje contiene alguno de esos datos");
        result.Should().Contain("## HERRAMIENTAS DE ESTE TURNO");
        result.Should().Contain("resolve_service_selection");
    }

    [Fact]
    public void Compose_WithGlobalActionAllowedActions_RendersToolNamesAndScopesTool()
    {
        var config = new AgentConfig
        {
            AgentId = DefaultConfig.AgentId,
            BusinessId = DefaultConfig.BusinessId,
            Name = "Luis",
            Persona = "Eres Luis.",
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "manage_existing_reservation",
                    Priority = 900,
                    Goal = "Gestionar reservas existentes.",
                    AllowedActions = ["manage_reservation"],
                    EntryActions =
                    [
                        new StageEntryAction
                        {
                            Tool = "manage_reservation",
                            When = new StageEntryActionCondition
                            {
                                MessageMatches = [new StageEntryMessageMatch { AnyOf = ["cambiar mi reserva"] }]
                            }
                        }
                    ]
                }
            ]
        };

        var result = Compose(config, [], new AgentToolContext
        {
            Conversation = new Conversation(),
            ConversationState = new ConversationState(),
            Facts = [],
            LatestUserMessage = "quiero cambiar mi reserva",
        }, enabledTools: [new TestTool("manage_reservation"), new TestTool("set_fact")]);

        result.Should().Contain("tools: manage_reservation");
        result.Should().Contain("## HERRAMIENTAS DE ESTE TURNO");
        result.Should().Contain("manage_reservation");
        result.Should().NotContain("set_fact");
    }

    private sealed class TestTool : IAgentTool
    {
        public TestTool(string name, IReadOnlyList<string>? capabilities = null)
        {
            Name = name;
            Capabilities = capabilities ?? [];
        }

        public string Name { get; }
        public IReadOnlyList<string> Capabilities { get; }
        public string Description => "Test tool";
        public string ParametersSchema => """{"type":"object"}""";

        public Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext ctx, CancellationToken cancellationToken = default) =>
            Task.FromResult("{}");
    }
}
