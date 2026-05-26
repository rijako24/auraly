using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Facts;

/// <summary>
/// Payload de ejecución de una tool: argumentos del LLM + facts resueltos por rol + contexto de sesión.
/// </summary>
public sealed class ToolInvocation
{
    public required JsonElement Arguments { get; init; }
    public required IReadOnlyDictionary<string, string> ResolvedFacts { get; init; }
    public required AgentToolContext Context { get; init; }

    public string? Get(string role) =>
        ResolvedFacts.TryGetValue(role, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;

    public string GetRequired(string role) =>
        Get(role) ?? throw new InvalidOperationException(
            $"Required role '{role}' is not resolved for this tool invocation.");
}
