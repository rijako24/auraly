using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Tools;

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

    public IReadOnlyList<IAgentTool> GetToolsForAgent(AgentConfig config) =>
        GetToolsForAgent(config.EnabledToolNames, config.CapabilityPacks);

    public IReadOnlyList<IAgentTool> GetToolsForAgent(
        IReadOnlyList<string> enabledNames,
        IReadOnlyList<string> capabilityPacks)
    {
        if (enabledNames.Count == 0)
            return [];

        return enabledNames
            .Where(name => _allTools.ContainsKey(name))
            .Select(name => _allTools[name])
            .Where(tool => IsToolAllowedForPacks(tool, capabilityPacks))
            .ToList();
    }

    public IAgentTool? Resolve(string name) =>
        _allTools.TryGetValue(name, out var tool) ? tool : null;

    public IReadOnlyList<IAgentTool> GetToolsForStage(AgentConfig config, AgentFlowStage? stage)
    {
        var tools = GetToolsForAgent(config);
        if (stage is null || stage.AllowedTools.Count == 0)
            return tools;

        var allowed = new HashSet<string>(stage.AllowedTools, StringComparer.OrdinalIgnoreCase);
        return tools.Where(tool => allowed.Contains(tool.Name)).ToList();
    }

    private static bool IsToolAllowedForPacks(IAgentTool tool, IReadOnlyList<string> capabilityPacks)
    {
        if (string.IsNullOrWhiteSpace(tool.PackId))
            return true;

        return capabilityPacks.Contains(tool.PackId, StringComparer.OrdinalIgnoreCase);
    }
}
