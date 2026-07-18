using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Commerce;

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

        return Task.FromResult(Build(operationContext.Session.Facts, config.Commerce));
    }

    public static TurnPlanningContextFragment? Build(
        IReadOnlyDictionary<string, string> facts,
        CommerceConfig? commerce = null)
    {
        var offerMemory = CatalogOfferMemory.Read(facts);
        var recommendationMemory = CatalogRecommendationMemory.Read(facts);
        var pending = PendingCartCommandMemory.Read(facts);
        if (offerMemory is null && recommendationMemory is null && pending is null)
            return null;

        var latestSequence = offerMemory?.Snapshots.Max(snapshot => snapshot.Sequence);
        var pendingItem = pending?.Items.FirstOrDefault(item => item.RequiresResolution);
        var pendingCommand = pendingItem?.Command;
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
                    deferred_command_count = 0,
                    discard_on_finalize_issue_codes =
                        commerce?.PendingCart.DiscardOnFinalizeIssueCodes ?? [],
                    pending_items = Array.Empty<object>()
                }
                : new
                {
                    expected_reply = "resolve_pending_cart_selection",
                    operation = pendingCommand?.Operation,
                    requested_product = pendingItem?.OriginalProductText,
                    quantity = pendingCommand?.Quantity,
                    candidate_products = pendingItem?.Issue?.ProductCandidates
                        .Select(candidate => candidate.Name).ToArray() ?? [],
                    deferred_command_count = pending.Items.Count(item => item.RequiresResolution),
                    discard_on_finalize_issue_codes =
                        commerce?.PendingCart.DiscardOnFinalizeIssueCodes ?? [],
                    pending_items = pending.Items
                        .Where(item => item.RequiresResolution)
                        .Select(item => (object)new
                        {
                            requested_product = item.OriginalProductText,
                            operation = item.Command.Operation,
                            quantity = item.Command.Quantity,
                            issue_code = item.Issue?.Code,
                            recognized_product = item.Issue?.ProductText,
                            requested_quantity = item.Issue?.RequestedQuantity,
                            available_quantity = item.Issue?.AvailableQuantity,
                            candidates = item.Issue?.ProductCandidates.Select(candidate => new
                            {
                                name = candidate.Name,
                                available = candidate.IsAvailable,
                                unit_price = candidate.UnitPrice,
                                currency = candidate.Currency
                            }).ToArray() ?? []
                        }).ToArray()
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
