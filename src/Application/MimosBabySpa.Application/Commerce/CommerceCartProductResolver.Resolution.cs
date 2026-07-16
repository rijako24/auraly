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
        if (remembered.Count > 0)
        {
            return ProductResolutionEngine.Resolve(
                searchReference,
                remembered.Select(product => new RetrievedProductCandidate(
                    product, ProductMatchSource.RememberedCatalog)).ToList());
        }

        var remote = await FindAsync(context, productText, cancellationToken);
        var retrieved = remote.Select(product => new RetrievedProductCandidate(
            product, ProductMatchSource.Catalog)).ToList();
        if (_candidateRetriever is not null)
        {
            retrieved.AddRange(await _candidateRetriever.RetrieveAsync(
                context, productText, cancellationToken));
        }
        return ProductResolutionEngine.Resolve(searchReference, retrieved);
    }
}
