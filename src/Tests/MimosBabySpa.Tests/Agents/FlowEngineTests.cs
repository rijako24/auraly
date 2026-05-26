using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Composition;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Orchestration;
using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

/// <summary>
/// Tests unitarios de los componentes clave del FlowEngine:
/// FlowLlm (parsing de JSON), FlowRefResolver y FlowStageDetector.
/// </summary>
public sealed class FlowEngineTests
{
    // ── FlowLlm.ParseResponse ─────────────────────────────────────────────────

    [Fact]
    public void ParseResponse_valid_json_extracts_all_fields()
    {
        var json = """
            {
              "intent": "Continue",
              "reply": "¡Hola Thomas!"
            }
            """;

        var result = FlowLlm.ParseResponse(json, 100);

        result.Intent.Should().Be("Continue");
        result.Reply.Should().Be("¡Hola Thomas!");
        result.Tokens.Should().Be(100);
    }

    [Fact]
    public void ParseResponse_confirm_intent_normalized()
    {
        var json = """{"intent":"Confirmar","facts":{},"reply":"Ok"}""";
        var result = FlowLlm.ParseResponse(json, 0);
        result.Intent.Should().Be("Confirm");
    }

    [Fact]
    public void ParseResponse_escalate_intent_preserved()
    {
        var json = """{"intent":"Escalate","facts":{},"reply":"Te conecto."}""";
        var result = FlowLlm.ParseResponse(json, 0);
        result.Intent.Should().Be("Escalate");
    }

    [Fact]
    public void ParseResponse_malformed_json_returns_fallback()
    {
        var result = FlowLlm.ParseResponse("Hola, ¿cómo puedo ayudarte?", 0);
        result.Intent.Should().Be("Continue");
        result.Reply.Should().NotBeEmpty();
    }

    [Fact]
    public void ParseResponse_json_wrapped_in_markdown_extracts_correctly()
    {
        var json = """
            ```json
            {"intent":"Continue","reply":"Aquí tu catálogo."}
            ```
            """;

        var result = FlowLlm.ParseResponse(json, 0);
        result.Intent.Should().Be("Continue");
        result.Reply.Should().Be("Aquí tu catálogo.");
    }

    [Fact]
    public void ParseResponse_empty_facts_does_not_throw()
    {
        var json = """{"intent":"Continue","facts":{},"reply":"Hola"}""";
        var result = FlowLlm.ParseResponse(json, 0);
        result.Reply.Should().Be("Hola");
    }

    // ── FlowRefResolver ───────────────────────────────────────────────────────

    [Fact]
    public void FlowRefResolver_resolves_fact_ref()
    {
        var session = BuildSession(facts: new() { ["service"] = "Plan Básico" });
        var resolved = FlowRefResolver.ResolveRef("@fact.service", session, null);
        resolved.Should().Be("Plan Básico");
    }

    [Fact]
    public void FlowRefResolver_resolves_const_true()
    {
        var session = BuildSession();
        var resolved = FlowRefResolver.ResolveRef("@const.true", session, null);
        resolved.Should().Be("true");
    }

    [Fact]
    public void FlowRefResolver_returns_literal_when_no_prefix()
    {
        var session = BuildSession();
        var resolved = FlowRefResolver.ResolveRef("literal_value", session, null);
        resolved.Should().Be("literal_value");
    }

    [Fact]
    public void FlowRefResolver_missing_fact_returns_null()
    {
        var session = BuildSession();
        var resolved = FlowRefResolver.ResolveRef("@fact.nonexistent", session, null);
        resolved.Should().BeNull();
    }

    [Fact]
    public void FlowRefResolver_resolves_result_field_from_tool_result()
    {
        var toolResult = FlowToolResult.Parse("""{"ok":true,"data":{"flow":"verbal_confirmation"}}""");
        var session = BuildSession();
        var resolved = FlowRefResolver.ResolveRef("@result.flow", session, toolResult);
        resolved.Should().Be("verbal_confirmation");
    }

    // ── FlowStageDetector ─────────────────────────────────────────────────────

    [Fact]
    public void FlowStageDetector_returns_first_incomplete_stage()
    {
        var flow = BuildFlow(
            ("greeting", StageCompletionCriteria.Always, []),
            ("discovery", StageCompletionCriteria.FactsCollected, ["baby_name"]));

        var state = BuildConversationState(completedOneShot: ["greeting"]);
        var session = BuildSession(state: state);

        var detector = new FlowStageDetector();
        var stage = detector.DetectCurrentStage(flow, session);

        stage.Should().NotBeNull();
        stage!.Id.Should().Be("discovery");
    }

    [Fact]
    public void FlowStageDetector_returns_null_when_all_stages_complete()
    {
        var flow = BuildFlow(
            ("greeting", StageCompletionCriteria.Always, []));

        var state = BuildConversationState(completedOneShot: ["greeting"]);
        var session = BuildSession(state: state);

        var detector = new FlowStageDetector();
        var stage = detector.DetectCurrentStage(flow, session);

        stage.Should().BeNull();
    }

    [Fact]
    public void FlowStageDetector_skips_factsCollected_stage_when_all_facts_present()
    {
        var flow = BuildFlow(
            ("greeting", StageCompletionCriteria.Always, []),
            ("discovery", StageCompletionCriteria.FactsCollected, ["baby_name"]),
            ("service", StageCompletionCriteria.FactsCollected, ["service"]));

        var state = BuildConversationState(completedOneShot: ["greeting"]);
        var session = BuildSession(
            state: state,
            facts: new() { ["baby_name"] = "Thomas" });

        var detector = new FlowStageDetector();
        var stage = detector.DetectCurrentStage(flow, session);

        stage.Should().NotBeNull();
        stage!.Id.Should().Be("service");
    }

    [Fact]
    public void FlowStageDetector_skips_discovery_when_marked_in_one_shot_without_facts()
    {
        var flow = BuildFlow(
            ("greeting", StageCompletionCriteria.Always, []),
            ("discovery", StageCompletionCriteria.FactsCollected, ["baby_name", "baby_age_months"]),
            ("service_presentation", StageCompletionCriteria.FactsCollected, ["service"]));

        var state = BuildConversationState(completedOneShot: ["greeting", "discovery"]);
        var session = BuildSession(state: state, facts: new());

        var detector = new FlowStageDetector();
        var stage = detector.DetectCurrentStage(flow, session);

        stage.Should().NotBeNull();
        stage!.Id.Should().Be("service_presentation");
    }

    [Fact]
    public void FlowStageDetector_appliesWhen_fact_condition_filters_stage()
    {
        var flow = new AgentFlowDefinition
        {
            Stages =
            [
                new AgentFlowStage
                {
                    Id = "closure",
                    CompletedWhen = StageCompletionCriteria.ToolSucceeded,
                    AppliesWhen = new AgentFlowStageCondition
                    {
                        Field = "@fact.flow",
                        EqualsValue = "verbal_confirmation"
                    }
                }
            ]
        };

        // Sin el fact correcto: stage se salta (no aplica)
        var sessionNoFact = BuildSession();
        var detector = new FlowStageDetector();
        var stage = detector.DetectCurrentStage(flow, sessionNoFact);
        stage.Should().BeNull();

        // Con el fact correcto: stage aplica
        var sessionWithFact = BuildSession(facts: new() { ["flow"] = "verbal_confirmation" });
        var stageFound = detector.DetectCurrentStage(flow, sessionWithFact);
        stageFound.Should().NotBeNull();
        stageFound!.Id.Should().Be("closure");
    }

    // ── FlowToolResult.Parse ──────────────────────────────────────────────────

    [Fact]
    public void FlowToolResult_parses_template_id_and_data()
    {
        var raw = """
            {
              "ok": true,
              "data": {
                "template_id": "checkout_no_deposit",
                "template_data": { "service_name": "Plan Básico", "total": 50000 }
              }
            }
            """;

        var result = FlowToolResult.Parse(raw);

        result.IsError.Should().BeFalse();
        result.TemplateId.Should().Be("checkout_no_deposit");
        result.TemplateData.Should().ContainKey("service_name");
    }

    [Fact]
    public void FlowToolResult_parses_error_response()
    {
        var raw = """{"ok":false,"error":{"code":"invalid_date","message":"Fecha inválida"}}""";
        var result = FlowToolResult.Parse(raw);
        result.IsError.Should().BeTrue();
        result.ErrorCode.Should().Be("invalid_date");
    }

    [Fact]
    public void FlowToolResult_GetString_extracts_top_level_data_field()
    {
        var raw = """{"ok":true,"data":{"flow":"verbal_confirmation"}}""";
        var result = FlowToolResult.Parse(raw);
        result.GetString("flow").Should().Be("verbal_confirmation");
    }

    [Fact]
    public void FlowToolResult_GetString_returns_null_for_missing_field()
    {
        var raw = """{"ok":true,"data":{}}""";
        var result = FlowToolResult.Parse(raw);
        result.GetString("nonexistent").Should().BeNull();
    }

    [Fact]
    public void AgentToolRegistry_filters_tools_by_stage_allowed_list()
    {
        var registry = AgentTestHelpers.CreateToolRegistry();
        var config = new AgentConfig
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            EnabledToolNames = ["create_reservation", "prepare_checkout"]
        };

        var stage = new AgentFlowStage
        {
            Id = "booking",
            AllowedTools = ["prepare_checkout"]
        };

        var tools = registry.GetToolsForStage(config, stage);

        tools.Should().ContainSingle().Which.Name.Should().Be("prepare_checkout");
    }

    // ── EvalCollectEntry: marker result:X=Y ───────────────────────────────────

    [Fact]
    public void Scheduling_stage_not_completed_when_slot_not_confirmed()
    {
        var toolResult = FlowToolResult.Parse(
            """{"ok":true,"data":{"slot_confirmed":false,"verbal_status":"sin_disponibilidad","available_slots":[]}}""");

        var stage = new AgentFlowStage
        {
            Id = "scheduling",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["desired_date", "desired_time", "result:slot_confirmed=true"]
        };

        var session = BuildSession(facts: new()
        {
            ["desired_date"] = "2026-05-25",
            ["desired_time"] = "09:00"
        });

        var completed = FlowStageCompletionRules.IsStageCompleted(stage, session, toolResult);
        completed.Should().BeFalse();
    }

    [Fact]
    public void Scheduling_stage_completed_when_slot_confirmed_true()
    {
        var toolResult = FlowToolResult.Parse(
            """{"ok":true,"data":{"slot_confirmed":true,"time":"09:00","verbal_status":"horario_disponible_no_reservado","available_slots":[]}}""");

        var stage = new AgentFlowStage
        {
            Id = "scheduling",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["desired_date", "desired_time", "result:slot_confirmed=true"]
        };

        var session = BuildSession(facts: new()
        {
            ["desired_date"] = "2026-05-25",
            ["desired_time"] = "09:00"
        });

        var completed = FlowStageCompletionRules.IsStageCompleted(stage, session, toolResult);
        completed.Should().BeTrue();
    }

    [Fact]
    public void Scheduling_stage_not_completed_when_time_fact_missing()
    {
        var toolResult = FlowToolResult.Parse(
            """{"ok":true,"data":{"slot_confirmed":true,"available_slots":[]}}""");

        var stage = new AgentFlowStage
        {
            Id = "scheduling",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["desired_date", "desired_time", "result:slot_confirmed=true"]
        };

        var session = BuildSession(facts: new()
        {
            ["desired_date"] = "2026-05-25"
            // desired_time ausente
        });

        var completed = FlowStageCompletionRules.IsStageCompleted(stage, session, toolResult);
        completed.Should().BeFalse();
    }

    [Fact]
    public void Scheduling_list_mode_does_not_satisfy_slot_confirmed_marker()
    {
        var toolResult = FlowToolResult.Parse(
            """{"ok":true,"data":{"slot_confirmed":false,"template_id":"availability_slots","available_slots":["08:00","09:00"]}}""");

        var stage = new AgentFlowStage
        {
            Id = "scheduling",
            CompletedWhen = StageCompletionCriteria.FactsCollected,
            Collects = ["desired_date", "desired_time", "result:slot_confirmed=true"]
        };

        var session = BuildSession(facts: new()
        {
            ["desired_date"] = "2026-05-25",
            ["desired_time"] = "09:00"
        });

        var completed = FlowStageCompletionRules.IsStageCompleted(stage, session, toolResult);
        completed.Should().BeFalse();
    }

    // ── FlowRefResolver: @pack.booking.has_pending_payment ────────────────────

    [Fact]
    public void FlowRefResolver_has_pending_payment_false_when_no_pack_context()
    {
        var session = BuildSession();
        var resolved = FlowRefResolver.ResolveRef("@pack.booking.has_pending_payment", session, null);
        resolved.Should().Be("false");
    }

    [Fact]
    public void FlowRefResolver_has_pending_payment_true_when_active_payment_created()
    {
        var session = BuildSession();
        session.SetPackContext(new BookingPackContext
        {
            ActivePayment = new PaymentTransaction
            {
                Status = PaymentTransactionStatus.Created
            }
        });

        var resolved = FlowRefResolver.ResolveRef("@pack.booking.has_pending_payment", session, null);
        resolved.Should().Be("true");
    }

    [Fact]
    public void FlowRefResolver_has_pending_payment_false_when_payment_confirmed()
    {
        var session = BuildSession();
        session.SetPackContext(new BookingPackContext
        {
            ActivePayment = new PaymentTransaction
            {
                Status = PaymentTransactionStatus.Confirmed
            }
        });

        var resolved = FlowRefResolver.ResolveRef("@pack.booking.has_pending_payment", session, null);
        resolved.Should().Be("false");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AgentToolContext BuildSession(
        ConversationState? state = null,
        Dictionary<string, string>? facts = null) =>
        new()
        {
            AgentId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            ConversationState = state ?? BuildConversationState(),
            Conversation = new Conversation(),
            Facts = facts ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        };

    private static ConversationState BuildConversationState(
        IEnumerable<string>? completedOneShot = null,
        IEnumerable<string>? completedAction = null) =>
        new()
        {
            CompletedOneShotStages = new HashSet<string>(
                completedOneShot ?? [], StringComparer.OrdinalIgnoreCase),
            CompletedActionStages = new HashSet<string>(
                completedAction ?? [], StringComparer.OrdinalIgnoreCase)
        };

    private static AgentFlowDefinition BuildFlow(
        params (string Id, string CompletedWhen, string[] Collects)[] stages) =>
        new()
        {
            Stages = stages.Select(s => new AgentFlowStage
            {
                Id = s.Id,
                CompletedWhen = s.CompletedWhen,
                Collects = s.Collects
            }).ToList()
        };
}
