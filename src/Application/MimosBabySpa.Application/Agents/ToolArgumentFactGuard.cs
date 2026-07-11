using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;

namespace MimosBabySpa.Application.Agents;

internal static class ToolArgumentFactGuard
{
    public static string? BuildUnsupportedUserFactResult(
        AgentConfig config,
        AgentFlowStage? currentStage,
        string toolName,
        string argumentsJson,
        AgentToolContext ctx,
        IReadOnlyList<IAgentTool>? scopedTools = null)
    {
        if (currentStage is null || string.IsNullOrWhiteSpace(argumentsJson))
            return null;

        if (!IsPrimaryFlowStage(config, currentStage))
            return null;

        if (!IsFactWritingTool(toolName, scopedTools))
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
                : BuildBlockedResult(toolName, unsupported.Value.FactKey, unsupported.Value.ArgumentName, unsupported.Value.Value);
        }

    }


    private static bool IsFactWritingTool(string toolName, IReadOnlyList<IAgentTool>? scopedTools) =>
        scopedTools?.FirstOrDefault(tool => tool.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase))
            ?.Capabilities.Contains(ToolCapabilities.FactWrite, StringComparer.OrdinalIgnoreCase) == true;
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
            .ToDictionary(entry => entry.Key, StringComparer.OrdinalIgnoreCase);
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

        if (IsExternallyValidatedFact(entry))
            return null;

        return IsSupportedByStateOrLatestMessage(config, ctx, entry, value)
            ? null
            : new UnsupportedArgument(factKey, "value", value);
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

            if (IsExternallyValidatedFact(entry))
                continue;

            if (!IsSupportedByStateOrLatestMessage(config, ctx, entry, value))
                return new UnsupportedArgument(entry.Key, property.Name, value);
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

    private static bool IsExternallyValidatedFact(FactSchemaEntry entry) =>
        entry.ValueSource is not null
        && (entry.ValueSource.Equals("catalog", StringComparison.OrdinalIgnoreCase)
            || entry.ValueSource.Equals("tool", StringComparison.OrdinalIgnoreCase)
            || entry.ValueSource.Equals("external", StringComparison.OrdinalIgnoreCase));

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
            && NormalizeFactSupportText(currentValue).Equals(NormalizeFactSupportText(value), StringComparison.Ordinal))
        {
            return true;
        }

        if (LatestMessageDirectlySupportsValue(ctx.LatestUserMessage, value))
            return true;

        if (LatestMessageSelectsPreviousAssistantOption(ctx, entry, value))
            return true;

        return FactValueShapeMatcher.MessageMatchesFactShape(
            config.FactSchema,
            entry.Key,
            ctx.LatestUserMessage);
    }

    private static bool LatestMessageDirectlySupportsValue(string? latestUserMessage, string value)
    {
        var normalizedMessage = NormalizeFactSupportText(latestUserMessage);
        var normalizedValue = NormalizeFactSupportText(value);
        if (string.IsNullOrWhiteSpace(normalizedMessage) || string.IsNullOrWhiteSpace(normalizedValue))
            return false;

        if (normalizedMessage.Equals(normalizedValue, StringComparison.Ordinal))
            return true;

        return SplitMeaningfulTokens(normalizedValue).Length > 0
            && ContainsNormalizedTokenSequence(normalizedMessage, normalizedValue);
    }

    private static bool ContainsNormalizedTokenSequence(string normalizedMessage, string normalizedValue)
    {
        var messageTokens = SplitMeaningfulTokens(normalizedMessage);
        var valueTokens = SplitMeaningfulTokens(normalizedValue);
        if (messageTokens.Length == 0 || valueTokens.Length == 0 || valueTokens.Length > messageTokens.Length)
            return false;

        for (var start = 0; start <= messageTokens.Length - valueTokens.Length; start++)
        {
            var matches = true;
            for (var offset = 0; offset < valueTokens.Length; offset++)
            {
                if (!messageTokens[start + offset].Equals(valueTokens[offset], StringComparison.Ordinal))
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
                return true;
        }

        return false;
    }
    private static string NormalizeFactSupportText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        value = Regex.Replace(value.Trim(), @"(?<=[a-záéíóúñ])(?=[A-ZÁÉÍÓÚÑ])", " ", RegexOptions.CultureInvariant);
        var decomposed = value.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = decomposed
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)
                != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray();

        return string.Join(' ', new string(chars)
            .Normalize(System.Text.NormalizationForm.FormC)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool LatestMessageSelectsPreviousAssistantOption(AgentToolContext ctx, FactSchemaEntry entry, string value)
    {
        var selectedOption = ExtractSelectedOptionToken(ctx.LatestUserMessage);
        if (string.IsNullOrWhiteSpace(selectedOption))
            return false;

        var assistantMessage = ctx.Conversation.Messages
            .Where(message => IsBotSender(message.Sender) && !string.IsNullOrWhiteSpace(message.MessageText))
            .OrderByDescending(message => message.Timestamp)
            .Select(message => message.MessageText)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(assistantMessage))
            return false;

        return ExtractAssistantOptions(assistantMessage)
            .Any(option => option.Token.Equals(selectedOption, StringComparison.OrdinalIgnoreCase)
                && OptionTextMatchesValue(entry, option.Text, value));
    }

    private static string? ExtractSelectedOptionToken(string? latestUserMessage)
    {
        var normalized = NormalizeFactSupportText(latestUserMessage);
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        var match = Regex.Match(
            normalized,
            @"^(?:la|el|opcion|opción|letra|numero|número)?\s*(?<token>[a-z]|\d{1,2})\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["token"].Value : null;
    }

    private static IEnumerable<(string Token, string Text)> ExtractAssistantOptions(string assistantMessage)
    {
        foreach (Match match in Regex.Matches(
            assistantMessage,
            @"(?<!\p{L})(?<token>[A-Z]|\d{1,2})\s*[\.)\]:-]\s*(?<text>[^\r\n]*?)(?=(?:\s+(?:[A-Z]|\d{1,2})\s*[\.)\]:-])|$)",
            RegexOptions.CultureInvariant | RegexOptions.Multiline))
        {
            var token = match.Groups["token"].Value;
            var text = match.Groups["text"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(token) && !string.IsNullOrWhiteSpace(text))
                yield return (token, text);
        }
    }

    private static bool OptionTextMatchesValue(FactSchemaEntry entry, string optionText, string value)
    {
        var normalizedOption = NormalizeFactSupportText(optionText);
        var normalizedValue = NormalizeFactSupportText(value);
        if (string.IsNullOrWhiteSpace(normalizedOption) || string.IsNullOrWhiteSpace(normalizedValue))
            return false;

        if (normalizedOption.Equals(normalizedValue, StringComparison.Ordinal)
            || ContainsNormalizedTokenSequence(normalizedOption, normalizedValue))
        {
            return true;
        }

        var valueTokens = SplitMeaningfulTokens(normalizedValue);
        if (valueTokens.Length > 0 && valueTokens.All(token => ContainsNormalizedTokenSequence(normalizedOption, token)))
            return true;

        return entry.Aliases
            .Where(alias => !string.IsNullOrWhiteSpace(alias))
            .Select(NormalizeFactSupportText)
            .Any(alias => ContainsNormalizedTokenSequence(normalizedValue, alias)
                && ContainsNormalizedTokenSequence(normalizedOption, alias));
    }

    private static string[] SplitMeaningfulTokens(string normalizedValue) =>
        normalizedValue
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2)
            .ToArray();

    private static bool IsBotSender(string sender) =>
        sender.Equals("bot", StringComparison.OrdinalIgnoreCase)
        || sender.Equals("assistant", StringComparison.OrdinalIgnoreCase);

    private static string BuildBlockedResult(string toolName, string factKey, string argumentName, string value) =>
        ToolResultHelper.ErrorWithLlm(
            "tool_argument_requires_user_fact_capture",
            "A tool argument appears to provide a user-scoped fact that is not present in state and was not supported by the latest user message.",
            new
            {
                next_action = "recover_fact_capture_before_continuing",
                tool = toolName,
                fact = factKey,
                argument = argumentName,
                rejected_value = value,
                fact_was_saved = false,
                recovery = new
                {
                    retry_tool_only_when_supported = true,
                    ask_user_when_unsupported = true,
                    do_not_claim_fact_was_saved = true,
                    do_not_advance_flow = true,
                    user_prompt_guidance = "Ask a short clarification for the blocked fact, or retry set_fact only if the latest user message or the immediately previous assistant option supports the value."
                }
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

    private readonly record struct UnsupportedArgument(string FactKey, string ArgumentName, string Value);
}
