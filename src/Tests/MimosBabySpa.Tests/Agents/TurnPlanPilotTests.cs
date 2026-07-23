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
    public void Scope_UsesOnlyFactsExplicitlyCollectableByTheCurrentStage()
    {
        var selection = new AgentFlowStage
        {
            Id = "selection",
            AdvanceWhenFacts = ["order_finalized"],
            Collect = ["delivery_address", "payment_method"]
        };
        var confirmation = new AgentFlowStage
        {
            Id = "confirmation",
            AdvanceWhenFacts = ["customer_confirmed"],
            Collect = ["customer_confirmed"]
        };
        var config = new AgentConfig
        {
            FactSchema =
            [
                UserFact("order_finalized", "boolean"),
                UserFact("delivery_address", "string"),
                UserFact("payment_method", "string"),
                UserFact("customer_confirmed", "boolean")
            ],
            Flows = [new AgentFlowDefinition { Id = "order", Type = FlowTypes.Primary, Stages = [selection, confirmation] }]
        };

        var scope = TurnPlanScopeBuilder.Build(config, selection, new Dictionary<string, string>(), "order");

        scope.Facts.Keys.Should().BeEquivalentTo("order_finalized", "delivery_address", "payment_method");
        scope.Facts.Keys.Should().NotContain("customer_confirmed");
    }

    [Fact]
    public async Task Planner_UsesStrictStructuredOutput_AndReturnsStructuredDomainSignal()
    {
        const string message = "Hoy no puedo, maÃ±ana sÃ­. Mi bebÃ© en 2 dÃ­as cumple 3 meses. Necesito 2 papas y 3 tocinetas.";
        var arguments = JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "booking", confidence = 0.96, evidence = (string?)null },
            facts = new object[]
            {
                new { key = "desired_date", operation = "set", value = "2026-07-11", evidence = "maÃ±ana sÃ­" },
                new { key = "baby_age_months", operation = "set", value = 3, evidence = "Mi bebÃ© en 2 dÃ­as cumple 3 meses" }
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
            GlobalActions =
            [
                new AgentGlobalAction
                {
                    Id = "restart",
                    ConversationGuidance = "restart only from an explicit new request",
                    Signal = new StageSignalDefinition { Type = "restart_request", ValueSchema = Schema("{\"type\":\"boolean\"}") }
                }
            ],
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
            new Dictionary<string, string>
            {
                ["customer_name"] = "Richard"
            },
            message,
            new DateTimeOffset(2026, 7, 10, 10, 0, 0, TimeSpan.FromHours(-5)),
            []);

        var proposal = await planner.PlanAsync(context);

        proposal.Success.Should().BeTrue(string.Join("; ", proposal.Errors));
        proposal.Plan!.Facts.Should().HaveCount(2);
        proposal.Plan.Signals.Should().ContainSingle();
        proposal.Plan.Signals[0].Value.GetArrayLength().Should().Be(2);
        chat.CapturedOptions!.StructuredOutput.Should().NotBeNull();
        chat.CapturedOptions.StructuredOutput!.Name.Should().Be(TurnPlanJsonSchemaBuilder.SchemaName);
        chat.CapturedOptions.StructuredOutput.Strict.Should().BeTrue();
        chat.CapturedOptions.Temperature.Should().Be(0);
        chat.CapturedMessages.Should().ContainSingle();
        var extractorPayload = chat.CapturedMessages![0].Content;
        extractorPayload.Should().Contain("\"currentFacts\":{\"customer_name\":\"Richard\"}");
        extractorPayload.Should().Contain(
            "already hydrated from persisted conversation facts and durable customer memory");
        extractorPayload.Should().Contain("restart only from an explicit new request");


    }

    [Fact]
    public async Task Planner_RepairsInvalidAmbiguousMutationOnce_WithoutExecutingAnything()
    {
        const string message = "tiene 2 meses y no sÃ© si reservar para los 2 o para los 3";
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
    public async Task Planner_MarksConfiguredOptionSelectorAmbiguous_WhenOptionsWereNotPresented()
    {
        const string message = "la a";
        var initial = JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "order", confidence = 0.9, evidence = (string?)null },
            facts = new object[]
            {
                new
                {
                    key = "customer_type",
                    operation = "set",
                    value = "Hogar",
                    confidence = 0.99,
                    evidence = "a"
                }
            },
            signals = Array.Empty<object>(),
            decision = (object?)null,
            response = new { mode = "continue", ambiguousFields = Array.Empty<string>() }
        });
        var chat = new SequenceChatClient(initial);
        var customerType = new FactSchemaEntry
        {
            Key = "customer_type",
            Label = "customer_type",
            Type = "string",
            Source = "user",
            Options =
            [
                new FactValueOption { Value = "Hogar", Label = "Hogar", Selector = "A" },
                new FactValueOption { Value = "Restaurante", Label = "Restaurante", Selector = "B" }
            ]
        };
        var config = new AgentConfig
        {
            Flows = [new AgentFlowDefinition { Id = "order", Type = FlowTypes.Primary }],
            FactSchema = [customerType]
        };
        var stage = new AgentFlowStage { Id = "customer_type", AdvanceWhenFacts = ["customer_type"] };
        var scope = TurnPlanScopeBuilder.Build(config, stage, new Dictionary<string, string>());
        var planner = new LlmTurnPlanner(chat, new TurnPlanValidator());

        var proposal = await planner.PlanAsync(new TurnPlanningContext(
            config, stage, scope, new Dictionary<string, string>(), message, DateTimeOffset.UtcNow, []));

        proposal.Success.Should().BeTrue(string.Join("; ", proposal.Errors));
        proposal.Plan!.Facts.Should().BeEmpty();
        proposal.Plan.Response.Mode.Should().Be("ask_clarification");
        proposal.Plan.Response.AmbiguousFields.Should().Equal("customer_type");
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Planner_DeterministicallyOverridesContradictoryConfiguredOptionSelectorClaim()
    {
        const string message = "la c";
        static string PlanWithValue(string value) => JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "order", confidence = 0.9, evidence = (string?)null },
            facts = new object[]
            {
                new
                {
                    key = "customer_type",
                    operation = "set",
                    value,
                    confidence = 0.99,
                    evidence = "c"
                }
            },
            signals = Array.Empty<object>(),
            decision = (object?)null,
            response = new { mode = "continue", ambiguousFields = Array.Empty<string>() }
        });
        var chat = new SequenceChatClient(PlanWithValue("ComidaRapida"));
        var customerType = CustomerTypeOptions();
        var config = new AgentConfig
        {
            Flows = [new AgentFlowDefinition { Id = "order", Type = FlowTypes.Primary }],
            FactSchema = [customerType]
        };
        var stage = new AgentFlowStage { Id = "customer_type", AdvanceWhenFacts = ["customer_type"] };
        var scope = TurnPlanScopeBuilder.Build(config, stage, new Dictionary<string, string>());
        var planner = new LlmTurnPlanner(chat, new TurnPlanValidator());

        var proposal = await planner.PlanAsync(new TurnPlanningContext(
            config,
            stage,
            scope,
            new Dictionary<string, string>(),
            message,
            DateTimeOffset.UtcNow,
            [ChatMessage.Assistant("A) Hogar\nB) Tienda\nC) Restaurante\nD) Comida rapida")]));

        proposal.Success.Should().BeTrue(string.Join("; ", proposal.Errors));
        proposal.Plan!.Facts.Should().ContainSingle();
        proposal.Plan.Facts[0].Value.GetString().Should().Be("Restaurante");
        chat.CallCount.Should().Be(1,
            "the configured selector is resolved by the engine rather than repaired by the model");
    }

    [Fact]
    public void OptionalFactRefusal_DropsInventedStringPlaceholder()
    {
        var definition = new FactSchemaEntry
        {
            Key = "delivery_reference",
            Label = "delivery_reference",
            Type = "string",
            Source = "user",
            Required = false
        };
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
                new PlannedFactClaim
                {
                    Key = definition.Key,
                    Operation = TurnPlanOperations.Set,
                    Value = JsonSerializer.SerializeToElement("no aplica"),
                    Evidence = "no"
                }
            ]
        };

        OptionalFactRefusalResolver.Resolve(plan, scope).Facts.Should().BeEmpty();
    }

    [Fact]
    public void OptionalFactRefusal_PreservesExplicitConfiguredNoOption()
    {
        var definition = new FactSchemaEntry
        {
            Key = "accepts_marketing",
            Label = "accepts_marketing",
            Type = "string",
            Source = "user",
            Required = false,
            Options =
            [
                new FactValueOption { Value = "no", Label = "No" }
            ]
        };
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.Key] = definition
            },
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase));
        var plan = new TurnPlan
        {
            Facts = [Claim(definition.Key, "no", "no")]
        };

        OptionalFactRefusalResolver.Resolve(plan, scope).Facts.Should().ContainSingle();
    }

    [Fact]
    public async Task Planner_DeterministicallyAddsPresentedSelector_WhenExtractorOmitsFact()
    {
        const string message = "la c";
        var emptyPlan = JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "order", confidence = 0.9, evidence = (string?)null },
            facts = Array.Empty<object>(),
            signals = Array.Empty<object>(),
            decision = (object?)null,
            response = new { mode = "continue", ambiguousFields = Array.Empty<string>() }
        });
        var chat = new SequenceChatClient(emptyPlan);
        var customerType = CustomerTypeOptions();
        var config = new AgentConfig
        {
            Flows = [new AgentFlowDefinition { Id = "order", Type = FlowTypes.Primary }],
            FactSchema = [customerType]
        };
        var stage = new AgentFlowStage { Id = "customer_type", AdvanceWhenFacts = ["customer_type"] };
        var scope = TurnPlanScopeBuilder.Build(config, stage, new Dictionary<string, string>());
        var planner = new LlmTurnPlanner(chat, new TurnPlanValidator());

        var proposal = await planner.PlanAsync(new TurnPlanningContext(
            config,
            stage,
            scope,
            new Dictionary<string, string>(),
            message,
            DateTimeOffset.UtcNow,
            [ChatMessage.Assistant("A) Hogar\nB) Tienda\nC) Restaurante\nD) Comida rapida")]));

        proposal.Success.Should().BeTrue(string.Join("; ", proposal.Errors));
        proposal.Plan!.Facts.Should().ContainSingle();
        proposal.Plan.Facts[0].Value.GetString().Should().Be("Restaurante");
        chat.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Planner_FailSoftRecovery_PreservesValidSignalAfterRepairStillFails()
    {
        const string message = "quiero preparar cerdo";
        var invalidPlan = JsonSerializer.Serialize(new
        {
            flowIntent = new { candidateFlow = "order", confidence = 1, evidence = (string?)null },
            facts = new object[]
            {
                new
                {
                    key = "delivery_address",
                    operation = "set",
                    value = "calle inventada",
                    evidence = "evidencia ausente"
                }
            },
            signals = new object[]
            {
                new
                {
                    type = "recipe_request",
                    value = "cerdo",
                    evidence = "preparar cerdo"
                }
            },
            decision = (object?)null,
            response = new { mode = "continue", ambiguousFields = Array.Empty<string>() }
        });
        var chat = new SequenceChatClient(invalidPlan, invalidPlan);
        var planner = new LlmTurnPlanner(chat, new TurnPlanValidator());
        var config = new AgentConfig
        {
            Flows =
            [
                new AgentFlowDefinition
                {
                    Id = "order",
                    Type = FlowTypes.Primary
                }
            ],
            FactSchema = [UserFact("delivery_address", "string")]
        };
        var stage = new AgentFlowStage
        {
            Id = "selection",
            Collect = ["delivery_address"],
            Signals =
            [
                new StageSignalDefinition
                {
                    Type = "recipe_request",
                    ValueSchema = Schema("""{"type":"string"}""")
                }
            ]
        };
        var scope = TurnPlanScopeBuilder.Build(config, stage, new Dictionary<string, string>());
        var context = new TurnPlanningContext(
            config,
            stage,
            scope,
            new Dictionary<string, string>(),
            message,
            DateTimeOffset.UtcNow,
            []);

        var proposal = await planner.PlanAsync(context);

        proposal.Success.Should().BeTrue(string.Join("; ", proposal.Errors));
        proposal.Errors.Should().BeEmpty();
        proposal.Warnings.Should().Contain(error =>
            error.Contains("delivery_address", StringComparison.OrdinalIgnoreCase));
        proposal.Plan!.Facts.Should().BeEmpty();
        proposal.Plan.Signals.Should().ContainSingle(signal => signal.Type == "recipe_request");
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

        var result = new TurnPlanValidator().Validate(plan, scope, "maÃ±ana a las diez");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("evidence", StringComparison.OrdinalIgnoreCase));
        result.Errors.Should().Contain(error => error.Contains("outside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsMissingCanonicalClaimForReferencedOptionSelector()
    {
        var definition = new FactSchemaEntry
        {
            Key = "customer_type",
            Type = "string",
            Source = "user",
            Options = [new FactValueOption { Value = "Hogar", Label = "Hogar", Selector = "A" }]
        };
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.Key] = definition
            },
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase));

        var selector = new OptionSelectorReference(definition, definition.Options.Single());
        var result = new TurnPlanValidator().Validate(
            new TurnPlan(),
            scope,
            "la a",
            selector);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("selector", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validator_RejectsCanonicalClaimThatContradictsReferencedOptionSelector()
    {
        var definition = CustomerTypeOptions();
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
                new PlannedFactClaim
                {
                    Key = "customer_type",
                    Operation = TurnPlanOperations.Set,
                    Value = JsonSerializer.SerializeToElement("ComidaRapida"),
                    Evidence = "c"
                }
            ]
        };

        var selector = new OptionSelectorReference(
            definition,
            definition.Options.Single(option =>
                option.Selector!.Equals("C", StringComparison.OrdinalIgnoreCase)));
        var result = new TurnPlanValidator().Validate(
            plan,
            scope,
            "la c",
            selector);

        result.IsValid.Should().BeFalse();
        result.Issues.Should().ContainSingle(issue =>
            issue.Code == "fact.selector_value_mismatch"
            && issue.Field == "customer_type"
            && issue.RecoveryAction == TurnPlanRecoveryAction.DropTarget);
    }

    [Fact]
    public void SelectorDetector_IgnoresSingleLetterInsideNaturalSentence()
    {
        var definition = new FactSchemaEntry
        {
            Key = "customer_type",
            Type = "string",
            Source = "user",
            Options = [new FactValueOption { Value = "Hogar", Label = "Hogar", Selector = "A" }]
        };
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.Key] = definition
            },
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase));

        OptionSelectorReferenceDetector.Find(
                scope,
                "a bueno, ademas quier preparar cerdo con tocineta en salsa")
            .Should().BeEmpty();
    }

    [Fact]
    public void Validator_AcceptsEvidenceWithMinorWordCompletionDifference()
    {
        var signal = new StageSignalDefinition
        {
            Type = "recipe_request",
            ValueSchema = Schema("""{"type":"string"}""")
        };
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase)
            {
                [signal.Type] = signal
            });
        var plan = new TurnPlan
        {
            Signals =
            [
                new PlannedSignal
                {
                    Type = "recipe_request",
                    Value = JsonSerializer.SerializeToElement("cerdo con tocineta en salsa"),
                    Evidence = "quiero preparar cerdo con tocineta en salsa"
                }
            ]
        };

        var result = new TurnPlanValidator().Validate(
            plan,
            scope,
            "a bueno, ademas quier preparar cerdo con tocineta en salsa");

        result.IsValid.Should().BeTrue(string.Join("; ", result.Errors));
    }

    [Fact]
    public void Validator_RejectsFactValueOutsideConfiguredCanonicalValues()
    {
        var definition = new FactSchemaEntry
        {
            Key = "payment_method",
            Type = "string",
            Source = "user",
            Options =
            [
                new FactValueOption { Value = "efectivo", Label = "Efectivo" },
                new FactValueOption { Value = "transferencia", Label = "Transferencia" }
            ]
        };
        var scope = new TurnPlanScope(
            new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase)
            {
                [definition.Key] = definition
            },
            new Dictionary<string, StageSignalDefinition>(StringComparer.OrdinalIgnoreCase));
        var plan = new TurnPlan
        {
            Facts = [Claim("payment_method", "cash", "pago en cash")]
        };

        var result = new TurnPlanValidator().Validate(plan, scope, "pago en cash");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("canonical", StringComparison.OrdinalIgnoreCase));
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
            "tiene 2 meses pero quiero informaciÃ³n para cuando tenga 3");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("cannot be set", StringComparison.OrdinalIgnoreCase));
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

    private static FactSchemaEntry CustomerTypeOptions() => new()
    {
        Key = "customer_type",
        Label = "perfil del cliente",
        Type = "string",
        Source = "user",
        Options =
        [
            new FactValueOption { Value = "Hogar", Label = "Hogar", Selector = "A" },
            new FactValueOption { Value = "TiendaMinimercado", Label = "Tienda", Selector = "B" },
            new FactValueOption { Value = "Restaurante", Label = "Restaurante", Selector = "C" },
            new FactValueOption { Value = "ComidaRapida", Label = "Comida rapida", Selector = "D" }
        ]
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

        public ChatCompletionOptions? CapturedOptions { get; private set; }
        public IReadOnlyList<ChatMessage>? CapturedMessages { get; private set; }

        public Task<ChatCompletionResult> CompleteAsync(
            IReadOnlyList<ChatMessage> messages,
            ChatCompletionOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            CapturedOptions = options;
            CapturedMessages = messages;
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
