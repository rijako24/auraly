using System.Collections;
using System.Reflection;

namespace MimosBabySpa.Application.Agents.Runtime;

public sealed class FlowPolicyEngine : IFlowPolicyEngine
{
    public FlowRuntimeDecision Decide(
        AgentConfig config,
        AgentToolContext session,
        FlowRuntimeState state,
        IReadOnlyList<TurnEvent> events)
    {
        var enabledGlobalActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var action in config.GlobalActions)
        {
            if (IsGlobalActionEnabled(action, session))
                enabledGlobalActions.Add(action.Id);
        }

        return new FlowRuntimeDecision(
            state,
            events,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            enabledGlobalActions,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsGlobalActionEnabled(Configuration.AgentGlobalAction action, AgentToolContext session) =>
        action.RuntimeWhenAny.Count == 0
        || action.RuntimeWhenAny.Any(expression => EvaluateExpression(expression, session));

    private static bool EvaluateExpression(string expression, AgentToolContext session)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;

        return expression.Split("||", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(orPart => orPart.Split("&&", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(atom => EvaluateAtom(atom, session)));
    }

    private static bool EvaluateAtom(string atom, AgentToolContext session)
    {
        if (atom.Equals("always", StringComparison.OrdinalIgnoreCase))
            return true;

        const string factPrefix = "fact:";
        if (atom.StartsWith(factPrefix, StringComparison.OrdinalIgnoreCase))
            return HasFact(session, atom[factPrefix.Length..]);

        const string contextPrefix = "context:";
        if (!atom.StartsWith(contextPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return EvaluateContextAtom(session, atom[contextPrefix.Length..]);
    }

    private static bool EvaluateContextAtom(AgentToolContext session, string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.EndsWith(".any", StringComparison.OrdinalIgnoreCase))
        {
            var value = ResolvePath(session, trimmed[..^4]);
            return value is IEnumerable enumerable && value is not string && enumerable.Cast<object?>().Any();
        }

        if (trimmed.Contains('=', StringComparison.Ordinal))
        {
            var parts = trimmed.Split('=', 2, StringSplitOptions.TrimEntries);
            var actual = ResolvePath(session, parts[0]);
            var expected = parts[1];

            if (expected.Equals("null", StringComparison.OrdinalIgnoreCase))
                return actual is null;

            return actual is not null
                   && string.Equals(Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture), expected, StringComparison.OrdinalIgnoreCase);
        }

        var resolved = ResolvePath(session, trimmed);
        return resolved switch
        {
            null => false,
            string text => !string.IsNullOrWhiteSpace(text),
            bool flag => flag,
            IEnumerable enumerable => enumerable.Cast<object?>().Any(),
            _ => true
        };
    }

    private static object? ResolvePath(object source, string path)
    {
        object? current = source;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (current is null)
                return null;

            var property = current.GetType().GetProperty(segment, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (property is null)
                return null;

            current = property.GetValue(current);
        }

        return current;
    }

    private static bool HasFact(AgentToolContext session, string key) =>
        !string.IsNullOrWhiteSpace(key)
        && session.Facts.TryGetValue(key.Trim(), out var value)
        && !string.IsNullOrWhiteSpace(value);
}
