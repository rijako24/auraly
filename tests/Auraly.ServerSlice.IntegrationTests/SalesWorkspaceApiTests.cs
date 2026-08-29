using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Microsoft.Data.SqlClient;

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
        Assert.True(bootstrap.MaximumEnrolledDevices >= bootstrap.ActiveEnrolledDeviceCount);
        Assert.Contains("permiso", bootstrap.EnrollmentUnavailableReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            bootstrap.Options,
            option =>
                option.BusinessId == fixture.BusinessId &&
                option.WarehouseId == fixture.WarehouseId);
    }

    [Fact]
    public async Task Bootstrap_disables_enrollment_when_tenant_capacity_is_exhausted()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var read = new SqlCommand("""
            SELECT t.MaximumEnrolledDevices,
                   (SELECT COUNT(*) FROM dbo.EnrolledDevices d
                    WHERE d.TenantId=t.TenantId AND d.IsActive=1)
            FROM dbo.Tenants t WHERE t.TenantId=@TenantId;
            """, connection);
        read.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await using var reader = await read.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var originalMaximum = reader.GetInt32(0);
        var active = reader.GetInt32(1);
        await reader.DisposeAsync();

        try
        {
            await using var exhaust = new SqlCommand(
                "UPDATE dbo.Tenants SET MaximumEnrolledDevices=@Active WHERE TenantId=@TenantId;",
                connection);
            exhaust.Parameters.AddWithValue("@Active", active);
            exhaust.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            Assert.Equal(1, await exhaust.ExecuteNonQueryAsync());

            using var client = fixture.CreateAdminClient(
                CommercePermissionCodes.SalesCreate,
                CommercePermissionCodes.EnrolledDevicesEnroll);
            var bootstrap = await client.GetFromJsonAsync<SalesWorkspaceBootstrap>(
                "/api/commerce/v1/pos/workspace/bootstrap");

            Assert.NotNull(bootstrap);
            Assert.False(bootstrap.CanEnrollPosDevice);
            Assert.Equal(active, bootstrap.ActiveEnrolledDeviceCount);
            Assert.Equal(active, bootstrap.MaximumEnrolledDevices);
            Assert.Contains("administrador", bootstrap.EnrollmentUnavailableReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await using var restore = new SqlCommand(
                "UPDATE dbo.Tenants SET MaximumEnrolledDevices=@Maximum WHERE TenantId=@TenantId;",
                connection);
            restore.Parameters.AddWithValue("@Maximum", originalMaximum);
            restore.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await restore.ExecuteNonQueryAsync();
        }
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
    public async Task Changing_the_active_sales_site_requires_its_own_supervisor_permission()
    {
        var selection = new SalesWorkspaceSelection(fixture.BusinessId, fixture.WarehouseId);
        using var cashier = fixture.CreateAdminClient(CommercePermissionCodes.SalesCreate);
        using var denied = await cashier.PostAsJsonAsync("/api/commerce/v1/pos/workspace/change", selection);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        using var supervisor = fixture.CreateAdminClient(
            CommercePermissionCodes.SalesCreate,
            CommercePermissionCodes.PosWorkspaceChange);
        using var allowed = await supervisor.PostAsJsonAsync("/api/commerce/v1/pos/workspace/change", selection);
        allowed.EnsureSuccessStatusCode();
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

    [Fact]
    public async Task System_warehouse_not_enabled_for_sales_is_hidden_and_cannot_be_selected()
    {
        var warehouseId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var insert = new SqlCommand("""
                INSERT dbo.Warehouses(
                  WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
                  IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,
                  IsActive,CreatedAt)
                VALUES(
                  @WarehouseId,@BusinessId,N'PED-TEST',N'Pedidos internos',0,
                  1,0,0,0,1,SYSDATETIMEOFFSET());
                """, connection);
            insert.Parameters.AddWithValue("@WarehouseId", warehouseId);
            insert.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await insert.ExecuteNonQueryAsync();
        }

        try
        {
            using var client = fixture.CreateAdminClient(
                CommercePermissionCodes.SalesCreate);
            var options = await client.GetFromJsonAsync<SalesWorkspaceOption[]>(
                "/api/commerce/v1/pos/workspace/options");
            Assert.NotNull(options);
            Assert.DoesNotContain(options, option => option.WarehouseId == warehouseId);

            using var response = await client.PostAsJsonAsync(
                "/api/commerce/v1/pos/workspace/select",
                new SalesWorkspaceSelection(fixture.BusinessId, warehouseId));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var delete = new SqlCommand(
                "DELETE dbo.Warehouses WHERE WarehouseId=@WarehouseId;", connection);
            delete.Parameters.AddWithValue("@WarehouseId", warehouseId);
            await delete.ExecuteNonQueryAsync();
        }
    }
}
