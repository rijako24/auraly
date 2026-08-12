using Auraly.Contracts.Catalog;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class UnifiedApiHostTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Platform_and_commerce_endpoints_share_the_same_authenticated_host()
    {
        using var client = fixture.CreateAdminClientWithBusinessHeader(
            fixture.BusinessId,
            "businesses.read",
            "tenants.read",
            CatalogPermissionCodes.Read);

        using var businesses = await client.GetAsync(
            "/api/v1/businesses?page=1&pageSize=10");
        var businessesBody = await businesses.Content.ReadAsStringAsync();
        Assert.True(
            businesses.IsSuccessStatusCode,
            $"Platform endpoint failed: {businesses.StatusCode}: {businessesBody}");

        using var products = await client.GetAsync(
            "/api/commerce/v1/products?pageSize=10");
        var productsBody = await products.Content.ReadAsStringAsync();
        Assert.True(
            products.IsSuccessStatusCode,
            $"Commerce endpoint failed: {products.StatusCode}: {productsBody}");

        Assert.Equal(
            businesses.RequestMessage?.RequestUri?.Authority,
            products.RequestMessage?.RequestUri?.Authority);
    }
}