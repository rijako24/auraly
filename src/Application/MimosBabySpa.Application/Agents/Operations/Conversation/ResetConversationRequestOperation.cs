using System.Text.Json;

namespace MimosBabySpa.Application.Agents.Operations.Conversation;

/// <summary>
/// Resets only request-scoped runtime checkpoints. Which facts are cleared remains
/// tenant configuration through the outcome effects of the global action.
/// </summary>
public sealed class ResetConversationRequestOperation : IAgentOperation
{
    public OperationDescriptor Descriptor { get; } = new(
        "conversation.reset_request",
        """{"type":"object","additionalProperties":false,"properties":{}}""",
        ["conversation.request_reset"],
        ["conversation.request"],
        [],
        []);

    public Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var state = context.ConversationState;
        state.ActiveFlowId = null;
        state.ActiveStageId = null;
        state.PendingTurnPlan = null;
        state.RequestGeneration++;
        state.ExecutedOperationKeys.Clear();
        state.StageFactSnapshots.Clear();
        state.Verifications.Clear();

        if (context.Session is not null)
        {
            context.Session.ActivePayment = null;
            context.Session.ManageableReservations = [];
        }

        return Task.FromResult(OperationOutcome.Ok(
            "conversation.request_reset",
            new { reset = true }));
    }
}
