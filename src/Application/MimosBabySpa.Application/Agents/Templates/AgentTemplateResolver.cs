namespace MimosBabySpa.Application.Agents.Templates;

public sealed class AgentTemplateResolver : IAgentTemplateResolver
{
    public string? Resolve(AgentConfig config, string templateId)
    {
        return config.Templates.TryGetValue(templateId, out var template)
            && !string.IsNullOrWhiteSpace(template)
            ? template.Trim()
            : null;
    }
}
