using MimosBabySpa.Application.Agents.Tools;

namespace MimosBabySpa.Application.Agents.Facts;

public interface IRoleFactResolver
{
    IReadOnlyDictionary<string, string> Resolve(IAgentTool tool, AgentToolContext ctx);
}
