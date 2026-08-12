using System.Net;
using Auraly.Contracts.Catalog;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class BusinessExecutionContextTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Canonical_session_uses_the_validated_business_header()
    {
        using var client = fixture.CreateAdminClientWithBusinessHeader(
            fixture.BusinessId,
            CatalogPermissionCodes.Read);

        using var response = await client.GetAsync(
            "/api/commerce/v1/products?pageSize=10");

        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
    }

    [Fact]
    public async Task Canonical_session_rejects_an_unassigned_business_header()
    {
        using var client = fixture.CreateAdminClientWithBusinessHeader(
            Guid.NewGuid(),
            CatalogPermissionCodes.Read);

        using var response = await client.GetAsync(
            "/api/commerce/v1/products?pageSize=10");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("Acceso denegado", problem, StringComparison.OrdinalIgnoreCase);
    }
}
