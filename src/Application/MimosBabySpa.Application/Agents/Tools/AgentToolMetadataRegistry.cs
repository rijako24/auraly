using System.Reflection;

namespace MimosBabySpa.Application.Agents.Tools;

public sealed class AgentToolMetadataRegistry
{
    private readonly IReadOnlyDictionary<string, AgentToolMetadata> _tools;

    public AgentToolMetadataRegistry()
    {
        _tools = typeof(IAgentTool).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IAgentTool).IsAssignableFrom(type))
            .Select(type => type.GetCustomAttribute<AgentToolMetadataAttribute>())
            .Where(attribute => attribute is not null && !string.IsNullOrWhiteSpace(attribute.Name))
            .Select(attribute => new AgentToolMetadata(
                attribute!.Name,
                attribute.Capabilities,
                attribute.RequiredTemplateIds))
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
