using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed record ProductCatalogSyncRequest(
    int PageSize = 50,
    int MaxPages = 5_000,
    CommerceProvider? Provider = null,
    int PageTimeoutSeconds = 120);
public sealed record ProductCatalogSyncResult(int PagesProcessed, int ProductsProcessed, DateTime CompletedAtUtc)
{
    public int ProductsChanged { get; init; }
    public int CustomerPagesProcessed { get; init; }
    public int CustomersProcessed { get; init; }
    public int CustomersChanged { get; init; }
}
public sealed record ProductIdentityRefreshResult(int ProductsFound, int ProductsChanged, DateTime CompletedAtUtc);

public interface IProductCatalogSyncService
{
    Task<ProductCatalogSyncResult> SyncAsync(Guid businessId, ProductCatalogSyncRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<ProductCatalogSyncResult>> SyncAllEnabledAsync(
        CommerceProvider provider = CommerceProvider.Mantis,
        CancellationToken ct = default);
    Task<ProductIdentityRefreshResult> RefreshProductAsync(
        Guid businessId,
        string query,
        CommerceProvider provider = CommerceProvider.Mantis,
        CancellationToken ct = default);
}

public sealed class ProductCatalogSyncService : IProductCatalogSyncService
{
    private readonly IUnitOfWork _unitOfWork;
    private const int CurrentSearchIndexVersion = ProductSearchText.CurrentIndexVersion;
    private readonly ICommerceAdapterFactory _adapters;

    public ProductCatalogSyncService(IUnitOfWork unitOfWork, ICommerceAdapterFactory adapters)
    {
        _unitOfWork = unitOfWork;
        _adapters = adapters;
    }

    public async Task<ProductCatalogSyncResult> SyncAsync(
        Guid businessId, ProductCatalogSyncRequest request, CancellationToken ct = default)
    {
        var pageSize = Math.Clamp(request.PageSize, 1, 50);
        var maxPages = Math.Clamp(request.MaxPages, 1, 10_000);
        var pageTimeout = TimeSpan.FromSeconds(Math.Clamp(request.PageTimeoutSeconds, 1, 600));
        var connections = await _unitOfWork.IntegrationConnections.GetByBusinessConnectionTypeAsync(
            businessId, ConnectionType.Commerce, ct);
        var eligible = connections.Where(connection => connection.IsEnabled
            && connection.Capability == (int)CommerceCapability.CatalogAndOrders
            && (!request.Provider.HasValue || connection.Provider == (int)request.Provider.Value))
            .ToList();
        if (eligible.Count == 0)
            throw new InvalidOperationException("No enabled catalog commerce connection was found for this business.");
        if (eligible.Count > 1)
            throw new InvalidOperationException("More than one catalog commerce connection is enabled; specify Provider explicitly.");

        var connection = eligible[0];
        if (!Enum.IsDefined(typeof(CommerceProvider), connection.Provider))
            throw new InvalidOperationException($"Commerce provider value '{connection.Provider}' is not supported.");
        var provider = (CommerceProvider)connection.Provider;
        if (provider == CommerceProvider.Local)
            throw new InvalidOperationException("The local catalog does not require remote synchronization.");
        var adapter = _adapters.Resolve(provider);
        var context = new CommerceAdapterContext(businessId, Guid.Empty, null, provider, connection);
        var total = 0;
        var changed = 0;
        var customerPages = 0;
        var customers = 0;
        var customersChanged = 0;
        var pages = 0;
        string? previousFingerprint = null;

        try
        {
            if (connection.CatalogSyncNextPage > 0)
            {
                var page = Math.Max(1, connection.CatalogSyncNextPage);
                var deltaSource = adapter as ICommerceProductDeltaIdentitySource;
                var deltaEndDate = DateTime.UtcNow.Date;
                DateTime? deltaDate = deltaSource is not null && connection.LastSyncAt.HasValue
                    ? connection.CatalogDeltaCursorDate?.Date
                        ?? connection.LastSyncAt.Value.Date.AddDays(-1)
                    : null;
                if (deltaDate > deltaEndDate)
                    deltaDate = deltaEndDate;

                for (var processedPage = 0; processedPage < maxPages; processedPage++)
                {
                    ProductIdentityPage result;
                    if (deltaDate.HasValue && deltaSource is not null)
                    {
                        var currentDeltaDate = deltaDate.Value;
                        result = await ExecutePageAsync(
                            token => deltaSource.GetProductIdentityDeltaPageAsync(
                                context, currentDeltaDate, page, pageSize, token),
                            pageTimeout,
                            $"Catalog delta {currentDeltaDate:yyyy-MM-dd}, page {page}",
                            ct);
                    }
                    else if (adapter is ICommerceProductIdentitySource identitySource)
                    {
                        result = await ExecutePageAsync(
                            token => identitySource.GetProductIdentityPageAsync(context, page, pageSize, token),
                            pageTimeout,
                            $"Catalog page {page}",
                            ct);
                    }
                    else
                    {
                        var searchResult = await ExecutePageAsync(
                            token => adapter.SearchProductsAsync(
                                new ProductSearchRequest(null, null, pageSize, IncludeStock: false, Page: page),
                                context,
                                token),
                            pageTimeout,
                            $"Catalog page {page}",
                            ct);
                        result = new ProductIdentityPage(searchResult.Products, searchResult.HasMore);
                    }
                    pages++;
                    total += result.Products.Count;

                    var fingerprint = string.Join('|', result.Products
                        .Select(product => product.ExternalProductId ?? product.Sku ?? product.Name)
                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                    if (page > 1 && fingerprint.Length > 0 && fingerprint == previousFingerprint)
                        throw new InvalidOperationException("Catalog pagination did not advance; synchronization was stopped to prevent an infinite loop.");
                    previousFingerprint = fingerprint;

                    changed += await UpsertProductIdentitiesAsync(connection, result.Products, ct);
                    var completedCatalog = !result.HasMore;
                    if (completedCatalog && deltaDate.HasValue && deltaDate.Value < deltaEndDate)
                    {
                        deltaDate = deltaDate.Value.AddDays(1);
                        page = 1;
                        previousFingerprint = null;
                        connection.CatalogDeltaCursorDate = deltaDate;
                        connection.CatalogSyncNextPage = page;
                    }
                    else
                    {
                        connection.CatalogSyncNextPage = completedCatalog ? 0 : checked(page + 1);
                        connection.CatalogDeltaCursorDate = completedCatalog ? null : deltaDate;
                    }
                    connection.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
                    await _unitOfWork.SaveChangesAsync(ct);

                    if (completedCatalog && connection.CatalogSyncNextPage == 0)
                        break;
                    if (!completedCatalog)
                        page = checked(page + 1);
                    if (processedPage == maxPages - 1)
                        throw new InvalidOperationException("Catalog synchronization reached MaxPages before completing the full or delta range; the next execution will resume from the saved checkpoint (date and page).");
                }
            }

            var synchronizedCatalog = total > 0 || !connection.LastSyncAt.HasValue
                ? null
                : await _unitOfWork.Products.GetIdentityCatalogAsync(businessId, ct);
            var hasSynchronizedIdentity = total > 0
                || connection.LastSyncAt.HasValue
                && synchronizedCatalog?.Any(product =>
                    product.IntegrationConnectionId == connection.IntegrationConnectionId) == true;
            if (!hasSynchronizedIdentity)
                throw new InvalidOperationException(
                    "Catalog synchronization completed without any products; the catalog remains unavailable and the empty result was not marked as a successful synchronization.");

            if (adapter is ICommerceCustomerIdentitySource customerSource)
            {
                var firstCustomerPage = Math.Max(1, connection.CustomerSyncNextPage);
                for (var offset = 0; offset < maxPages; offset++)
                {
                    var page = checked(firstCustomerPage + offset);
                    var result = await ExecutePageAsync(
                        token => customerSource.GetCustomerIdentityPageAsync(context, page, pageSize, token),
                        pageTimeout,
                        $"Customer page {page}",
                        ct);
                    customerPages++;
                    customers += result.Customers.Count;
                    customersChanged += await UpsertCustomerIdentitiesAsync(connection, result.Customers, ct);
                    var completedCustomers = !result.HasMore || result.Customers.Count == 0;
                    connection.CustomerSyncNextPage = completedCustomers ? 1 : checked(page + 1);
                    connection.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                    if (completedCustomers)
                        break;
                    if (offset == maxPages - 1)
                        throw new InvalidOperationException("Customer synchronization reached MaxPages before Mantis reported the final page; the next execution will resume from the saved checkpoint.");
                }
            }

            connection.CatalogSyncNextPage = 1;
            connection.CatalogDeltaCursorDate = null;
            connection.CustomerSyncNextPage = 1;
            connection.LastSyncAt = DateTime.UtcNow;
            connection.LastError = null;
            connection.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return new(pages, total, connection.LastSyncAt.Value)
            {
                ProductsChanged = changed,
                CustomerPagesProcessed = customerPages,
                CustomersProcessed = customers,
                CustomersChanged = customersChanged
            };
        }
        catch (Exception exception)
        {
            if (connection.CatalogSyncNextPage == 0)
            {
                connection.CatalogSyncNextPage = 1;
                connection.CatalogDeltaCursorDate = null;
            }
            connection.LastError = exception.Message.Length > 4000 ? exception.Message[..4000] : exception.Message;
            connection.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }
    }
    public async Task<IReadOnlyList<ProductCatalogSyncResult>> SyncAllEnabledAsync(
        CommerceProvider provider = CommerceProvider.Mantis,
        CancellationToken ct = default)
    {
        var connections = await _unitOfWork.IntegrationConnections.GetEnabledCommerceConnectionsAsync(
            provider, CommerceCapability.CatalogAndOrders, ct);
        var results = new List<ProductCatalogSyncResult>(connections.Count);
        foreach (var connection in connections)
        {
            results.Add(await SyncAsync(
                connection.BusinessId,
                new ProductCatalogSyncRequest(Provider: provider),
                ct));
        }
        return results;
    }

    public async Task<ProductIdentityRefreshResult> RefreshProductAsync(
        Guid businessId,
        string query,
        CommerceProvider provider = CommerceProvider.Mantis,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("A product name or code is required.", nameof(query));

        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            businessId, provider, CommerceCapability.CatalogAndOrders, ct)
            ?? throw new InvalidOperationException($"Commerce connection '{provider}' is not configured.");
        if (!connection.IsEnabled)
            throw new InvalidOperationException($"Commerce connection '{provider}' is disabled.");

        var adapter = _adapters.Resolve(provider);
        var context = new CommerceAdapterContext(businessId, Guid.Empty, null, provider, connection);
        var result = await adapter.SearchProductsAsync(
            new ProductSearchRequest(query.Trim(), null, 50, IncludeStock: false),
            context,
            ct);
        var changed = await UpsertProductIdentitiesAsync(connection, result.Products, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return new ProductIdentityRefreshResult(result.Products.Count, changed, DateTime.UtcNow);
    }

    private async Task<int> UpsertProductIdentitiesAsync(
        IntegrationConnection connection,
        IReadOnlyList<ProductReference> references,
        CancellationToken ct)
    {
        var changed = 0;
        var categories = new Dictionary<string, ProductCategory>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            var externalId = Clean(reference.ExternalProductId) ?? Clean(reference.Sku);
            if (externalId is null || string.IsNullOrWhiteSpace(reference.Name))
                continue;

            var identityDescription = string.Join(' ', new[]
                {
                    reference.Description,
                    reference.FamilyName,
                    reference.SubcategoryName,
                    reference.ProductClassName
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase));
            var productCategory = await ResolveProductCategoryAsync(connection, reference, categories, ct);
            var categoryName = productCategory?.Name ?? Clean(reference.CategoryName);
            var existing = await _unitOfWork.Products.GetByExternalIdAsync(
                connection.BusinessId,
                connection.IntegrationConnectionId,
                externalId,
                ct);
            if (existing is null)
            {
                var now = DateTime.UtcNow;
                var product = new Product
                {
                    ProductId = Guid.NewGuid(),
                    BusinessId = connection.BusinessId,
                    IntegrationConnectionId = connection.IntegrationConnectionId,
                    ExternalProductId = externalId,
                    Source = ProductSource.External,
                    Sku = Clean(reference.Sku) ?? externalId,
                    Name = reference.Name.Trim(),
                    Description = Clean(identityDescription),
                    ProductCategoryId = productCategory?.ProductCategoryId,
                    CategoryName = categoryName,
                    ManageStock = false,
                    StockQuantity = null,
                    SearchIndexVersion = CurrentSearchIndexVersion,
                    IsActive = reference.IsActive,
                    RawPayloadJson = null,
                    LastSyncedAt = now,
                    CreatedAt = now
                };
                await _unitOfWork.Products.CreateAsync(product, ct);
                await _unitOfWork.Products.ReplaceSearchTermsAsync(product, ct);
                changed++;
                continue;
            }

            var sku = Clean(reference.Sku) ?? externalId;
            var name = reference.Name.Trim();
            var description = Clean(identityDescription);
            var identityChanged = !EqualsText(existing.Sku, sku)
                || !EqualsText(existing.Name, name)
                || !EqualsText(existing.Description, description)
                || existing.ProductCategoryId != productCategory?.ProductCategoryId
                || !EqualsText(existing.CategoryName, categoryName)
                || existing.ManageStock
                || existing.StockQuantity.HasValue
                || existing.IsActive != reference.IsActive
                || existing.RawPayloadJson is not null
                || existing.SearchIndexVersion != CurrentSearchIndexVersion;
            if (!identityChanged)
                continue;

            existing.Sku = sku;
            existing.Name = name;
            existing.Description = description;
            existing.ProductCategoryId = productCategory?.ProductCategoryId;
            existing.CategoryName = categoryName;
            existing.ManageStock = false;
            existing.StockQuantity = null;
            existing.IsActive = reference.IsActive;
            existing.RawPayloadJson = null;
            existing.SearchIndexVersion = CurrentSearchIndexVersion;
            existing.LastSyncedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Products.UpdateAsync(existing, ct);
            await _unitOfWork.Products.ReplaceSearchTermsAsync(existing, ct);
            changed++;
        }
        return changed;
    }

    private async Task<ProductCategory?> ResolveProductCategoryAsync(
        IntegrationConnection connection,
        ProductReference reference,
        IDictionary<string, ProductCategory> categories,
        CancellationToken ct)
    {
        var externalId = Clean(reference.ExternalCategoryId);
        var name = Clean(reference.CategoryName);
        var cacheKey = externalId is not null
            ? $"id:{externalId}"
            : name is not null ? $"name:{name}" : null;
        if (cacheKey is not null && categories.TryGetValue(cacheKey, out var cached))
            return cached;
        ProductCategory? category = null;
        if (externalId is not null)
            category = await _unitOfWork.ProductCategories.GetByExternalIdAsync(
                connection.BusinessId, connection.IntegrationConnectionId, externalId, ct);
        if (category is null && name is not null)
            category = await _unitOfWork.ProductCategories.GetByNameAsync(
                connection.BusinessId, connection.IntegrationConnectionId, name, ct);
        if (category is null)
        {
            if (name is null)
                return null;
            var now = DateTime.UtcNow;
            category = new ProductCategory
            {
                ProductCategoryId = Guid.NewGuid(),
                BusinessId = connection.BusinessId,
                IntegrationConnectionId = connection.IntegrationConnectionId,
                ExternalCategoryId = externalId,
                Name = name,
                DisplayOrder = 0,
                IsActive = true,
                IsBrowsable = true,
                LastSyncedAt = now,
                CreatedAt = now
            };
            await _unitOfWork.ProductCategories.CreateAsync(category, ct);
            if (cacheKey is not null)
                categories[cacheKey] = category;
            return category;
        }

        if (name is null || EqualsText(category.Name, name))
            return category;
        category.Name = name;
        category.LastSyncedAt = DateTime.UtcNow;
        category.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.ProductCategories.UpdateAsync(category, ct);
        if (cacheKey is not null)
            categories[cacheKey] = category;
        return category;

    }
    private async Task<int> UpsertCustomerIdentitiesAsync(
        IntegrationConnection connection,
        IReadOnlyList<ExternalCustomerIdentityReference> references,
        CancellationToken ct)
    {
        var changed = 0;
        foreach (var reference in references)
        {
            var accountId = Clean(reference.ExternalAccountId);
            var customerId = Clean(reference.ExternalCustomerId);
            var phone = Clean(reference.PhoneNormalized);
            if (accountId is null || customerId is null || phone is null)
                continue;

            var existing = await _unitOfWork.ExternalCommerceCustomers.GetByExternalKeysAsync(
                connection.BusinessId,
                connection.IntegrationConnectionId,
                accountId,
                customerId,
                ct);
            if (existing is null)
            {
                var now = DateTime.UtcNow;
                await _unitOfWork.ExternalCommerceCustomers.CreateAsync(new ExternalCommerceCustomer
                {
                    ExternalCommerceCustomerId = Guid.NewGuid(),
                    BusinessId = connection.BusinessId,
                    IntegrationConnectionId = connection.IntegrationConnectionId,
                    ExternalAccountId = accountId,
                    ExternalCustomerId = customerId,
                    Name = Clean(reference.Name),
                    PhoneNormalized = phone,
                    Phone = Clean(reference.Phone),
                    IsActive = true,
                    LastSyncedAt = now,
                    CreatedAt = now
                }, ct);
                changed++;
                continue;
            }

            var name = Clean(reference.Name);
            var rawPhone = Clean(reference.Phone);
            if (EqualsText(existing.Name, name)
                && EqualsText(existing.PhoneNormalized, phone)
                && EqualsText(existing.Phone, rawPhone)
                && existing.IsActive)
            {
                continue;
            }

            existing.Name = name;
            existing.PhoneNormalized = phone;
            existing.Phone = rawPhone;
            existing.IsActive = true;
            existing.LastSyncedAt = DateTime.UtcNow;
            existing.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.ExternalCommerceCustomers.UpdateAsync(existing, ct);
            changed++;
        }
        return changed;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool EqualsText(string? left, string? right) =>
        string.Equals(Clean(left), Clean(right), StringComparison.Ordinal);

    private static async Task<T> ExecutePageAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        TimeSpan timeout,
        string operationName,
        CancellationToken ct)
    {
        using var pageCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pageTask = operation(pageCancellation.Token);
        var completedTask = await Task.WhenAny(
            pageTask,
            Task.Delay(timeout, CancellationToken.None));
        if (completedTask == pageTask)
            return await pageTask;

        await pageCancellation.CancelAsync();
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException(
            $"{operationName} exceeded the configured timeout of {timeout.TotalSeconds:0} seconds; synchronization can resume from its saved checkpoint.");
    }

}
