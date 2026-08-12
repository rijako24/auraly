using System.Net;
using System.Net.Http.Json;
using Auraly.Api;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PosSynchronizationApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Enrolled_device_negotiates_only_its_authenticated_scope()
    {
        using var client = fixture.CreateClient();
        using var request = Request(
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret,
            fixture.BusinessId);
        using var response = await client.SendAsync(request);

        response.EnsureSuccessStatusCode();
        var negotiation = await response.Content
            .ReadFromJsonAsync<PosSynchronizationNegotiationResponse>();
        Assert.NotNull(negotiation);
        Assert.Equal("wss", negotiation.ClientAccessUri.Scheme);
        Assert.Contains(
            $"tenant={fixture.TenantId:D}",
            negotiation.ClientAccessUri.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            $"business={fixture.BusinessId:D}",
            negotiation.ClientAccessUri.Query,
            StringComparison.Ordinal);
        Assert.Contains(
            $"device={fixture.DeviceId:D}",
            negotiation.ClientAccessUri.Query,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Device_without_sync_permission_cannot_negotiate_push()
    {
        using var client = fixture.CreateClient();
        using var request = Request(
            fixture.DeniedDeviceId,
            ServerSliceFixture.DeniedDeviceSecret,
            fixture.BusinessId);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static HttpRequestMessage Request(Guid deviceId, string secret, Guid businessId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/synchronization/negotiate?businessId={businessId:D}");
        request.Headers.Add("X-Auraly-Device-Id", deviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", secret);
        return request;
    }
}
