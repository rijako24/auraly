using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Services;

/// <summary>
/// Resolves Handlebars-style template variables against FlowTurnContext.
/// Supported tokens:
///   {{variables.KEY}}              - variable value
///   {{flags.KEY}}                  - flag value (true/false)
///   {{action_results.NODE.FIELD}}  - action result field
///   {{previous_session.KEY}}       - persistent variable from previous session
///   {{variables_group:GROUP}}      - comma-separated list of key=value for a variable group
///   {{collected_data}}             - all non-null, showInSummary=true variables
///   {{missing_data}}               - required variables that are null
///   {{validation_errors}}          - validation errors from current turn
///   {{user_message}}               - raw user message
///
///   Runtime tokens (system clock — not domain-specific):
///   {{runtime.today}}              - current date as yyyy-MM-dd
///   {{runtime.tomorrow}}           - tomorrow as yyyy-MM-dd
///   {{runtime.day_after_tomorrow}} - day after tomorrow as yyyy-MM-dd
///   {{runtime.current_time}}       - current time as HH:mm
///   {{runtime.day_of_week}}        - current day name (e.g. "miércoles")
/// </summary>
public class TemplateResolver
{
    private static readonly Regex TokenRegex = new(
        @"\{\{([^}]+)\}\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Resolve(string template, FlowTurnContext ctx)
    {
        if (string.IsNullOrEmpty(template)) return template;

        return TokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value.Trim();
            return ResolveToken(token, ctx) ?? match.Value;
        });
    }

    /// <summary>
    /// Resolves a template that only contains runtime.* tokens (no FlowTurnContext needed).
    /// Used by FlowExtractionService to resolve ExtractionInstructions before the turn context is built.
    /// </summary>
    /// <summary>
    /// Resolves a template that only contains runtime.* tokens (no FlowTurnContext needed).
    /// Used by FlowExtractionService to resolve ExtractionInstructions before the turn context is built.
    /// </summary>
    /// <param name="template">Template string with {{runtime.*}} tokens.</param>
    /// <param name="culture">Optional culture code (e.g. "es-ES"). Defaults to "es-ES".</param>
    public string ResolveStandalone(string template, string culture = "es-ES")
    {
        if (string.IsNullOrEmpty(template)) return template;

        return TokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value.Trim();
            if (token.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase))
                return ResolveRuntimeToken(token["runtime.".Length..], culture) ?? match.Value;
            return match.Value; // non-runtime tokens require ctx — leave as-is
        });
    }

    private string? ResolveToken(string token, FlowTurnContext ctx)
    {
        var state = ctx.State;
        var flow = ctx.FlowDefinition;

        if (token.StartsWith("variables.", StringComparison.OrdinalIgnoreCase))
        {
            var key = token["variables.".Length..];
            return state.GetVariable(key) ?? string.Empty;
        }

        if (token.StartsWith("flags.", StringComparison.OrdinalIgnoreCase))
        {
            var key = token["flags.".Length..];
            return state.GetFlag(key).ToString().ToLowerInvariant();
        }

        if (token.StartsWith("action_results.", StringComparison.OrdinalIgnoreCase))
        {
            var parts = token.Split('.', 3);
            if (parts.Length == 3 &&
                state.ActionResults.TryGetValue(parts[1], out var actionData) &&
                actionData.TryGetValue(parts[2], out var fieldValue))
            {
                return fieldValue?.ToString() ?? string.Empty;
            }
            return string.Empty;
        }

        if (token.StartsWith("previous_session.", StringComparison.OrdinalIgnoreCase))
        {
            var key = token["previous_session.".Length..];
            return state.PreviousSession?.PersistentVariables.GetValueOrDefault(key) ?? string.Empty;
        }

        if (token.StartsWith("variables_group:", StringComparison.OrdinalIgnoreCase))
        {
            var group = token["variables_group:".Length..];
            var grouped = state.GetVariablesByGroup(group, flow);
            var parts = grouped
                .Where(kv => kv.Value != null)
                .Select(kv => $"{kv.Key}={kv.Value}");
            return string.Join(", ", parts);
        }

        if (token.StartsWith("runtime.", StringComparison.OrdinalIgnoreCase))
        {
            var culture = ctx.FlowDefinition.EngineSettings.DateTimeCulture;
            return ResolveRuntimeToken(token["runtime.".Length..], culture);
        }

        return token switch
        {
            "collected_data" => BuildCollectedData(state, flow),
            "missing_data" => BuildMissingData(state, flow),
            "validation_errors" => BuildValidationErrors(ctx.ValidationErrors),
            "user_message" => ctx.UserMessage,
            _ => null
        };
    }

    /// <summary>
    /// Resolves runtime tokens from the system clock.
    /// These are generic (not domain-specific) — any flow can use them.
    /// </summary>
    /// <param name="tokenName">Token name after "runtime." prefix (e.g. "today", "day_of_week").</param>
    /// <param name="culture">Culture code for locale-sensitive tokens like day_of_week.</param>
    private static string? ResolveRuntimeToken(string tokenName, string culture = "es-ES")
    {
        var now = DateTime.Now;
        var cultureInfo = GetCultureInfo(culture);

        return tokenName.ToLowerInvariant() switch
        {
            "today"              => now.ToString("yyyy-MM-dd"),
            "tomorrow"           => now.AddDays(1).ToString("yyyy-MM-dd"),
            "day_after_tomorrow" => now.AddDays(2).ToString("yyyy-MM-dd"),
            "current_time"       => now.ToString("HH:mm"),
            "day_of_week"        => now.ToString("dddd", cultureInfo),
            _                    => null
        };
    }

    private static System.Globalization.CultureInfo GetCultureInfo(string culture)
    {
        try
        {
            return new System.Globalization.CultureInfo(culture);
        }
        catch
        {
            return new System.Globalization.CultureInfo("es-ES");
        }
    }

    private static string BuildCollectedData(FlowExecutionState state, FlowDefinitionDocument flow)
    {
        var sb = new StringBuilder();
        // showInSummary alone controls presentation; isSystemManaged only affects extraction (FlowExtractionService).
        foreach (var variable in flow.Variables
            .Where(v => v.ShowInSummary)
            .OrderBy(v => v.DisplayOrder))
        {
            var value = state.GetVariable(variable.Key);
            if (value != null)
                sb.AppendLine($"- {variable.Label}: {value}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildMissingData(FlowExecutionState state, FlowDefinitionDocument flow)
    {
        var missing = flow.Variables
            .Where(v => v.Required && !v.IsSystemManaged && state.GetVariable(v.Key) == null)
            .OrderBy(v => v.DisplayOrder)
            .Select(v => v.Label);
        return string.Join(", ", missing);
    }

    private static string BuildValidationErrors(List<FlowValidationError> errors)
    {
        if (errors.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var e in errors)
            sb.AppendLine($"⚠️ \"{e.ProvidedValue}\" no es válido para {e.VariableKey}: {e.ErrorMessage}");
        return sb.ToString().TrimEnd();
    }
}
