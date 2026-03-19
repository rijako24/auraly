using MimosBabySpa.Domain.Models.Flow;

namespace MimosBabySpa.Application.GenericFlow.Actions;

/// <summary>
/// Pluggable action executed by ActionNodeHandler.
/// Actions encapsulate domain-specific business logic and are registered by action_type.
/// All inputs/outputs are generic key-value pairs — no domain coupling in the interface.
/// </summary>
public interface IFlowAction
{
    string ActionType { get; }

    Task<FlowActionResult> ExecuteAsync(
        Dictionary<string, object?> inputs,
        FlowTurnContext ctx,
        CancellationToken ct);
}
