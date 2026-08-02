using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class SalesWorkspaceApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Authenticated_user_lists_and_selects_a_business_warehouse()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);

        var options = await client.GetFromJsonAsync<SalesWorkspaceOption[]>(
            "/api/commerce/v1/pos/workspace/options");

        Assert.NotNull(options);
        var option = Assert.Single(
            options,
            value => value.BusinessId == fixture.BusinessId &&
                     value.WarehouseId == fixture.WarehouseId);
        Assert.Equal("B01", option.WarehouseCode);

        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/workspace/select",
            new SalesWorkspaceSelection(
                option.BusinessId,
                option.WarehouseId));
        response.EnsureSuccessStatusCode();
        var selected =
            await response.Content.ReadFromJsonAsync<SalesWorkspaceContext>();
        Assert.NotNull(selected);
        Assert.Equal(option.BusinessId, selected.BusinessId);
        Assert.Equal(option.WarehouseId, selected.WarehouseId);
        Assert.Equal(option.WarehouseAllowsNegativeStockSales,
            selected.WarehouseAllowsNegativeStockSales);
    }

    [Fact]
    public async Task Bootstrap_returns_identity_and_workspaces_in_one_call()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);

        var bootstrap = await client.GetFromJsonAsync<SalesWorkspaceBootstrap>(
            "/api/commerce/v1/pos/workspace/bootstrap");

        Assert.NotNull(bootstrap);
        Assert.Equal("Cajero de pruebas", bootstrap.UserDisplayName);
        Assert.False(bootstrap.CanEnrollPosDevice);
        Assert.Contains(
            bootstrap.Options,
            option =>
                option.BusinessId == fixture.BusinessId &&
                option.WarehouseId == fixture.WarehouseId);
    }

    [Fact]
    public async Task User_without_sales_permission_cannot_list_workspaces()
    {
        using var client = fixture.CreateAdminClient();

        using var response = await client.GetAsync(
            "/api/commerce/v1/pos/workspace/options");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Selection_cannot_escape_the_authenticated_tenant()
    {
        using var client = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate);

        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/workspace/select",
            new SalesWorkspaceSelection(
                Guid.NewGuid(),
                fixture.WarehouseId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}