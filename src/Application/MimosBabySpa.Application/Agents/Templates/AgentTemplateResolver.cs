using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Templates;

public sealed class AgentTemplateResolver : IAgentTemplateResolver
{
    public string? Resolve(AgentConfig config, string templateId, IReadOnlyList<IAgentTool> enabledTools)
    {
        if (config.Templates.TryGetValue(templateId, out var fromConfig) && !string.IsNullOrWhiteSpace(fromConfig))
            return fromConfig.Trim();

        var owner = enabledTools.FirstOrDefault(t =>
            string.Equals(t.DefaultTemplateId, templateId, StringComparison.OrdinalIgnoreCase));

        return owner?.DefaultTemplate?.Trim();
    }
}
