using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

public class EndNodeHandler : INodeHandler
{
    public FlowNodeType NodeType => FlowNodeType.End;
    public ReEntryBehavior ReEntryBehavior => ReEntryBehavior.AdvancePast;

    public Task<NodeExecutionResult> ExecuteAsync(FlowNode node, FlowTurnContext ctx, CancellationToken ct) =>
        Task.FromResult(NodeExecutionResult.Advance());
}
