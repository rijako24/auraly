using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Templates;

public interface IAgentTemplateResolver
{
    string? Resolve(AgentConfig config, string templateId, IReadOnlyList<IAgentTool> enabledTools);
}
