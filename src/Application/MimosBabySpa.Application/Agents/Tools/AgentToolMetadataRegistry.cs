using System.Reflection;
using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Tools;

public sealed class AgentToolMetadataRegistry
{
    private readonly IReadOnlyDictionary<string, AgentToolMetadata> _tools;

    public AgentToolMetadataRegistry()
    {
        _tools = typeof(IAgentTool).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IAgentTool).IsAssignableFrom(type))
            .Select(type => new
            {
                Tool = type.GetCustomAttribute<AgentToolMetadataAttribute>(),
                Type = type
            })
            .Where(item => item.Tool is not null && !string.IsNullOrWhiteSpace(item.Tool.Name))
            .Select(item => new AgentToolMetadata(
                item.Tool!.Name,
                item.Tool.Capabilities,
                item.Tool.RequiredTemplateIds))
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<AgentToolMetadata> GetTools(IEnumerable<string> toolNames)
    {
        foreach (var name in toolNames)
        {
            if (_tools.TryGetValue(name, out var metadata))
                yield return metadata;
        }
    }
}

public sealed record AgentToolMetadata(
    string Name,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> RequiredTemplateIds);
