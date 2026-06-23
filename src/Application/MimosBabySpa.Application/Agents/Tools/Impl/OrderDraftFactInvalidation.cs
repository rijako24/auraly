using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class OrderDraftFactInvalidation
{
    private const string OrderFinalizedRole = "order.finalized";

    public static async Task ClearOrderFinalizedAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        var key = new FactRoleIndex(ctx.Config?.FactSchema ?? []).KeyByRole(OrderFinalizedRole);
        if (string.IsNullOrWhiteSpace(key))
            return;

        var cleared = await factsService.ClearFieldsAsync(ctx.ConversationId, [key], cancellationToken);
        foreach (var factKey in cleared)
            ctx.Facts.Remove(factKey);
    }
}