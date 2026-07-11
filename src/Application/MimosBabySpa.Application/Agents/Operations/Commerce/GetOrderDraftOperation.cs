using System.Text.Json;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class GetOrderDraftOperation : IAgentOperation
{
    private readonly ICommerceService _commerce;

    public GetOrderDraftOperation(ICommerceService commerce) => _commerce = commerce;

    public OperationDescriptor Descriptor { get; } = new(
        "commerce.get_order_draft",
        """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""",
        ["order.draft_loaded", "order_draft_missing"],
        [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session
            ?? throw new InvalidOperationException("commerce.get_order_draft requires a conversation session.");
        var draft = await _commerce.GetDraftAsync(session, cancellationToken);
        return draft is null
            ? OperationOutcome.Fail("order_draft_missing", "There is no active order draft.", true)
            : OperationOutcome.Ok("order.draft_loaded", new { order = draft });
    }
}
