using System.Net.Http.Json;
using Auraly.Contracts.Catalog;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosDeviceCredentials(Guid DeviceId, string Secret);
public sealed record PosOperationalScope(Guid BusinessId, Guid WarehouseId);

public interface IPosSynchronizationEventSink
{
    void Record(string level, string category, string title, string? detail = null);
    void ProductReceived(PosCatalogItem product, PosCatalogItem? previous, bool bootstrap);
    void CustomerReceived(PosCustomerPricing customer, PosCustomerPricing? previous);
    void ChannelPriceReceived(
        PosPriceChannelItem price,
        PosPriceChannelItem? previous,
        string? productName);
}

public interface IPosWarehousePolicySink
{
    Task ApplyAsync(bool allowsNegativeStock, CancellationToken cancellationToken = default);
}

public sealed class PosCatalogSynchronizer(
    HttpClient httpClient,
    PosCatalogStore store,
    PosDeviceCredentials credentials,
    PosOperationalScope scope,
    IPosSynchronizationEventSink? events = null,
    IPosWarehousePolicySink? warehousePolicy = null) : IPosInventoryAvailabilityClient
{
    private static readonly string[] OperationalReferenceCatalogs =
        ["payment-method", "card-franchise", "sales-document-type", "cash-denomination"];

    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        var status = await store.StatusAsync(cancellationToken);
        var initialSynchronization = status.Status is "Empty" or "Invalid" or "Bootstrapping";
        if (status.Status is "Empty" or "Invalid")
        {
            var session = await SendAsync<CatalogSyncSessionResponse>(
                HttpMethod.Post,
                $"api/pos/v1/catalog/sync-sessions?{ScopeQuery}",
                content: null,
                cancellationToken);
            await store.BeginBootstrapAsync(session, cancellationToken);
            status = await store.StatusAsync(cancellationToken);
        }

        if (status.Status == "Bootstrapping")
        {
            if (status.SessionId is null)
                throw new InvalidOperationException("The durable bootstrap state has no server session.");
            var cursor = status.NextPageCursor;
            while (true)
            {
                var path = $"api/pos/v1/catalog/sync-sessions/{status.SessionId:D}/pages?{ScopeQuery}&pageSize=500";
                if (!string.IsNullOrWhiteSpace(cursor))
                    path += $"&cursor={Uri.EscapeDataString(cursor)}";
                var page = await SendAsync<CatalogBootstrapPage>(
                    HttpMethod.Get,
                    path,
                    content: null,
                    cancellationToken);
                await store.ApplyBootstrapPageAsync(page, cancellationToken);
                if (!page.HasMore)
                {
                    await store.PromoteBootstrapAsync(cancellationToken);
                    break;
                }
                cursor = page.NextCursor;
            }
        }

        var previousPricing = events is null || initialSynchronization
            ? null
            : await store.ReadPricingSnapshotAsync(cancellationToken);
        var pricing = await SendAsync<PosPricingSnapshot>(
            HttpMethod.Get,
            $"api/pos/v1/pricing/snapshot?{ScopeQuery}",
            content: null,
            cancellationToken);
        if (previousPricing is not null)
        {
            var previousCustomers = previousPricing.Customers.ToDictionary(item => item.CustomerId);
            foreach (var customer in pricing.Customers)
            {
                previousCustomers.TryGetValue(customer.CustomerId, out var previous);
                if (!CustomerEquals(previous, customer))
                    events!.CustomerReceived(customer, previous);
            }

            var previousPrices = previousPricing.PriceChannelItems.ToDictionary(PriceKey);
            foreach (var price in pricing.PriceChannelItems)
            {
                previousPrices.TryGetValue(PriceKey(price), out var previous);
                if (previous == price) continue;
                var product = await store.GetByProductIdAsync(price.ProductId, cancellationToken);
                events!.ChannelPriceReceived(price, previous, product?.Name);
            }
        }
        await store.ApplyPricingSnapshotAsync(pricing, cancellationToken);
        if (pricing.WarehouseAllowsNegativeStock is { } allowsNegativeStock
            && warehousePolicy is not null)
            await warehousePolicy.ApplyAsync(allowsNegativeStock, cancellationToken);
        foreach (var catalogCode in OperationalReferenceCatalogs)
        {
            var options = await SendAsync<IReadOnlyList<ReferenceOption>>(
                HttpMethod.Get,
                $"api/commerce/v1/reference-options/{Uri.EscapeDataString(catalogCode)}",
                content: null,
                cancellationToken);
            await store.ApplyReferenceOptionsAsync(catalogCode, options, cancellationToken);
        }
        while (true)
        {
            status = await store.StatusAsync(cancellationToken);
            var page = await SendAsync<CatalogDeltaPage>(
                HttpMethod.Get,
                $"api/pos/v1/catalog/changes?{ScopeQuery}&cursor={status.Cursor}&pageSize=500",
                content: null,
                cancellationToken);
            foreach (var change in page.Changes)
                events?.ProductReceived(
                    change.Product,
                    await store.GetByProductIdAsync(change.Product.ProductId, cancellationToken),
                    bootstrap: false);
            await store.ApplyChangesAsync(page, cancellationToken);
            if (!page.HasMore) break;
        }
    }

    public async Task<InventoryAvailabilityResponse> CheckAvailabilityAsync(
        InventoryAvailabilityRequest request,
        CancellationToken cancellationToken = default) =>
        await SendAsync<InventoryAvailabilityResponse>(
            HttpMethod.Post,
            $"api/pos/v1/inventory/availability?businessId={scope.BusinessId:D}",
            JsonContent.Create(request),
            cancellationToken);

    private string ScopeQuery =>
        $"businessId={scope.BusinessId:D}&warehouseId={scope.WarehouseId:D}";

    private static (Guid PriceChannelId, Guid ProductId, decimal MinimumQuantity) PriceKey(
        PosPriceChannelItem item) =>
        (item.PriceChannelId, item.ProductId, item.MinimumQuantity);

    private static bool CustomerEquals(
        PosCustomerPricing? previous,
        PosCustomerPricing current) =>
        previous is not null &&
        previous.CustomerId == current.CustomerId &&
        previous.Identification == current.Identification &&
        previous.Name == current.Name &&
        previous.PriceChannelId == current.PriceChannelId &&
        previous.IsActive == current.IsActive &&
        previous.RequiresElectronicInvoice == current.RequiresElectronicInvoice &&
        previous.AppliesWithholding == current.AppliesWithholding &&
        previous.TaxJurisdictionCode == current.TaxJurisdictionCode &&
        (previous.TaxResponsibilities ?? []).SequenceEqual(
            current.TaxResponsibilities ?? [], StringComparer.Ordinal);

    private async Task<T> SendAsync<T>(
        HttpMethod method,
        string path,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new HttpRequestException(
                $"Auraly Server rejected catalog synchronization with " +
                $"{(int)response.StatusCode}: {detail}",
                null,
                response.StatusCode);
        }
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The Auraly server returned an empty catalog response.");
    }
}
