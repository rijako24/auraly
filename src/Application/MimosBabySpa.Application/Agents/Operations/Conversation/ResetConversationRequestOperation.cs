using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Operations.Conversation;

/// <summary>
/// Starts a clean request while retaining customer-scoped memory and discarding
/// request-owned drafts, pending checkout state and runtime checkpoints.
/// </summary>
public sealed class ResetConversationRequestOperation : IAgentOperation
{
    private readonly IRequestContextService _requests;
    private readonly ICommerceService _commerce;
    private readonly ICheckoutPaymentCoordinator _checkoutPayments;

    public ResetConversationRequestOperation(
        IRequestContextService requests,
        ICommerceService commerce,
        ICheckoutPaymentCoordinator checkoutPayments)
    {
        _requests = requests;
        _commerce = commerce;
        _checkoutPayments = checkoutPayments;
    }

    public OperationDescriptor Descriptor { get; } = new(
        "conversation.reset_request",
        """{"type":"object","additionalProperties":false,"properties":{}}""",
        ["conversation.request_reset"],
        ["conversation.request"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var state = context.ConversationState;
        var config = context.Config;
        var session = context.Session;

        foreach (var checkoutKind in Enum.GetValues<CheckoutKind>())
        {
            await _checkoutPayments.DiscardActiveCheckoutAsync(
                new CheckoutPaymentContext(context.BusinessId, context.ConversationId, session?.ActivePayment),
                checkoutKind,
                cancellationToken);
        }

        var discardedDrafts = config.Commerce.Enabled
            ? await _commerce.DiscardDraftsAsync(context.BusinessId, context.ConversationId, cancellationToken)
            : 0;

        var cleanup = await _requests.CompleteAsync(
            context.ConversationId,
            config,
            state,
            session?.Facts,
            "request_restarted",
            cancellationToken);

        state.ActiveFlowId = null;
        state.ActiveStageId = null;
        state.PendingTurnPlan = null;
        state.ExecutedOperationKeys.Clear();

        if (session is not null)
        {
            session.ActivePayment = null;
            session.ManageableReservations = [];
        }

        return OperationOutcome.Ok(
            "conversation.request_reset",
            new { reset = true, discardedDrafts },
            effects: [new ResetRequestOperationEffect(cleanup.ClearedFacts)]);
    }
}
