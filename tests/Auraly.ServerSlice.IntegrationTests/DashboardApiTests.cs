using System.Net;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class DashboardApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Dashboard_endpoints_load_for_an_authorized_business()
    {
        using var client = fixture.CreateAdminClient("dashboard.read");
        var business = Uri.EscapeDataString(fixture.BusinessId.ToString("D"));
        var paths = new[]
        {
            (Path: $"/api/v1/dashboard/stats?businessId={business}&period=30d", Nullable: false),
            (Path: $"/api/v1/dashboard/revenue-chart?businessId={business}&period=daily", Nullable: false),
            (Path: $"/api/v1/dashboard/overview-chart?businessId={business}&period=30d", Nullable: false),
            (Path: $"/api/v1/dashboard/top-services?businessId={business}&limit=4", Nullable: false),
            (Path: $"/api/v1/dashboard/recent-reservations?businessId={business}&limit=5", Nullable: false),
            (Path: $"/api/v1/dashboard/usage?businessId={business}", Nullable: true)
        };

        foreach (var endpoint in paths)
        {
            using var response = await client.GetAsync(endpoint.Path);
            Assert.True(
                response.StatusCode == HttpStatusCode.OK ||
                endpoint.Nullable && response.StatusCode == HttpStatusCode.NoContent,
                $"{endpoint.Path}: {await response.Content.ReadAsStringAsync()}");
        }
    }
}
