namespace MimosBabySpa.Application.Agents.Configuration;

/// <summary>
/// Accion transversal declarada por tenant. Sus tools pueden usarse aunque
/// la etapa activa tenga una whitelist distinta.
/// </summary>
public sealed class AgentGlobalAction
{
    public string Id { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string Goal { get; init; } = string.Empty;
    public string? Hint { get; init; }
    public IReadOnlyList<string> AllowedTools { get; init; } = [];}
