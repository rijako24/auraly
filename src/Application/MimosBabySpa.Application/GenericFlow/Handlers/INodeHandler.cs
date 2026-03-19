using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Handlers;

/// <summary>
/// Executes the logic for a specific FlowNodeType.
/// Implementations are registered as keyed services by FlowNodeType.
/// </summary>
public interface INodeHandler
{
    FlowNodeType NodeType { get; }

    /// <summary>
    /// Defines behavior when the engine revisits this node after a WaitForUser pause.
    /// ReExecute: call ExecuteAsync again (for CollectFields, Action, WaitForEvent).
    /// AdvancePast: skip to the next node automatically (for GenerateResponse that already fired).
    /// </summary>
    ReEntryBehavior ReEntryBehavior { get; }

    Task<NodeExecutionResult> ExecuteAsync(
        FlowNode node,
        FlowTurnContext ctx,
        CancellationToken ct);
}
