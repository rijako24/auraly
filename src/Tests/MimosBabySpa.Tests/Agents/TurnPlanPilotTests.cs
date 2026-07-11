using System.Text.Json;
using FluentAssertions;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Planning;
using MimosBabySpa.Application.LLM;
using Xunit;

namespace MimosBabySpa.Tests.Agents;

public sealed class TurnPlanPilotTests
{
    [Fact]
    public void Scope_UsesOnlyStageFactsAndSemanticSignals()
    {
        var schema = new[]
        {
            UserFact("customer_name", "string"),
            UserFact("customer_email", "email"),
            UserFact("desired_date", "date"),
            UserFact("delivery_phone", "phone")
        };
        var config = new AgentConfig { FactSchema = schema };
        var stage = new AgentFlowStage
        {
            AdvanceWhenFacts = ["customer_name"],
            Collect = ["customer_email"],
            Signals = [new StageSignalDefinition { Type = "service_selection", Description = "Customer selected a service", ValueSchema = Schema("{\"type\":\"string\"}") }]
        };
        var currentFacts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["desired_date"] = "2026-07-10"
        };

        var scope = TurnPlanScopeBuilder.Build(config, stage, currentFacts);

        scope.Facts.Keys.Should().BeEquivalentTo("customer_name", "customer_email", "desired_date");
        scope.Facts.Keys.Should().NotContain("delivery_phone");
        scope.Signals.Keys.Should().ContainSingle().Which.Should().Be("service_selection");
    }

    [Fact]
    public async Task Planner_UsesStrictStructuredOutput_AndReturnsStructuredDomainSignal()
    {
        const string message = "Hoy no puedo, mañana sí. Mi bebé en 2 días cumple 3 meses. Necesito 2 papas y 3 tocinetas.";
        var arguments = JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "booking", confidence = 0.96, evidence = (string?)null },
            facts = new object[]
            {
                new { key = "desired_date", operation = "set", value = "2026-07-11", evidence = "mañana sí" },
                new { key = "baby_age_months", operation = "set", value = 3, evidence = "Mi bebé en 2 días cumple 3 meses" }
            },
            signals = new object[]
            {
                new
                {
                    type = "order_changes",
                    value = new object[]
                    {
                        new { operation = "add", productText = "papas", quantity = 2, groupReference = (string?)null },
                        new { operation = "add", productText = "tocinetas", quantity = 3, groupReference = (string?)null }
                    },
                    evidence = "Necesito 2 papas y 3 tocinetas"
                }
            },
            decision = (object?)null,
            response = new { mode = "continue", ambiguousFields = Array.Empty<string>() }
        });
        var chat = new StubChatClient(arguments);
        var planner = new LlmTurnPlanner(chat, new TurnPlanValidator());
        var config = new AgentConfig
        {
            Persona = "Asistente de prueba",
            Flows = [new AgentFlowDefinition { Id = "booking", Type = FlowTypes.Primary }],
            FactSchema =
            [
                UserFact("desired_date", "date"),
                UserFact("baby_age_months", "number")
            ]
        };
        var stage = new AgentFlowStage
        {
            Id = "pilot",
            Goal = "Capturar datos y pedido",
            AdvanceWhenFacts = ["desired_date"],
            Collect = ["baby_age_months"],
            Signals = [new StageSignalDefinition { Type = "order_changes", ValueSchema = OrderChangesSchema() }]
        };
        var scope = TurnPlanScopeBuilder.Build(config, stage, new Dictionary<string, string>());
        var context = new TurnPlanningContext(
            config,
            stage,
            scope,
            new Dictionary<string, string>(),
            message,
            new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.FromHours(-5)),
            []);

        var proposal = await planner.PlanAsync(context);

        proposal.Success.Should().BeTrue(string.Join("; ", proposal.Errors));
        proposal.Plan!.Facts.Should().HaveCount(2);
        proposal.Plan.Signals.Should().ContainSingle();
        proposal.Plan.Signals[0].Value.GetArrayLength().Should().Be(2);
        chat.CapturedTools.Should().BeEmpty();
        chat.CapturedOptions!.StructuredOutput.Should().NotBeNull();
        chat.CapturedOptions.StructuredOutput!.Name.Should().Be(TurnPlanJsonSchemaBuilder.SchemaName);
        chat.CapturedOptions.StructuredOutput.Strict.Should().BeTrue();
        chat.CapturedOptions.Temperature.Should().Be(0);
    }

    [Fact]
    public async Task Planner_RepairsInvalidAmbiguousMutationOnce_WithoutExecutingAnything()
    {
        const string message = "tiene 2 meses y no sé si reservar para los 2 o para los 3";
        var invalid = JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "booking", confidence = 0.9, evidence = message },
            facts = new object[]
            {
                new { key = "baby_age_months", operation = "set", value = 2, evidence = "tiene 2 meses" },
                new { key = "baby_age_months", operation = "set", value = 3, evidence = "para los 3" }
            },
            signals = Array.Empty<object>(),
            decision = (object?)null,
            response = new
            {
                mode = "ask_clarification",
                ambiguousFields = new[] { "baby_age_months" }
            }
        });
        var repaired = JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "booking", confidence = 0.9, evidence = message },
            facts = Array.Empty<object>(),
            signals = Array.Empty<object>(),
            decision = (object?)null,
            response = new
            {
                mode = "ask_clarification",
                ambiguousFields = new[] { "baby_age_months" }
            }
        });
        var chat = new SequenceChatClient(invalid, repaired);
        var config = new AgentConfig
        {
            Flows = [new AgentFlowDefinition { Id = "booking", Type = FlowTypes.Primary }],
            FactSchema = [UserFact("baby_age_months", "number")]
        };
        var stage = new AgentFlowStage
        {
            Id = "discovery",
            Collect = ["baby_age_months"]
        };
        var scope = TurnPlanScopeBuilder.Build(config, stage, new Dictionary<string, string>());
        var planner = new LlmTurnPlanner(chat, new TurnPlanValidator());

        var proposal = await planner.PlanAsync(new TurnPlanningContext(
            config,
            stage,
            scope,
            new Dictionary<string, string>(),
            message,
            DateTimeOffset.UtcNow,
            []));

        proposal.Success.Should().BeTrue(string.Join("; ", proposal.Errors));
        proposal.Plan!.Facts.Should().BeEmpty();
        proposal.Plan.Response.AmbiguousFields.Should().Equal("baby_age_months");
        chat.CallCount.Should().Be(2);
    }
    [Fact]
    public void Validator_RejectsUnsupportedEvidenceAndOutOfScopeFacts()
    {
        var definition = UserFact("desired_date", "date");
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.Key] = definition
            },
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase));
        var plan = new TurnPlan
        {
            Facts =
            [
                Claim("desired_date", "2026-07-12", "el viernes"),
                Claim("system.checkout", true, "confirmo")
            ]
        };

        var result = new TurnPlanValidator().Validate(plan, scope, "mañana a las diez");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("outside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsMutationForAFieldBeingClarified()
    {
        var definition = UserFact("baby_age_months", "number");
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.Key] = definition
            },
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase));
        var plan = new TurnPlan
        {
            Facts = [Claim("baby_age_months", 2, "tiene 2 meses")],
            Response = new TurnPlanResponseDirective
            {
                Mode = "ask_clarification",
                AmbiguousFields = ["baby_age_months"]
            }
        };

        var result = new TurnPlanValidator().Validate(
            plan,
            scope,
            "tiene 2 meses pero quiero información para cuando tenga 3");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("cannot be mutated", StringComparison.OrdinalIgnoreCase));
    }
    private static JsonElement Schema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static JsonElement OrderChangesSchema() => Schema("""
        {
          "type": "array",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "operation": { "type": "string", "enum": ["add", "remove", "set_quantity"] },
              "productText": { "type": "string" },
              "quantity": { "type": ["number", "null"] },
              "groupReference": { "type": ["string", "null"] }
            },
            "required": ["operation", "productText", "quantity", "groupReference"]
          }
        }
        """);
    private static FactSchemaEntry UserFact(string key, string type) => new()
    {
        Key = key,
        Label = key,
        Type = type,
        Source = "user"
    };

    private static PlannedFactClaim Claim(string key, object value, string evidence)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return new PlannedFactClaim
        {
            Key = key,
            Operation = TurnPlanOperations.Set,
            Value = document.RootElement.Clone(),
            Evidence = evidence
        };
    }

    private sealed class SequenceChatClient : IChatClient
    {
        private readonly Queue<string> _responses;
        public SequenceChatClient(params string[] responses) => _responses = new Queue<string>(responses);
        public int CallCount { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ChatToolDefinition>? tools = null,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            var content = _responses.Dequeue();
            return Task.FromResult(new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = content,
                AssistantMessage = ChatMessage.Assistant(content)
            });
        }
    }
    private sealed class StubChatClient : IChatClient
    {
        private readonly string _arguments;

        public StubChatClient(string arguments) => _arguments = arguments;

        public IReadOnlyList<ChatToolDefinition> CapturedTools { get; private set; } = [];
        public ChatCompletionOptions? CapturedOptions { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            IReadOnlyList<ChatToolDefinition>? tools = null,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedTools = tools ?? [];
            CapturedOptions = options;
            return Task.FromResult(new ChatCompletionResult
            {
                Success = true,
                FinishReason = ChatCompletionFinishReason.Stop,
                Content = _arguments,
                AssistantMessage = ChatMessage.Assistant(_arguments),
                PromptTokens = 100,
                CompletionTokens = 50
            });
        }
    }
}
