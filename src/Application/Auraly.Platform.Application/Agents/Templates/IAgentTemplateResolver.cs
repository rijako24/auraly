namespace Auraly.Platform.Application.Agents.Templates;

public interface IAgentTemplateResolver
{
    string? Resolve(AgentConfig config, string templateId);
}
