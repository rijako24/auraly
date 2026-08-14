using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Operations.Conversation;

/// <summary>
/// Reads only tenant-configured facts that are explicitly safe to disclose to the customer.
/// The operation is read-only and never accesses system facts or arbitrary context keys.
/// </summary>
public sealed class GetKnownFactsOperation : IAgentOperation
{
    private const string InputSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "fact_keys": {
              "type": "array",
              "items": { "type": "string" },
              "minItems": 1,
              "maxItems": 10
            }
          },
          "required": ["fact_keys"]
        }
        """;

    public OperationDescriptor Descriptor { get; } = new(
        "conversation.get_known_facts",
        InputSchema,
        ["known_facts.found", "known_facts.not_found", "known_facts.forbidden"],
        [], [], []);

    public Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var requestedKeys = ReadKeys(input);
        var definitions = context.Config.FactSchema
            .Where(definition => definition.CustomerReadable)
            .ToDictionary(definition => definition.Key, StringComparer.OrdinalIgnoreCase);

        var forbidden = requestedKeys
            .Where(key => !definitions.ContainsKey(key))
            .ToArray();
        if (forbidden.Length > 0)
            return Task.FromResult(OperationOutcome.Fail(
                "known_facts.forbidden",
                "One or more requested facts are not configured for customer disclosure.",
                false));

        var facts = requestedKeys
            .Where(key => context.Facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            .Select(key => new
            {
                key,
                label = string.IsNullOrWhiteSpace(definitions[key].Label) ? key : definitions[key].Label,
                value = context.Facts[key]
            })
            .ToList();
        var missing = requestedKeys
            .Where(key => facts.All(fact => !string.Equals(fact.key, key, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return Task.FromResult(OperationOutcome.Ok(
            facts.Count == 0 ? "known_facts.not_found" : "known_facts.found",
            new { facts, missing_fact_keys = missing }));
    }

    private static IReadOnlyList<string> ReadKeys(JsonElement input)
    {
        if (!input.TryGetProperty("fact_keys", out var element) || element.ValueKind != JsonValueKind.Array)
            return [];

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToArray();
    }
}
