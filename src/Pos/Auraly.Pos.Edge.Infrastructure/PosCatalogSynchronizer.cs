using System.Net.Http.Json;
using Auraly.Contracts.Catalog;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosDeviceCredentials(Guid DeviceId, string Secret);

public sealed class PosCatalogSynchronizer(
    HttpClient httpClient,
    PosCatalogStore store,
    PosDeviceCredentials credentials)
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        await store.InitializeAsync(cancellationToken);
        var status = await store.StatusAsync(cancellationToken);
        if (status.Status is "Empty" or "Invalid")
        {
            var session = await SendAsync<CatalogSyncSessionResponse>(
                HttpMethod.Post,
                "api/pos/v1/catalog/sync-sessions",
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
                var path = $"api/pos/v1/catalog/sync-sessions/{status.SessionId:D}/pages?pageSize=500";
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

        while (true)
        {
            status = await store.StatusAsync(cancellationToken);
            var page = await SendAsync<CatalogDeltaPage>(
                HttpMethod.Get,
                $"api/pos/v1/catalog/changes?cursor={status.Cursor}&pageSize=500",
                content: null,
                cancellationToken);
            await store.ApplyChangesAsync(page, cancellationToken);
            if (!page.HasMore) break;
        }
    }

    public async Task<InventoryAvailabilityResponse> CheckAvailabilityAsync(
        InventoryAvailabilityRequest request,
        CancellationToken cancellationToken = default) =>
        await SendAsync<InventoryAvailabilityResponse>(
            HttpMethod.Post,
            "api/pos/v1/inventory/availability",
            JsonContent.Create(request),
            cancellationToken);

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
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken)
            ?? throw new InvalidDataException("The Auraly server returned an empty catalog response.");
    }
}
