using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OnlineRegisterApiTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Authenticated_user_lists_and_selects_a_register_with_derived_warehouse()
    {
        using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);

        var options = await client.GetFromJsonAsync<OnlineRegisterOption[]>(
            "/api/commerce/v1/pos/register-context/options");

        Assert.NotNull(options);
        Assert.Equal(
            options.Length, options.Select(value => value.RegisterId).Distinct().Count());
        var edge = Assert.Single(options, value => value.RegisterId == fixture.RegisterId);
        Assert.True(edge.HasActiveEdgeEnrollment);
        var option = Assert.Single(
            options,
            value => value.RegisterId == fixture.OnlineRegisterId);
        Assert.Equal(fixture.BusinessId, option.BusinessId);
        Assert.Equal(fixture.LocationId, option.LocationId);
        Assert.Equal(fixture.WarehouseId, option.WarehouseId);
        Assert.False(option.HasActiveEdgeEnrollment);

        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/register-context/select",
            new OnlineRegisterSelection(
                option.BusinessId,
                option.LocationId,
                option.RegisterId));
        response.EnsureSuccessStatusCode();
        var selected = await response.Content.ReadFromJsonAsync<OnlineRegisterContext>();
        Assert.NotNull(selected);
        Assert.Equal(option.WarehouseId, selected.WarehouseId);
        Assert.Equal(option.RegisterCode, selected.RegisterCode);
    }

    [Fact]
    public async Task Bootstrap_returns_identity_and_registers_in_one_authenticated_call()
    {
        using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);

        var bootstrap = await client.GetFromJsonAsync<OnlineRegisterBootstrap>(
            "/api/commerce/v1/pos/register-context/bootstrap");

        Assert.NotNull(bootstrap);
        Assert.Equal("Cajero de pruebas", bootstrap.UserDisplayName);
        Assert.Contains(
            bootstrap.Options,
            option =>
                option.RegisterId == fixture.OnlineRegisterId &&
                option.BusinessId == fixture.BusinessId);
    }

    [Fact]
    public async Task User_without_sales_permission_cannot_list_registers()
    {
        using var client = fixture.CreateAdminClient();

        using var response = await client.GetAsync(
            "/api/commerce/v1/pos/register-context/options");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Selection_cannot_replace_business_location_or_register_scope()
    {
        using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);

        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/register-context/select",
            new OnlineRegisterSelection(
                Guid.NewGuid(),
                fixture.LocationId,
                fixture.RegisterId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Register_with_active_edge_enrollment_cannot_be_selected_online()
    {
        using var client = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        using var response = await client.PostAsJsonAsync(
            "/api/commerce/v1/pos/register-context/select",
            new OnlineRegisterSelection(
                fixture.BusinessId,
                fixture.LocationId,
                fixture.RegisterId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
