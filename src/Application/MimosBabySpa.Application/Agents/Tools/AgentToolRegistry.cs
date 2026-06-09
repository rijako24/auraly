using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Agents.Tools;

/// <summary>
/// Registro de todas las tools disponibles en el sistema.
/// Filtra por la lista de nombres habilitados del AgentConfig antes de exponerlas al LLM.
/// </summary>
public sealed class AgentToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _allTools;
    private readonly ILogger<AgentToolRegistry> _logger;

    public AgentToolRegistry(
        IEnumerable<IAgentTool> tools,
        ILogger<AgentToolRegistry> logger)
    {
        _allTools = tools.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
        _logger.LogInformation("AgentToolRegistry loaded {Count} tools: {Names}",
            _allTools.Count, string.Join(", ", _allTools.Keys));
    }

    /// <summary>
    /// Retorna las tools filtradas por los nombres habilitados del agente.
    /// Si enabledNames está vacío, retorna todas (útil en desarrollo).
    /// </summary>
    public IReadOnlyList<IAgentTool> GetToolsForAgent(IReadOnlyList<string> enabledNames)
    {
        if (enabledNames.Count == 0)
            return _allTools.Values.ToList();

        return enabledNames
            .Where(name => _allTools.ContainsKey(name))
            .Select(name => _allTools[name])
            .ToList();
    }

    /// <summary>
    /// Resuelve una tool por nombre para ejecutarla tras recibir un tool_call del LLM.
    /// </summary>
    public IAgentTool? Resolve(string name) =>
        _allTools.TryGetValue(name, out var tool) ? tool : null;

    /// <summary>
    /// Resuelve una tool por capacidad semantica estable.
    /// Usado por el motor cuando necesita una accion interna sin depender del nombre OpenAI.
    /// </summary>
    public IAgentTool? ResolveByCapability(string capability) =>
        _allTools.Values.FirstOrDefault(t =>
            t.Capabilities.Contains(capability, StringComparer.OrdinalIgnoreCase));
}
