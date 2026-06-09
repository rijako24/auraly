using MimosBabySpa.Application.Agents.Configuration;

namespace MimosBabySpa.Application.Agents.Composition;

public interface IFlowStageDetector
{
    AgentFlowStage? DetectCurrentStage(AgentFlowDefinition flow, AgentToolContext? session);
}
