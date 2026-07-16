using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed record ProductCatalogSyncRequest(
    int PageSize = 50, int MaxPages = 5_000, CommerceProvider? Provider = null);
public sealed record ProductCatalogSyncResult(int PagesProcessed, int ProductsProcessed, DateTime CompletedAtUtc);

public interface IProductCatalogSyncService
{
    Task<ProductCatalogSyncResult> SyncAsync(Guid businessId, ProductCatalogSyncRequest request, CancellationToken ct = default);
}

public sealed class ProductCatalogSyncService : IProductCatalogSyncService
{
    private readonly IUnitOfWork _unitOfWork;
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
        var pages = 0;
        string? previousFingerprint = null;

        try
        {
            for (var page = 1; page <= maxPages; page++)
            {
                var result = await adapter.SearchProductsAsync(
                    new ProductSearchRequest(null, null, pageSize, IncludeStock: true, Page: page), context, ct);
                pages++;
                total += result.Products.Count;

                var fingerprint = string.Join('|', result.Products
                    .Select(product => product.ExternalProductId ?? product.Sku ?? product.Name)
                    .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
                if (page > 1 && fingerprint.Length > 0 && fingerprint == previousFingerprint)
                    throw new InvalidOperationException("Catalog pagination did not advance; synchronization was stopped to prevent an infinite loop.");
                previousFingerprint = fingerprint;

                if (!result.HasMore || result.Products.Count == 0)
                    break;
                if (page == maxPages)
                    throw new InvalidOperationException("Catalog synchronization reached MaxPages before Mantis reported the final page.");
            }

            connection.LastSyncAt = DateTime.UtcNow;
            connection.LastError = null;
            connection.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return new(pages, total, connection.LastSyncAt.Value);
        }
        catch (Exception exception)
        {
            connection.LastError = exception.Message.Length > 4000 ? exception.Message[..4000] : exception.Message;
            connection.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.IntegrationConnections.UpdateAsync(connection, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            throw;
        }
    }
}
