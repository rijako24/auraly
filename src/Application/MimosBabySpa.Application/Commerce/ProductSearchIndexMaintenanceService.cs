using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed record ProductSearchIndexRebuildResult(
    Guid BusinessId,
    int ProductsReindexed,
    int AliasesScanned,
    int AliasesNormalized,
    int AliasConflicts);

public interface IProductSearchIndexMaintenanceService
{
    Task<ProductSearchIndexRebuildResult> RebuildAsync(
        Guid businessId,
        bool dryRun = false,
        CancellationToken ct = default);
}

public sealed class ProductSearchIndexMaintenanceService : IProductSearchIndexMaintenanceService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductSearchIndexMaintenanceService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<ProductSearchIndexRebuildResult> RebuildAsync(
        Guid businessId,
        bool dryRun = false,
        CancellationToken ct = default)
    {
        var products = await _unitOfWork.Products.GetIdentityCatalogAsync(businessId, ct);
        var aliases = await _unitOfWork.ProductAliases.GetByBusinessAsync(businessId, ct);
        var normalizedAliases = aliases.ToDictionary(alias => alias.ProductAliasId, alias => ProductSearchText.NormalizeAlias(alias.Alias));
        var conflicts = 0;
        var aliasesNormalized = 0;

        foreach (var product in products)
        {
            if (!dryRun)
            {
                product.SearchIndexVersion = ProductSearchText.CurrentIndexVersion;
                product.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.Products.UpdateAsync(product, ct);
                await _unitOfWork.Products.ReplaceSearchTermsAsync(product, ct);
            }
        }

        foreach (var alias in aliases)
        {
            var normalized = normalizedAliases[alias.ProductAliasId];
            if (string.IsNullOrWhiteSpace(normalized) || normalized == alias.NormalizedAlias)
                continue;

            var hasConflict = aliases.Any(other =>
                other.ProductAliasId != alias.ProductAliasId
                && other.Status == ProductAliasStatus.Active
                && alias.Status == ProductAliasStatus.Active
                && other.Scope == alias.Scope
                && other.CustomerKey == alias.CustomerKey
                && other.ProductId != alias.ProductId
                && normalizedAliases[other.ProductAliasId] == normalized);
            if (hasConflict)
            {
                conflicts++;
                continue;
            }

            aliasesNormalized++;
            if (!dryRun)
            {
                alias.NormalizedAlias = normalized;
                alias.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.ProductAliases.UpdateAsync(alias, ct);
            }
        }

        if (!dryRun)
            await _unitOfWork.SaveChangesAsync(ct);

        return new ProductSearchIndexRebuildResult(
            businessId,
            products.Count,
            aliases.Count,
            aliasesNormalized,
            conflicts);
    }
}
