using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Tools.Impl;

namespace MimosBabySpa.Application.Agents;

internal static class ToolArgumentFactGuard
{
    public static string? BuildUnsupportedUserFactResult(
        AgentConfig config,
        AgentFlowStage? currentStage,
        string toolName,
        string argumentsJson,
        AgentToolContext ctx)
    {
        if (currentStage is null || string.IsNullOrWhiteSpace(argumentsJson))
            return null;

        if (!IsPrimaryFlowStage(config, currentStage))
            return null;

        var protectedFacts = ResolveProtectedFacts(config, currentStage);
        if (protectedFacts.Count == 0)
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var unsupported = toolName.Equals("set_fact", StringComparison.OrdinalIgnoreCase)
                ? FindUnsupportedSetFactArgument(config, protectedFacts, document.RootElement, ctx)
                : FindUnsupportedToolArgument(config, protectedFacts, document.RootElement, ctx);

            return unsupported is null
                ? null
                : BuildBlockedResult(toolName, unsupported.Value.FactKey, unsupported.Value.ArgumentName);
        }

    }

    private static bool IsPrimaryFlowStage(AgentConfig config, AgentFlowStage currentStage)
    {
        var matchingFlows = AgentFlowCatalog.EffectiveFlows(config)
            .Where(flow => flow.Stages.Any(stage => stage.Id.Equals(currentStage.Id, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return matchingFlows.Length == 0
            || matchingFlows.Any(AgentFlowCatalog.IsPrimary);
    }

    private static IReadOnlyDictionary<string, FactSchemaEntry> ResolveProtectedFacts(
        AgentConfig config,
        AgentFlowStage currentStage)
    {
        var stageFactKeys = currentStage.Collect
            .Concat(currentStage.AdvanceWhenFacts)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (stageFactKeys.Count == 0)
            return new Dictionary<string, FactSchemaEntry>(StringComparer.OrdinalIgnoreCase);

        return config.FactSchema
            .Where(entry => stageFactKeys.Contains(entry.Key))
            .Where(entry => entry.Source.Equals("user", StringComparison.OrdinalIgnoreCase))
            .Where(IsStructuredUserFact)
            .ToDictionary(entry => entry.Key, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsStructuredUserFact(FactSchemaEntry entry)
    {
        var type = entry.Type.Trim().ToLowerInvariant();
        return type is "date" or "time" or "phone" or "email" or "number"
               || (type == "string" && entry.Aliases.Count > 0);
    }

    private static UnsupportedArgument? FindUnsupportedSetFactArgument(
        AgentConfig config,
        IReadOnlyDictionary<string, FactSchemaEntry> protectedFacts,
        JsonElement arguments,
        AgentToolContext ctx)
    {
        if (!TryReadScalar(arguments, "key", out var rawKey)
            || !TryReadScalar(arguments, "value", out var value))
        {
            return null;
        }

        var roleIndex = new FactRoleIndex(config.FactSchema);
        var factKey = roleIndex.NormalizeKey(rawKey.Trim());
        if (!protectedFacts.TryGetValue(factKey, out var entry))
            return null;

        return IsSupportedByStateOrLatestMessage(config, ctx, entry, value)
            ? null
            : new UnsupportedArgument(factKey, "value");
    }

    private static UnsupportedArgument? FindUnsupportedToolArgument(
        AgentConfig config,
        IReadOnlyDictionary<string, FactSchemaEntry> protectedFacts,
        JsonElement arguments,
        AgentToolContext ctx)
    {
        foreach (var property in arguments.EnumerateObject())
        {
            if (!TryReadScalar(property.Value, out var value))
                continue;

            var entry = ResolveFactForArgument(config, protectedFacts, property.Name);
            if (entry is null)
                continue;

            if (!IsSupportedByStateOrLatestMessage(config, ctx, entry, value))
                return new UnsupportedArgument(entry.Key, property.Name);
        }

        return null;
    }

    private static FactSchemaEntry? ResolveFactForArgument(
        AgentConfig config,
        IReadOnlyDictionary<string, FactSchemaEntry> protectedFacts,
        string argumentName)
    {
        var roleIndex = new FactRoleIndex(config.FactSchema);
        var normalized = roleIndex.NormalizeKey(argumentName);
        if (protectedFacts.TryGetValue(normalized, out var byKey))
            return byKey;

        return protectedFacts.Values.FirstOrDefault(entry =>
            entry.Aliases.Any(alias => alias.Equals(argumentName, StringComparison.OrdinalIgnoreCase))
            || RoleTailEquals(entry.Role, argumentName));
    }

    private static bool RoleTailEquals(string? role, string argumentName)
    {
        if (string.IsNullOrWhiteSpace(role))
            return false;

        var tail = role.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .LastOrDefault();
        return tail?.Equals(argumentName, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSupportedByStateOrLatestMessage(
        AgentConfig config,
        AgentToolContext ctx,
        FactSchemaEntry entry,
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return true;

        if (ctx.Facts.TryGetValue(entry.Key, out var currentValue)
            && !string.IsNullOrWhiteSpace(currentValue)
            && currentValue.Trim().Equals(value.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(ctx.LatestUserMessage)
            && ctx.LatestUserMessage.Contains(value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return FactValueShapeMatcher.MessageMatchesFactShape(
            config.FactSchema,
            entry.Key,
            ctx.LatestUserMessage);
    }

    private static string BuildBlockedResult(string toolName, string factKey, string argumentName) =>
        ToolResultHelper.ErrorWithLlm(
            "tool_argument_requires_user_fact_capture",
            "A tool argument appears to provide a user-scoped fact that is not present in state and was not supported by the latest user message.",
            new
            {
                next_action = "collect_or_capture_user_fact_then_retry",
                tool = toolName,
                fact = factKey,
                argument = argumentName
            },
            recoverable: true);

    private static bool TryReadScalar(JsonElement arguments, string propertyName, out string value)
    {
        value = string.Empty;
        return arguments.TryGetProperty(propertyName, out var element)
            && TryReadScalar(element, out value);
    }

    private static bool TryReadScalar(JsonElement element, out string value)
    {
        value = element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
            JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
            _ => string.Empty
        };

        return element.ValueKind is JsonValueKind.String
            or JsonValueKind.Number
            or JsonValueKind.True
            or JsonValueKind.False;
    }

    private readonly record struct UnsupportedArgument(string FactKey, string ArgumentName);
}



