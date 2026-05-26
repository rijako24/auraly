using MimosBabySpa.Application.Agents.Packs;
using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Templates;

public sealed class AgentTemplateResolver : IAgentTemplateResolver
{
    private readonly IToolCapabilityPackRegistry _packRegistry;

    public AgentTemplateResolver(IToolCapabilityPackRegistry packRegistry)
    {
        _packRegistry = packRegistry;
    }

    public string? Resolve(AgentConfig config, string templateId, IReadOnlyList<IAgentTool> enabledTools)
    {
        if (config.Templates.TryGetValue(templateId, out var fromConfig) && !string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig.Trim();

        var fromPack = _packRegistry.ResolveTemplate(config.CapabilityPacks, templateId);
        if (!string.IsNullOrWhiteSpace(fromPack))
            return fromPack;

        var owner = enabledTools.FirstOrDefault(t =>
            string.Equals(t.DefaultTemplateId, templateId, StringComparison.OrdinalIgnoreCase));

        return owner?.DefaultTemplate?.Trim();
    }
}
