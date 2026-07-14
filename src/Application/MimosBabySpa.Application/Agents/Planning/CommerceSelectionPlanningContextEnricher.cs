using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Support;

namespace MimosBabySpa.Application.Agents.Planning;

public sealed class CommerceSelectionPlanningContextEnricher : ITurnPlanningContextEnricher
{
    public Task<TurnPlanningContextFragment?> EnrichAsync(
        AgentConfig config,
        OperationContext operationContext,
        CancellationToken cancellationToken = default)
    {
        if (!config.Commerce.Enabled || operationContext.Session is null)
            return Task.FromResult<TurnPlanningContextFragment?>(null);

        return Task.FromResult(Build(operationContext.Session.Facts));
    }

    public static TurnPlanningContextFragment? Build(IReadOnlyDictionary<string, string> facts)
    {
        var offerMemory = CatalogOfferMemory.Read(facts);
        var recommendationMemory = CatalogRecommendationMemory.Read(facts);
        var pending = PendingCartCommandMemory.Read(facts);
        if (offerMemory is null && recommendationMemory is null && pending is null)
            return null;

        var latestSequence = offerMemory?.Snapshots.Max(snapshot => snapshot.Sequence);
        var pendingCommand = pending?.Commands.FirstOrDefault(command =>
            command.ProductText.Equals(pending.AmbiguousProductText, StringComparison.OrdinalIgnoreCase));
        var value = JsonSerializer.SerializeToElement(new
        {
            interaction = pending is null
                ? new
                {
                    expected_reply = "catalog_follow_up",
                    operation = (string?)null,
                    requested_product = (string?)null,
                    quantity = (decimal?)null,
                    candidate_products = Array.Empty<string>(),
                    deferred_command_count = 0
                }
                : new
                {
                    expected_reply = "resolve_pending_cart_selection",
                    operation = pendingCommand?.Operation,
                    requested_product = (string?)pending.AmbiguousProductText,
                    quantity = pendingCommand?.Quantity,
                    candidate_products = pending.ProductCandidates.Select(candidate => candidate.Name).ToArray(),
                    deferred_command_count = pending.Commands.Count
                },
            latest_offer_sequence = latestSequence,
            offers = offerMemory?.Snapshots.Select(snapshot => new
            {
                sequence = snapshot.Sequence,
                is_latest = snapshot.Sequence == latestSequence,
                search_terms = snapshot.SearchTerms,
                products = snapshot.Products.Select(product => product.Name).ToArray()
            }).ToArray() ?? [],
            recommendations = recommendationMemory?.Products.Select(product => product.Name).ToArray() ?? []
        });

        return new TurnPlanningContextFragment("shoppingContext", value);
    }
}
