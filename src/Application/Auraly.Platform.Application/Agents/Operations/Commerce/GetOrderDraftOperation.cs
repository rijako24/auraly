using System.Text.Json;
using Auraly.Platform.Application.Commerce;

namespace Auraly.Platform.Application.Agents.Operations.Commerce;

public sealed class GetOrderDraftOperation : IAgentOperation
{
    public const string OperationId = "commerce.get_order_draft";
    private readonly ICommerceService _commerce;

    public GetOrderDraftOperation(ICommerceService commerce) => _commerce = commerce;

    public OperationDescriptor Descriptor { get; } = new(
        OperationId,
        """{"type":"object","additionalProperties":false,"properties":{},"required":[]}""",
        ["order.draft_loaded", "order.draft_empty", "order_draft_missing"],
        [], [], []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var session = context.Session
            ?? throw new InvalidOperationException("commerce.get_order_draft requires a conversation session.");
        var draft = await _commerce.GetDraftAsync(session, cancellationToken);
        if (draft is not null)
            draft = CartItemPresentationMemory.Decorate(draft, session.Facts);
        return draft is null
            ? OperationOutcome.Fail("order_draft_missing", "There is no active order draft.", true)
            : OperationOutcome.Ok(draft.Items.Count == 0 ? "order.draft_empty" : "order.draft_loaded", new
            {
                order = new
                {
                    currency = draft.Currency,
                    subtotal = draft.Subtotal,
                    discount_total = draft.DiscountTotal,

                    total = draft.Total,
                    items = draft.Items.Select(item => new
                    {
                        name = item.ProductName,
                        requested_name = item.RequestedName,
                        quantity = item.Quantity,
                        unit_price = item.UnitPrice,
                        line_total = item.LineTotal
                    }).ToList()
                }
            });
    }
}
