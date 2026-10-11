using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Auraly.Platform.Application.Identity.DTOs;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class RestaurantConfigurationTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Existing_business_is_direct_sale_and_old_updates_preserve_its_explicit_mode()
    {
        var businessId = await CreateBusinessAsync();
        using var client = fixture.CreateAdminClient("businesses.read", "businesses.update");
        var original = await client.GetFromJsonAsync<BusinessDto>($"/api/v1/businesses/{businessId}");
        Assert.Equal("DirectSale", original!.PointOfSaleMode);

        using var change = await client.PutAsJsonAsync($"/api/v1/businesses/{businessId}",
            new { pointOfSaleMode = "RestaurantBar" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal("RestaurantBar", (await change.Content.ReadFromJsonAsync<BusinessDto>())!.PointOfSaleMode);

        using var legacyUpdate = await client.PutAsJsonAsync($"/api/v1/businesses/{businessId}",
            new { name = "Sede actualizada" });
        Assert.Equal(HttpStatusCode.OK, legacyUpdate.StatusCode);
        Assert.Equal("RestaurantBar", (await legacyUpdate.Content.ReadFromJsonAsync<BusinessDto>())!.PointOfSaleMode);
        var unchanged = await client.GetFromJsonAsync<BusinessDto>($"/api/v1/businesses/{fixture.BusinessId}");
        Assert.Equal("DirectSale", unchanged!.PointOfSaleMode);
    }

    [Fact]
    public async Task Waiter_access_options_are_per_business_and_reject_unverified_exclusivity()
    {
        var businessId = await CreateBusinessAsync();
        using var client = fixture.CreateAdminClient("businesses.read", "businesses.update");
        using var mode = await client.PutAsJsonAsync($"/api/v1/businesses/{businessId}",
            new { pointOfSaleMode = "RestaurantBar", restaurantRequireWaiterCredential = false });
        Assert.Equal(HttpStatusCode.OK, mode.StatusCode);
        Assert.False((await mode.Content.ReadFromJsonAsync<BusinessDto>())!.RestaurantRequireWaiterCredential);

        using var rejected = await client.PutAsJsonAsync($"/api/v1/businesses/{businessId}",
            new { restaurantRestrictTableToWaiter = true });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.False((await client.GetFromJsonAsync<BusinessDto>($"/api/v1/businesses/{businessId}"))!
            .RestaurantRestrictTableToWaiter);

        using var enabled = await client.PutAsJsonAsync($"/api/v1/businesses/{businessId}",
            new { restaurantRequireWaiterCredential = true, restaurantRestrictTableToWaiter = true });
        Assert.Equal(HttpStatusCode.OK, enabled.StatusCode);
        var saved = await enabled.Content.ReadFromJsonAsync<BusinessDto>();
        Assert.True(saved!.RestaurantRequireWaiterCredential);
        Assert.True(saved.RestaurantRestrictTableToWaiter);
        Assert.True((await client.GetFromJsonAsync<BusinessDto>($"/api/v1/businesses/{businessId}"))!
            .RestaurantRestrictTableToWaiter);
        Assert.False((await client.GetFromJsonAsync<BusinessDto>($"/api/v1/businesses/{fixture.BusinessId}"))!
            .RestaurantRestrictTableToWaiter);
    }

    [Fact]
    public async Task New_restaurant_persists_credential_optional_choice_instead_of_database_default()
    {
        using var client = fixture.CreateAdminClient("businesses.create", "businesses.read");
        using var created = await client.PostAsJsonAsync("/api/v1/businesses", new
        {
            name = "Restaurante sin clave",
            description = "",
            address = "Calle 1",
            phone = "1234567",
            email = "test@example.com",
            website = "",
            priceSourceBusinessId = fixture.BusinessId,
            pointOfSaleMode = "RestaurantBar",
            restaurantRequireWaiterCredential = false,
            restaurantRestrictTableToWaiter = false
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var business = await created.Content.ReadFromJsonAsync<BusinessDto>();
        Assert.False(business!.RestaurantRequireWaiterCredential);
        var reloaded = await client.GetFromJsonAsync<BusinessDto>($"/api/v1/businesses/{business.BusinessId}");
        Assert.False(reloaded!.RestaurantRequireWaiterCredential);
    }

    [Fact]
    public async Task Mode_change_requires_business_permission_and_cannot_cross_tenants()
    {
        var businessId = await CreateBusinessAsync();
        using var waiter = fixture.CreateAdminClient("restaurant.attend");
        using var forbidden = await waiter.PutAsJsonAsync($"/api/v1/businesses/{businessId}",
            new { pointOfSaleMode = "RestaurantBar" });
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var foreignBusiness = new SqlCommand(
            "SELECT TOP(1) BusinessId FROM dbo.Businesses WHERE TenantId<>@TenantId ORDER BY BusinessId;", connection);
        foreignBusiness.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        var foreignBusinessId = Assert.IsType<Guid>(await foreignBusiness.ExecuteScalarAsync());
        using var authorized = fixture.CreateAdminClient("businesses.update");
        using var missing = await authorized.PutAsJsonAsync($"/api/v1/businesses/{foreignBusinessId}",
            new { pointOfSaleMode = "RestaurantBar" });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("restaurantbar")]
    [InlineData("")]
    public async Task Invalid_mode_is_rejected_without_changing_the_business(string mode)
    {
        var businessId = await CreateBusinessAsync();
        using var client = fixture.CreateAdminClient("businesses.update", "businesses.read");
        using var response = await client.PutAsJsonAsync($"/api/v1/businesses/{businessId}",
            new { pointOfSaleMode = mode });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("DirectSale", (await client.GetFromJsonAsync<BusinessDto>(
            $"/api/v1/businesses/{businessId}"))!.PointOfSaleMode);
    }

    [Fact]
    public async Task Additive_column_backfills_every_existing_business_and_seed_reruns_preserve_choices()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        // Simulate the previous schema with real historical rows, entirely inside
        // a rolled-back DDL transaction in the isolated local fixture database.
        var schema = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(),
            "database", "Auraly.Database", "Tables", "Businesses.sql"));
        var column = schema.Split('\n').Single(line => line.TrimStart().StartsWith("[PointOfSaleMode]"))
            .Trim().TrimEnd(',');
        await ExecuteAsync(connection, transaction, """
            ALTER TABLE dbo.Businesses DROP CONSTRAINT CK_Businesses_PointOfSaleMode;
            ALTER TABLE dbo.Businesses DROP CONSTRAINT DF_Businesses_PointOfSaleMode;
            ALTER TABLE dbo.Businesses DROP COLUMN PointOfSaleMode;
            """ + $"\nALTER TABLE dbo.Businesses ADD {column};");
        Assert.Equal(0, await ScalarAsync(connection, transaction,
            "SELECT COUNT(*) FROM dbo.Businesses WHERE PointOfSaleMode IS NULL OR PointOfSaleMode<>N'DirectSale';"));
        await ExecuteAsync(connection, transaction,
            "UPDATE dbo.Businesses SET PointOfSaleMode=N'RestaurantBar' WHERE BusinessId=@BusinessId;",
            new SqlParameter("@BusinessId", fixture.BusinessId));
        await RunSeedAsync(connection, transaction);
        await RunSeedAsync(connection, transaction);
        Assert.Equal(1, await ScalarAsync(connection, transaction,
            "SELECT COUNT(*) FROM dbo.Businesses WHERE BusinessId=@BusinessId AND PointOfSaleMode=N'RestaurantBar';",
            new SqlParameter("@BusinessId", fixture.BusinessId)));
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Waiter_template_has_no_financial_permission_and_reruns_preserve_customization()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, transaction, "EXEC dbo.RestaurantWaiterRoleEnsure @TenantId=@TenantId;",
            new SqlParameter("@TenantId", fixture.TenantId));
        Assert.Equal(1, await ScalarAsync(connection, transaction, """
            SELECT COUNT(*) FROM dbo.RolePermissions assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
            JOIN dbo.Permissions permission ON permission.PermissionId=assignment.PermissionId
            WHERE role.TenantId=@TenantId AND role.NormalizedName=N'WAITER'
              AND permission.Resource=N'restaurant.attend';
            """, new SqlParameter("@TenantId", fixture.TenantId)));
        Assert.Equal(0, await ScalarAsync(connection, transaction, """
            SELECT COUNT(*) FROM dbo.RolePermissions assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
            JOIN dbo.Permissions permission ON permission.PermissionId=assignment.PermissionId
            WHERE role.TenantId=@TenantId AND role.NormalizedName=N'WAITER'
              AND permission.Resource<>N'restaurant.attend';
            """, new SqlParameter("@TenantId", fixture.TenantId)));
        var assignmentsBefore = await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM dbo.UserRoles;");
        await ExecuteAsync(connection, transaction, """
            DELETE assignment FROM dbo.RolePermissions assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
            WHERE role.TenantId=@TenantId AND role.NormalizedName=N'WAITER';
            UPDATE dbo.AppRoles SET Name=N'Atención personalizada',IsActive=0
            WHERE TenantId=@TenantId AND NormalizedName=N'WAITER';
            """, new SqlParameter("@TenantId", fixture.TenantId));
        await RunSeedAsync(connection, transaction);
        Assert.Equal(0, await ScalarAsync(connection, transaction, """
            SELECT COUNT(*) FROM dbo.RolePermissions assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
            WHERE role.TenantId=@TenantId AND role.NormalizedName=N'WAITER';
            """, new SqlParameter("@TenantId", fixture.TenantId)));
        Assert.Equal(1, await ScalarAsync(connection, transaction, """
            SELECT COUNT(*) FROM dbo.AppRoles WHERE TenantId=@TenantId
            AND NormalizedName=N'WAITER' AND Name=N'Atención personalizada' AND IsActive=0;
            """, new SqlParameter("@TenantId", fixture.TenantId)));
        Assert.Equal(assignmentsBefore, await ScalarAsync(connection, transaction, "SELECT COUNT(*) FROM dbo.UserRoles;"));
        await transaction.RollbackAsync();
    }

    private async Task<Guid> CreateBusinessAsync()
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
            VALUES(@Id,@TenantId,N'Sede modo POS',N'',N'',N'',N'',N'');
            """, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private static async Task RunSeedAsync(SqlConnection connection, SqlTransaction transaction)
    {
        var script = await File.ReadAllTextAsync(Path.Combine(RepositoryRoot(), "database", "Auraly.Database",
            "Scripts", "Seeds", "SeedRestaurantConfiguration.sql"));
        foreach (var batch in Regex.Split(script, @"^GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            if (!string.IsNullOrWhiteSpace(batch)) await ExecuteAsync(connection, transaction, batch);
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction transaction,
        string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<int> ScalarAsync(SqlConnection connection, SqlTransaction transaction,
        string sql, params SqlParameter[] parameters)
    {
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddRange(parameters);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (Directory.Exists(Path.Combine(directory.FullName, "database", "Auraly.Database"))) return directory.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
