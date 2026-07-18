using MimosBabySpa.Application.Agents;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed record ProductAliasDto(
    Guid ProductAliasId,
    Guid ProductId,
    string ProductName,
    string Alias,
    ProductAliasScope Scope,
    string CustomerKey,
    ProductAliasKind Kind,
    ProductAliasResolutionMode ResolutionMode,
    ProductAliasSource Source,
    ProductAliasStatus Status,
    int UsageCount,
    DateTime? LastConfirmedAt);

public sealed record ProductAliasImportItem(
    string Alias,
    Guid? ProductId = null,
    string? ExternalProductId = null,
    string? Sku = null,
    ProductAliasKind Kind = ProductAliasKind.Alias,
    ProductAliasResolutionMode ResolutionMode = ProductAliasResolutionMode.AutoResolve,
    ProductAliasScope Scope = ProductAliasScope.Business,
    string? CustomerKey = null,
    ProductAliasStatus Status = ProductAliasStatus.Active);

public sealed record ProductAliasImportRequest(
    IReadOnlyList<ProductAliasImportItem> Items,
    bool DryRun = false);

public sealed record ProductAliasImportError(int Index, string Alias, string Code, string Message);

public sealed record ProductAliasImportResult(
    int Created,
    int Updated,
    int Skipped,
    bool DryRun,
    IReadOnlyList<ProductAliasImportError> Errors);

public interface IProductAliasService
{
    Task<IReadOnlyList<ProductAliasDto>> GetByProductAsync(Guid businessId, Guid productId, CancellationToken ct = default);
    Task<ProductAliasImportResult> ImportAsync(Guid businessId, ProductAliasImportRequest request, CancellationToken ct = default);
    Task LearnConfirmedAsync(AgentConversationContext context, string customerExpression, ProductReference product, CancellationToken ct = default);
}

public sealed class ProductAliasService : IProductAliasService
{
    private const int MaxImportItems = 20_000;
    private readonly IUnitOfWork _unitOfWork;

    public ProductAliasService(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

    public async Task<IReadOnlyList<ProductAliasDto>> GetByProductAsync(
        Guid businessId, Guid productId, CancellationToken ct = default)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(businessId, productId, ct)
            ?? throw new InvalidOperationException("Product not found.");
        var aliases = await _unitOfWork.ProductAliases.GetByProductAsync(businessId, productId, ct);
        return aliases.Select(alias => ToDto(alias, product.Name)).ToList();
    }

    public async Task<ProductAliasImportResult> ImportAsync(
        Guid businessId, ProductAliasImportRequest request, CancellationToken ct = default)
    {
        if (request.Items.Count > MaxImportItems)
            throw new InvalidOperationException($"An alias import supports at most {MaxImportItems} items.");

        var catalog = await _unitOfWork.Products.GetIdentityCatalogAsync(businessId, ct);
        var storedAliases = await _unitOfWork.ProductAliases.GetByBusinessAsync(businessId, ct);

        var productsById = catalog.ToDictionary(product => product.ProductId);
        var productsByExternalId = catalog
            .Where(product => !string.IsNullOrWhiteSpace(product.ExternalProductId))
            .GroupBy(product => product.ExternalProductId!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var productsBySku = catalog
            .Where(product => !string.IsNullOrWhiteSpace(product.Sku))
            .GroupBy(product => product.Sku!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var nativeOwners = catalog
            .SelectMany(product => new[] { product.Name, product.Sku, product.ExternalProductId }
                .Where(identity => !string.IsNullOrWhiteSpace(identity))
                .Select(identity => new { Key = ProductSearchText.NormalizeAlias(identity), product.ProductId }))
            .GroupBy(value => value.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key,
                group => group.Select(value => value.ProductId).ToHashSet(), StringComparer.Ordinal);
        var aliasesByMapping = storedAliases.ToDictionary(alias => AliasMappingKey(
            AliasResolutionKey(alias.Scope, alias.CustomerKey, alias.NormalizedAlias), alias.ProductId),
            StringComparer.Ordinal);
        var aliasesByResolution = storedAliases
            .GroupBy(alias => AliasResolutionKey(alias.Scope, alias.CustomerKey, alias.NormalizedAlias), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        var created = 0;
        var updated = 0;
        var skipped = 0;
        var errors = new List<ProductAliasImportError>();
        for (var index = 0; index < request.Items.Count; index++)
        {
            var item = request.Items[index];
            var normalized = ProductSearchText.NormalizeAlias(item.Alias);
            if (normalized.Length < 2)
            {
                errors.Add(new(index, item.Alias, "invalid_alias", "Alias must contain at least two normalized characters."));
                continue;
            }

            var customerKey = item.Scope == ProductAliasScope.Customer
                ? LocalProductCandidateRetriever.NormalizeCustomerKey(item.CustomerKey)
                : string.Empty;
            if (item.Scope == ProductAliasScope.Customer && customerKey.Length == 0)
            {
                errors.Add(new(index, item.Alias, "customer_required", "Customer-scoped aliases require a customer key."));
                continue;
            }

            Product? product = null;
            if (item.ProductId is { } productId)
                productsById.TryGetValue(productId, out product);
            else if (!string.IsNullOrWhiteSpace(item.ExternalProductId))
                productsByExternalId.TryGetValue(item.ExternalProductId.Trim(), out product);
            else if (!string.IsNullOrWhiteSpace(item.Sku))
                productsBySku.TryGetValue(item.Sku.Trim(), out product);
            if (product is null)
            {
                errors.Add(new(index, item.Alias, "product_not_found", "No product matched ProductId, ExternalProductId, or SKU."));
                continue;
            }

            if (IsNativeIdentity(normalized, product))
            {
                skipped++;
                continue;
            }

            if (item.Status == ProductAliasStatus.Active
                && item.ResolutionMode == ProductAliasResolutionMode.AutoResolve
                && nativeOwners.TryGetValue(normalized, out var owners)
                && owners.Any(owner => owner != product.ProductId))
            {
                errors.Add(new(index, item.Alias, "native_identity_conflict",
                    "An auto-resolving alias cannot equal another product's name, SKU, or external identifier."));
                continue;
            }

            var resolutionKey = AliasResolutionKey(item.Scope, customerKey, normalized);
            if (!aliasesByResolution.TryGetValue(resolutionKey, out var conflicts))
            {
                conflicts = [];
                aliasesByResolution[resolutionKey] = conflicts;
            }
            if (item.Status == ProductAliasStatus.Active
                && item.ResolutionMode == ProductAliasResolutionMode.AutoResolve
                && conflicts.Any(alias => alias.ProductId != product.ProductId
                    && alias.Status == ProductAliasStatus.Active))
            {
                errors.Add(new(index, item.Alias, "alias_conflict", "An auto-resolving alias cannot point to multiple active products in the same scope."));
                continue;
            }

            var mappingKey = AliasMappingKey(resolutionKey, product.ProductId);
            aliasesByMapping.TryGetValue(mappingKey, out var existing);
            if (existing is null)
            {
                created++;
                var newAlias = new ProductAlias
                {
                    ProductAliasId = Guid.NewGuid(),
                    BusinessId = businessId,
                    ProductId = product.ProductId,
                    Scope = item.Scope,
                    CustomerKey = customerKey,
                    Alias = item.Alias.Trim(),
                    NormalizedAlias = normalized,
                    Kind = item.Kind,
                    ResolutionMode = item.ResolutionMode,
                    Source = ProductAliasSource.Imported,
                    Status = item.Status,
                    CreatedAt = DateTime.UtcNow
                };
                aliasesByMapping[mappingKey] = newAlias;
                conflicts.Add(newAlias);
                if (!request.DryRun)
                    await _unitOfWork.ProductAliases.CreateAsync(newAlias, ct);
                continue;
            }

            if (existing.Alias == item.Alias.Trim() && existing.Kind == item.Kind
                && existing.ResolutionMode == item.ResolutionMode && existing.Status == item.Status)
            {
                skipped++;
                continue;
            }

            updated++;
            existing.Alias = item.Alias.Trim();
            existing.Kind = item.Kind;
            existing.ResolutionMode = item.ResolutionMode;
            existing.Status = item.Status;
            existing.Source = ProductAliasSource.Imported;
            existing.UpdatedAt = DateTime.UtcNow;
            if (!request.DryRun)
                await _unitOfWork.ProductAliases.UpdateAsync(existing, ct);
        }

        if (!request.DryRun && created + updated > 0)
            await _unitOfWork.SaveChangesAsync(ct);
        return new(created, updated, skipped, request.DryRun, errors);
    }

    public async Task LearnConfirmedAsync(
        AgentConversationContext context,
        string customerExpression,
        ProductReference reference,
        CancellationToken ct = default)
    {
        var normalized = ProductSearchText.NormalizeAlias(customerExpression);
        if (normalized.Length < 2)
            return;
        var product = await ResolveProductAsync(context.BusinessId, reference, ct);
        if (product is null || IsNativeIdentity(normalized, product))
            return;

        var customerKey = CommerceCustomerAliasKey.Resolve(
            context.CommerceCustomer,
            context.ChannelPhone);
        var customerConflicts = customerKey.Length == 0
            ? []
            : await _unitOfWork.ProductAliases.FindConflictsAsync(
                context.BusinessId, ProductAliasScope.Customer, customerKey,
                normalized, product.ProductId, ct);
        var customerAutoResolveSafe = customerKey.Length > 0
            && customerConflicts.All(alias => alias.Status != ProductAliasStatus.Active)
            && !await HasNativeIdentityConflictAsync(context.BusinessId, normalized, product.ProductId, ct);

        if (customerKey.Length > 0)
        {
            await UpsertLearnedAsync(
                context.BusinessId, product.ProductId, ProductAliasScope.Customer, customerKey,
                customerExpression, normalized,
                customerAutoResolveSafe ? ProductAliasStatus.Active : ProductAliasStatus.Pending,
                customerAutoResolveSafe
                    ? ProductAliasResolutionMode.AutoResolve
                    : ProductAliasResolutionMode.SuggestOnly,
                ct);
        }

        await UpsertLearnedAsync(
            context.BusinessId, product.ProductId, ProductAliasScope.Business, string.Empty,
            customerExpression, normalized, ProductAliasStatus.Pending,
            ProductAliasResolutionMode.SuggestOnly, ct);
        await _unitOfWork.SaveChangesAsync(ct);
    }

    private async Task UpsertLearnedAsync(
        Guid businessId, Guid productId, ProductAliasScope scope, string customerKey,
        string rawAlias, string normalizedAlias, ProductAliasStatus status,
        ProductAliasResolutionMode mode, CancellationToken ct)
    {
        var existing = await _unitOfWork.ProductAliases.GetMappingAsync(
            businessId, productId, scope, customerKey, normalizedAlias, ct);
        if (existing is null)
        {
            var requiresSecondCustomerConfirmation =
                scope == ProductAliasScope.Customer
                && status == ProductAliasStatus.Active
                && mode == ProductAliasResolutionMode.AutoResolve;
            await _unitOfWork.ProductAliases.CreateAsync(new ProductAlias
            {
                ProductAliasId = Guid.NewGuid(), BusinessId = businessId, ProductId = productId,
                Scope = scope, CustomerKey = customerKey, Alias = rawAlias.Trim(),
                NormalizedAlias = normalizedAlias, Kind = ProductAliasKind.Alias,
                ResolutionMode = requiresSecondCustomerConfirmation
                    ? ProductAliasResolutionMode.SuggestOnly
                    : mode,
                Source = ProductAliasSource.Learned,
                Status = requiresSecondCustomerConfirmation
                    ? ProductAliasStatus.Pending
                    : status,
                UsageCount = 1, LastConfirmedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            }, ct);
            return;
        }
        existing.UsageCount++;
        existing.LastConfirmedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;
        if (scope == ProductAliasScope.Customer && existing.UsageCount >= 2
            && status == ProductAliasStatus.Active
            && mode == ProductAliasResolutionMode.AutoResolve)
        {
            existing.Status = ProductAliasStatus.Active;
            existing.ResolutionMode = ProductAliasResolutionMode.AutoResolve;
        }
        await _unitOfWork.ProductAliases.UpdateAsync(existing, ct);
    }

    private static string AliasResolutionKey(
        ProductAliasScope scope, string customerKey, string normalizedAlias) =>
        string.Join('\u001f', ((int)scope).ToString(), customerKey, normalizedAlias);

    private static string AliasMappingKey(string resolutionKey, Guid productId) =>
        string.Join('\u001f', resolutionKey, productId.ToString("N"));


    private async Task<Product?> ResolveProductAsync(Guid businessId, ProductReference reference, CancellationToken ct)
    {
        if (reference.ProductId is { } productId)
            return await _unitOfWork.Products.GetByIdAsync(businessId, productId, ct);
        if (!string.IsNullOrWhiteSpace(reference.ExternalProductId))
            return await _unitOfWork.Products.GetByAnyExternalIdAsync(businessId, reference.ExternalProductId, ct);
        if (!string.IsNullOrWhiteSpace(reference.Sku))
            return await _unitOfWork.Products.GetBySkuAsync(businessId, reference.Sku, ct);
        return null;
    }

    private static bool IsNativeIdentity(string normalizedAlias, Product product) =>
        normalizedAlias == ProductSearchText.NormalizeAlias(product.Name)
        || normalizedAlias == ProductSearchText.NormalizeAlias(product.Sku)
        || normalizedAlias == ProductSearchText.NormalizeAlias(product.ExternalProductId);

    private async Task<bool> HasNativeIdentityConflictAsync(
        Guid businessId, string normalizedAlias, Guid productId, CancellationToken ct)
    {
        var sku = await _unitOfWork.Products.GetBySkuAsync(businessId, normalizedAlias, ct);
        if (sku is not null && sku.ProductId != productId)
            return true;

        var external = await _unitOfWork.Products.GetByAnyExternalIdAsync(businessId, normalizedAlias, ct);
        if (external is not null && external.ProductId != productId)
            return true;

        var candidates = await _unitOfWork.Products.SearchAsync(
            businessId, normalizedAlias, null, 50, ct, includeInactive: true);
        return candidates.Any(candidate =>
            candidate.ProductId != productId && IsNativeIdentity(normalizedAlias, candidate));
    }

    private static ProductAliasDto ToDto(ProductAlias alias, string productName) =>
        new(alias.ProductAliasId, alias.ProductId, productName, alias.Alias, alias.Scope,
            alias.CustomerKey, alias.Kind, alias.ResolutionMode, alias.Source,
            alias.Status, alias.UsageCount, alias.LastConfirmedAt);
}
