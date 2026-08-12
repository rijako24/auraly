using System.Net.Http.Json;
using Auraly.Contracts.Fiscal;

namespace Auraly.Pos.Edge.Infrastructure;

public interface IPosFiscalStatusClient
{
    Task<PosFiscalStatusPage> GetPageAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default);
}

public sealed class HttpPosFiscalStatusClient(
    HttpClient httpClient,
    PosDeviceCredentials credentials,
    PosOperationalScope scope) : IPosFiscalStatusClient
{
    public async Task<PosFiscalStatusPage> GetPageAsync(
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var path = $"api/pos/v1/fiscal/statuses?businessId={scope.BusinessId:D}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(cursor))
            path += $"&cursor={Uri.EscapeDataString(cursor)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PosFiscalStatusPage>(
            cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("The fiscal status response is empty.");
    }
}

public sealed class PosFiscalStatusSynchronizer(
    PosEdgeSaleStore store,
    IPosFiscalStatusClient client)
{
    public async Task SynchronizeAsync(CancellationToken cancellationToken = default)
    {
        var cursor = await store.GetFiscalStatusCursorAsync(cancellationToken);
        while (true)
        {
            var page = await client.GetPageAsync(cursor, 100, cancellationToken);
            await store.ApplyFiscalStatusPageAsync(page, cancellationToken);
            cursor = page.NextCursor;
            if (!page.HasMore) return;
        }
    }
}
