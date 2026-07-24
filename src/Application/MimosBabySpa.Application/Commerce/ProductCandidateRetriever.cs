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
        var externalCustomerKey = CommerceCustomerAliasKey.FromExternalCustomer(
            context.CommerceCustomer);
        var legacyCustomerKey = NormalizeCustomerKey(context.ChannelPhone);
        var customerKeys = new[] { externalCustomerKey, legacyCustomerKey }
            .Where(key => key.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        IReadOnlyList<ProductAlias> aliases = [];
        if (normalizedAlias.Length > 0)
        {
            if (customerKeys.Count == 0)
            {
                aliases = await _unitOfWork.ProductAliases.FindActiveAsync(
                    context.BusinessId, normalizedAlias, string.Empty, cancellationToken);
            }
            else
            {
                aliases = await _unitOfWork.ProductAliases.FindActiveAsync(
                    context.BusinessId, normalizedAlias, customerKeys[0], cancellationToken);
                for (var index = 1;
                     index < customerKeys.Count
                     && !aliases.Any(alias => alias.Scope == ProductAliasScope.Customer);
                     index++)
                {
                    var fallback = await _unitOfWork.ProductAliases.FindActiveAsync(
                        context.BusinessId,
                        normalizedAlias,
                        customerKeys[index],
                        cancellationToken);
                    var customerAliases = fallback
                        .Where(alias => alias.Scope == ProductAliasScope.Customer)
                        .ToList();
                    if (customerAliases.Count > 0)
                    {
                        aliases = aliases
                            .Where(alias => alias.Scope == ProductAliasScope.Business)
                            .Concat(customerAliases)
                            .ToList();
                    }
                }
            }
        }
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
            RawPayloadJson: product.RawPayloadJson,
            IntegrationConnectionId: product.IntegrationConnectionId)
        { IsActive = product.IsActive };
}
