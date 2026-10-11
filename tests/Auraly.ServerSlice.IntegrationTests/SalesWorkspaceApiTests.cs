using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Auraly.Application.Fiscal;
using Auraly.Api;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

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
        using (var scope = fixture.CreateScope())
        {
            var fiscal = await scope.ServiceProvider
                .GetRequiredService<IFiscalConfigurationStore>()
                .GetAsync(fixture.TenantId, fixture.BusinessId, CancellationToken.None);
            Assert.Equal(fiscal.IsReadyForOnlineSales, option.FiscalReadyForOnlineSales);
            Assert.Equal(fiscal.IsReadyForEnrollment, option.FiscalReadyForEnrollment);
            Assert.Equal(fiscal.HasDianDocumentQuota, option.HasDianDocumentQuota);
        }

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

        using var response = await client.GetAsync(
            "/api/commerce/v1/pos/workspace/bootstrap");
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        var bootstrap = await response.Content.ReadFromJsonAsync<SalesWorkspaceBootstrap>();

        Assert.NotNull(bootstrap);
        Assert.Equal("Cajero de pruebas", bootstrap.UserDisplayName);
        Assert.False(bootstrap.CanEnrollPosDevice);
        Assert.True(bootstrap.MaximumEnrolledDevices >= bootstrap.ActiveEnrolledDeviceCount);
        Assert.Contains("permiso", bootstrap.EnrollmentUnavailableReason, StringComparison.OrdinalIgnoreCase);
        var option = Assert.Single(
            bootstrap.Options,
            option =>
                option.BusinessId == fixture.BusinessId &&
                option.WarehouseId == fixture.WarehouseId);
        Assert.Equal(fixture.BusinessId, option.BusinessId);
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
    public async Task Waiter_only_bootstrap_lists_restaurant_mode_without_cashier_setup()
    {
        using var waiter = fixture.CreateAdminClient("restaurant.attend");
        var selection = new SalesWorkspaceSelection(
            fixture.BusinessId, fixture.WarehouseId);
        using (var direct = await waiter.GetAsync(
            "/api/commerce/v1/pos/workspace/bootstrap"))
        {
            direct.EnsureSuccessStatusCode();
            var bootstrap = await direct.Content.ReadFromJsonAsync<
                SalesWorkspaceBootstrap>();
            Assert.DoesNotContain(bootstrap!.Options,
                item => item.BusinessId == fixture.BusinessId);
            Assert.False(bootstrap.CanEnrollPosDevice);
        }
        using (var denied = await waiter.PostAsJsonAsync(
            "/api/commerce/v1/pos/workspace/select", selection))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SetModeAsync("RestaurantBar");
        try
        {
            var bootstrap = await waiter.GetFromJsonAsync<
                SalesWorkspaceBootstrap>(
                "/api/commerce/v1/pos/workspace/bootstrap");
            var option = Assert.Single(bootstrap!.Options,
                item => item.BusinessId == fixture.BusinessId &&
                    item.WarehouseId == fixture.WarehouseId);
            Assert.Equal("RestaurantBar", option.PointOfSaleMode);
            Assert.False(bootstrap.CanEnrollPosDevice);
            using var chosen = await waiter.PostAsJsonAsync(
                "/api/commerce/v1/pos/workspace/select", selection);
            chosen.EnsureSuccessStatusCode();
            var context = await chosen.Content.ReadFromJsonAsync<
                SalesWorkspaceContext>();
            Assert.Equal("RestaurantBar", context!.PointOfSaleMode);
        }
        finally
        {
            await SetModeAsync("DirectSale");
        }
    }

    [Fact]
    public async Task Command_operator_negotiates_push_only_for_a_restaurant_business()
    {
        using var kitchen = fixture.CreateAdminClient("restaurant.commands.read");
        var negotiationPath =
            "/api/commerce/v1/restaurant/command-cards/synchronization/negotiate";
        using (var direct = await kitchen.PostAsync(negotiationPath, null))
            Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
        using (var options = await kitchen.GetAsync(
            "/api/commerce/v1/pos/workspace/options"))
            Assert.Equal(HttpStatusCode.Forbidden, options.StatusCode);

        await SetModeAsync("RestaurantBar");
        try
        {
            await SetWarehouseSalesAsync(false);
            using var allowed = await kitchen.PostAsync(negotiationPath, null);
            allowed.EnsureSuccessStatusCode();
            var connection = await allowed.Content.ReadFromJsonAsync<
                PosSynchronizationNegotiationResponse>();
            Assert.NotNull(connection);
            Assert.Equal("wss", connection.ClientAccessUri.Scheme);
            Assert.Contains($"business={fixture.BusinessId:D}",
                connection.ClientAccessUri.Query, StringComparison.Ordinal);
            Assert.Contains("stream=RestaurantCommands",
                connection.ClientAccessUri.Query, StringComparison.Ordinal);
            using var generic = await kitchen.PostAsync(
                $"/api/commerce/v1/pos/workspace/synchronization/negotiate?businessId={fixture.BusinessId:D}",
                null);
            Assert.Equal(HttpStatusCode.Forbidden, generic.StatusCode);
        }
        finally
        {
            await SetWarehouseSalesAsync(true);
            await SetModeAsync("DirectSale");
        }
    }

    private async Task SetWarehouseSalesAsync(bool enabled)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Warehouses SET UseForSales=@Enabled
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId;
            """, connection);
        command.Parameters.AddWithValue("@Enabled", enabled);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task SetModeAsync(string mode)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.Businesses SET PointOfSaleMode=@Mode
            WHERE BusinessId=@BusinessId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@Mode", mode);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await command.ExecuteNonQueryAsync();
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
