using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

public class StartNodeHandler : INodeHandler
{
    public FlowNodeType NodeType => FlowNodeType.Start;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.AdvancePast;

    public Task<NodeExecutionResult> ExecuteAsync(FlowNode node, FlowTurnContext ctx, CancellationToken ct) =>
        Task.FromResult(NodeExecutionResult.Advance());
}
