using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public interface IProductCandidateRetriever
{
    Task<IReadOnlyList<RetrievedProductCandidate>> RetrieveAsync(
        AgentConversationContext context,
        string productText,
        CancellationToken cancellationToken = default);
}

public sealed class LocalProductCandidateRetriever : IProductCandidateRetriever
{
    private readonly IUnitOfWork _unitOfWork;

    public LocalProductCandidateRetriever(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<RetrievedProductCandidate>> RetrieveAsync(
        AgentConversationContext context,
        string productText,
        CancellationToken cancellationToken = default)
    {
        var normalizedAlias = ProductSearchText.NormalizeAlias(productText);
        var customerKey = NormalizeCustomerKey(context.ChannelPhone);
        var aliases = normalizedAlias.Length == 0
            ? []
            : await _unitOfWork.ProductAliases.FindActiveAsync(
                context.BusinessId, normalizedAlias, customerKey, cancellationToken);
        var effectiveAliases = aliases.Any(alias => alias.Scope == ProductAliasScope.Customer)
            ? aliases.Where(alias => alias.Scope == ProductAliasScope.Customer).ToList()
            : aliases;
        var results = effectiveAliases
            .Select(alias => new RetrievedProductCandidate(
                ToReference(alias.Product),
                alias.Scope == ProductAliasScope.Customer ? ProductMatchSource.CustomerAlias : ProductMatchSource.BusinessAlias,
                ExactAlias: true,
                CanAutoResolve: alias.ResolutionMode == ProductAliasResolutionMode.AutoResolve))
            .ToList();

        var keys = ProductSearchText.GetSearchKeys(productText);
        var products = await _unitOfWork.Products.SearchByIndexTermsAsync(
            context.BusinessId, keys, 200, cancellationToken);
        results.AddRange(products.Select(product => new RetrievedProductCandidate(
            ToReference(product), ProductMatchSource.LocalLexicalIndex)));
        return results;
    }

    public static string NormalizeCustomerKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length > 0 ? digits : value.Trim().ToLowerInvariant();
    }

    private static ProductReference ToReference(Product product) =>
        new(
            product.ProductId,
            product.ExternalProductId,
            product.Sku,
            product.Name,
            product.Description,
            product.CategoryName,
            product.UnitPrice,
            product.Currency,
            product.StockQuantity,
            RawPayloadJson: product.RawPayloadJson)
        { IsActive = product.IsActive };
}
