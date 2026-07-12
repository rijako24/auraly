using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed class CommerceCartPlanningContextEnricher : ITurnPlanningContextEnricher
{
    private readonly ICommerceService _commerce;

    public CommerceCartPlanningContextEnricher(ICommerceService commerce)
    {
        _commerce = commerce;
    }
    public async Task<TurnPlanningContextFragment?> EnrichAsync(
        AgentConfig config,
        OperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        if (!config.Commerce.Enabled || operationContext.Session is null)
            return null;

        var draft = await _commerce.GetDraftAsync(operationContext.Session, cancellationToken);
        if (draft.Items.Count == 0)
            return null;

        return new TurnPlanningContextFragment(
            "currentCart",
            JsonSerializer.SerializeToElement(new
            {
                items = draft.Items.Select(item => new
                {
                    name = item.ProductName,
                    quantity = item.Quantity
                })
            }));
    }
}