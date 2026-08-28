using System.Net.Http.Json;
using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public sealed class PosProductAvailabilityServerClient(
    HttpClient http,
    PosDeviceCredentials credentials,
    PosOperationalScope scope)
{
    public async Task<IReadOnlyList<ProductWarehouseAvailabilityItem>> GetAsync(
        Guid productId,
        bool includeOtherBusinesses,
        CancellationToken ct)
    {
        var query = new QueryString()
            .Add("businessId", scope.BusinessId.ToString("D"))
            .Add("includeOtherBusinesses", includeOtherBusinesses ? "true" : "false");
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/inventory/products/{productId:D}/warehouse-availability{query}");
        request.Headers.Add("X-Auraly-Device-Id", credentials.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", credentials.Secret);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                await response.Content.ReadAsStringAsync(ct), null, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<IReadOnlyList<ProductWarehouseAvailabilityItem>>(
            cancellationToken: ct) ?? [];
    }
}
