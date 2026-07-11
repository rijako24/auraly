using System.Text.Json;
using MimosBabySpa.Application.LLM;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Planning;

public static class TurnPlanJsonSchemaBuilder
{
    public const string SchemaName = "turn_plan";

    public static ChatStructuredOutput Build(TurnPlanScope scope)
    {
        var flowIds = scope.Flows.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        var factKeys = scope.Facts.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray();
        var signals = scope.Signals.Values.OrderBy(signal => signal.Type, StringComparer.OrdinalIgnoreCase).ToArray();

        var schema = new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new Dictionary<string, object?>
            {
                ["flowIntent"] = FlowIntentSchema(flowIds),
                ["facts"] = ArraySchema(FactSchema(factKeys)),
                ["signals"] = ArraySchema(SignalSchema(signals)),
                ["decision"] = DecisionSchema(),
                ["response"] = ResponseSchema(factKeys.Concat(signals.Select(signal => signal.Type)).Concat(new[] { "flowIntent", "decision" }).ToArray())
            },
            ["required"] = new[] { "flowIntent", "facts", "signals", "decision", "response" }
        };

        return new ChatStructuredOutput
        {
            Name = SchemaName,
            Description = "Complete semantic plan for one customer turn.",
            JsonSchema = JsonSerializer.Serialize(schema),
            Strict = true
        };
    }

    private static Dictionary<string, object?> FlowIntentSchema(IReadOnlyList<string> flowIds) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new Dictionary<string, object?>
        {
            ["candidateFlow"] = StringSchemaWithOptionalEnum(flowIds),
            ["confidence"] = new Dictionary<string, object?> { ["type"] = "number" },
            ["evidence"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" } }
        },
        ["required"] = new[] { "candidateFlow", "confidence", "evidence" }
    };

    private static Dictionary<string, object?> ArraySchema(object itemSchema) => new()
    {
        ["type"] = "array",
        ["items"] = itemSchema
    };

    private static Dictionary<string, object?> FactSchema(IReadOnlyList<string> keys) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new Dictionary<string, object?>
        {
            ["key"] = StringSchemaWithOptionalEnum(keys),
            ["operation"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["enum"] = new[] { TurnPlanOperations.Set, TurnPlanOperations.Clear }
            },
            ["value"] = new Dictionary<string, object?>
            {
                ["anyOf"] = new object[]
                {
                    new Dictionary<string, object?> { ["type"] = "string" },
                    new Dictionary<string, object?> { ["type"] = "number" },
                    new Dictionary<string, object?> { ["type"] = "boolean" },
                    new Dictionary<string, object?> { ["type"] = "null" }
                }
            },
            ["evidence"] = EvidenceSchema()
        },
        ["required"] = new[] { "key", "operation", "value", "evidence" }
    };

    private static Dictionary<string, object?> SignalSchema(IReadOnlyList<StageSignalDefinition> signals)
    {
        if (signals.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new Dictionary<string, object?>
                {
                    ["type"] = new Dictionary<string, object?> { ["type"] = "string" },
                    ["value"] = new Dictionary<string, object?> { ["type"] = "null" },
                    ["evidence"] = EvidenceSchema()
                },
                ["required"] = new[] { "type", "value", "evidence" }
            };
        }

        return new Dictionary<string, object?>
        {
            ["anyOf"] = signals.Select(SignalBranch).ToArray()
        };
    }

    private static Dictionary<string, object?> SignalBranch(StageSignalDefinition signal) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new Dictionary<string, object?>
        {
            ["type"] = new Dictionary<string, object?> { ["type"] = "string", ["enum"] = new[] { signal.Type } },
            ["value"] = signal.ValueSchema.ValueKind == JsonValueKind.Object
                ? signal.ValueSchema.Clone()
                : new Dictionary<string, object?> { ["type"] = "string" },
            ["evidence"] = EvidenceSchema()
        },
        ["required"] = new[] { "type", "value", "evidence" }
    };
    private static Dictionary<string, object?> DecisionSchema() => new()
    {
        ["type"] = new[] { "object", "null" },
        ["additionalProperties"] = false,
        ["properties"] = new Dictionary<string, object?>
        {
            ["type"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["value"] = new Dictionary<string, object?> { ["type"] = "string" },
            ["evidence"] = EvidenceSchema(),
            ["artifactId"] = new Dictionary<string, object?> { ["type"] = new[] { "string", "null" } },
            ["requestRevision"] = new Dictionary<string, object?> { ["type"] = new[] { "integer", "null" } }
        },
        ["required"] = new[] { "type", "value", "evidence", "artifactId", "requestRevision" }
    };

    private static Dictionary<string, object?> ResponseSchema(IReadOnlyList<string> ambiguousFieldOptions) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["properties"] = new Dictionary<string, object?>
        {
            ["mode"] = new Dictionary<string, object?>
            {
                ["type"] = "string",
                ["enum"] = new[] { "continue", "ask_clarification" }
            },
            ["ambiguousFields"] = ArraySchema(StringSchemaWithOptionalEnum(ambiguousFieldOptions))
        },
        ["required"] = new[] { "mode", "ambiguousFields" }
    };

    private static Dictionary<string, object?> StringSchemaWithOptionalEnum(IReadOnlyList<string> values)
    {
        var schema = new Dictionary<string, object?> { ["type"] = "string" };
        if (values.Count > 0)
            schema["enum"] = values;
        return schema;
    }

    private static Dictionary<string, object?> EvidenceSchema() => new()
    {
        ["type"] = "string",
        ["description"] = "Exact supporting text copied from the latest customer message."
    };
}