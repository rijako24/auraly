using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Operations.Support;

namespace MimosBabySpa.Application.Commerce;

public sealed partial class CommerceCartProductResolver
{
    private readonly IProductCandidateRetriever? _candidateRetriever;

    public CommerceCartProductResolver(
        ICommerceService commerce,
        IProductCandidateRetriever candidateRetriever)
    {
        _commerce = commerce;
        _candidateRetriever = candidateRetriever;
    }

    public async Task<ProductResolution> ResolveAsync(
        AgentConversationContext context,
        string productText,
        CancellationToken cancellationToken = default)
    {
        var searchReference = ProductSelectionMemory.NormalizeSearchReference(productText);
        var remembered = ProductSelectionMemory.FindCatalogMatches(context, productText);
        var retrieved = remembered
            .Select(product => new RetrievedProductCandidate(
                product, ProductMatchSource.RememberedCatalog))
            .ToList();
        if (_candidateRetriever is not null)
        {
            retrieved.AddRange(await _candidateRetriever.RetrieveAsync(
                context, productText, cancellationToken));
        }
        else if (retrieved.Count == 0)
        {
            retrieved.AddRange((await FindAsync(context, productText, cancellationToken))
                .Select(product => new RetrievedProductCandidate(
                    product, ProductMatchSource.LocalLexicalIndex)));
        }

        var resolution = ProductResolutionEngine.Resolve(searchReference, retrieved);
        if (_commerce is not IProductLookupService lookup)
            return resolution;
        if (resolution.Candidates.Count == 0)
            return resolution;

        // Local identity narrows the catalog first. Only the bounded finalist set is
        // then quoted one by one so price and warehouse availability always come
        // from the commerce provider, including ambiguous and suggested products.
        var liveCandidates = new List<RetrievedProductCandidate>(resolution.Candidates.Count);
        foreach (var candidate in resolution.Candidates)
        {
            var original = retrieved.FirstOrDefault(value => value.Product == candidate.Product)
                ?? new RetrievedProductCandidate(candidate.Product, candidate.Source);
            var quoted = await lookup.GetProductAsync(
                context,
                new ProductLookupRequest(
                    candidate.Product.ProductId,
                    candidate.Product.ExternalProductId,
                    candidate.Product.Sku,
                    candidate.Product.Name,
                    searchReference),
                cancellationToken);
            if (quoted is null)
            {
                liveCandidates.Add(original with
                {
                    Product = candidate.Product with { IsActive = false }
                });
                continue;
            }

            if (!quoted.ProductId.HasValue && candidate.Product.ProductId.HasValue)
                quoted = quoted with { ProductId = candidate.Product.ProductId };
            liveCandidates.Add(original with { Product = quoted });
        }

        if (resolution.Status == ProductResolutionStatus.Ambiguous)
        {
            var liveOptions = liveCandidates.Select(candidate => new ProductResolutionCandidate(
                candidate.Product,
                ProductResolutionEngine.Score(searchReference, candidate.Product),
                candidate.Source)).ToList();
            return new ProductResolution(
                liveCandidates.Any(candidate => candidate.Product.IsActive)
                    ? ProductResolutionStatus.Ambiguous
                    : ProductResolutionStatus.Unavailable,
                null,
                liveOptions,
                searchReference);
        }

        return ProductResolutionEngine.Resolve(searchReference, liveCandidates);
    }
}
