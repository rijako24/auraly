using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Api;
using Microsoft.Extensions.DependencyInjection;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ExecutionContextTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Lists_only_tenants_and_businesses_assigned_through_roles()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        await SeedAdditionalMembership(tenantId, businessId, roleId);
        try
        {
            using var client = fixture.CreateAdminClient();
            client.DefaultRequestHeaders.Remove("X-Business-Id");
            var tenants = await client.GetFromJsonAsync<List<TenantOption>>(
                "/api/v1/execution-context/tenants");

            Assert.NotNull(tenants);
            Assert.Contains(tenants, item => item.TenantId == fixture.TenantId);
            Assert.Contains(tenants, item => item.TenantId == tenantId);

            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));
            var businesses = await client.GetFromJsonAsync<List<BusinessOption>>(
                "/api/v1/execution-context/businesses");

            var selected = Assert.Single(businesses!);
            Assert.Equal(businessId, selected.BusinessId);
            Assert.Equal(tenantId, selected.TenantId);
        }
        finally
        {
            await DeleteAdditionalMembership(tenantId, businessId, roleId);
        }
    }

    [Fact]
    public async Task Opens_a_work_session_in_an_assigned_business_of_another_tenant()
    {
        var tenantId = Guid.NewGuid();
        var businessId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        await SeedCrossTenantWorkSessionScope(tenantId, businessId, warehouseId, roleId);
        try
        {
            using var client = fixture.CreateAdminClientWithBusinessHeader(
                businessId, "work-sessions.open", "work-sessions.read");
            client.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.ToString("D"));

            using var response = await client.PostAsJsonAsync(
                "/api/commerce/v1/work-sessions/current",
                new OpenWorkSessionRequest(businessId, warehouseId, null));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var session = await response.Content.ReadFromJsonAsync<WorkSessionView>();
            Assert.NotNull(session);
            Assert.Equal(businessId, session.BusinessId);
            Assert.Equal(warehouseId, session.WarehouseId);
            Assert.Equal(fixture.UserId, session.UserId);
        }
        finally
        {
            await DeleteCrossTenantWorkSessionScope(tenantId, businessId, roleId);
        }
    }
    [Fact]
    public async Task Legacy_manual_fiscal_configuration_endpoints_are_not_exposed()
    {
        var businessId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        await SeedFiscalConfigurationScope(businessId, roleId);
        try
        {
            using var client = fixture.CreateAdminClientWithBusinessHeader(
                businessId,
                FiscalPermissionCodes.ConfigurationRead,
                FiscalPermissionCodes.ConfigurationManage);
            client.DefaultRequestHeaders.Add("X-Tenant-Id", fixture.TenantId.ToString("D"));
            using var numberingResponse = await client.PutAsJsonAsync(
                $"/api/commerce/v1/fiscal/numbering?businessId={businessId:D}",
                new { initialConsecutive = 250 });
            using var resolutionResponse = await client.PutAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration?businessId={businessId:D}",
                new { authorizationNumber = "legacy" });

            Assert.Equal(HttpStatusCode.NotFound, numberingResponse.StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed, resolutionResponse.StatusCode);
        }
        finally
        {
            await DeleteFiscalConfigurationScope(businessId, roleId);
        }
    }
    [Fact]
    public async Task Rejects_a_tenant_without_membership()
    {
        using var client = fixture.CreateAdminClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Guid.NewGuid().ToString("D"));

        using var response = await client.GetAsync(
            "/api/v1/execution-context/businesses");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Rejects_a_business_that_belongs_to_a_different_selected_tenant()
    {
        var otherTenantId = Guid.NewGuid();
        var otherBusinessId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        await SeedAdditionalMembership(otherTenantId, otherBusinessId, roleId);
        try
        {
            using var client = fixture.CreateAdminClientWithBusinessHeader(otherBusinessId);
            client.DefaultRequestHeaders.Add(
                "X-Tenant-Id", fixture.TenantId.ToString("D"));

            using var response = await client.GetAsync(
                "/api/v1/execution-context/businesses");

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            await DeleteAdditionalMembership(otherTenantId, otherBusinessId, roleId);
        }
    }

    [Fact]
    public async Task Business_owned_domain_roots_have_a_database_foreign_key_to_businesses()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            DECLARE @Expected TABLE(SchemaName SYSNAME NOT NULL,TableName SYSNAME NOT NULL);
            INSERT @Expected(SchemaName,TableName) VALUES
              (N'dbo',N'Warehouses'),
              (N'dbo',N'Orders'),
              (N'dbo',N'SalesDocuments'),
              (N'dbo',N'AccountingEntries'),
              (N'payroll',N'Runs');

            SELECT CONCAT(expected.SchemaName,N'.',expected.TableName)
            FROM @Expected expected
            WHERE NOT EXISTS
            (
                SELECT 1
                FROM sys.foreign_keys fk
                INNER JOIN sys.tables parentTable
                    ON parentTable.object_id=fk.parent_object_id
                INNER JOIN sys.schemas parentSchema
                    ON parentSchema.schema_id=parentTable.schema_id
                INNER JOIN sys.tables referencedTable
                    ON referencedTable.object_id=fk.referenced_object_id
                INNER JOIN sys.schemas referencedSchema
                    ON referencedSchema.schema_id=referencedTable.schema_id
                WHERE parentSchema.name=expected.SchemaName
                  AND parentTable.name=expected.TableName
                  AND referencedSchema.name=N'dbo'
                  AND referencedTable.name=N'Businesses'
            );
            """, connection);

        var missing = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            missing.Add(reader.GetString(0));

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Production_context_resolves_permissions_from_persisted_roles_only()
    {
        var directory = fixture.Services.GetRequiredService<SqlExecutionContextDirectory>();

        var access = await directory.ResolveAccessAsync(
            fixture.UserId,
            fixture.TenantId,
            fixture.BusinessId,
            CancellationToken.None);

        Assert.True(access.IsAllowed);
        Assert.DoesNotContain("forged.permission", access.Permissions);
    }

    private async Task SeedAdditionalMembership(
        Guid tenantId,
        Guid businessId,
        Guid roleId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt)
            VALUES(@TenantId,N'Tenant adicional',@Email,1,SYSUTCDATETIME());
            INSERT dbo.Businesses
              (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES
              (@BusinessId,@TenantId,N'Negocio adicional',N'Prueba',N'Bogotá',N'3000000000',
               @BusinessEmail,N'https://auraly.test',1,SYSUTCDATETIME());
            INSERT dbo.AppRoles
              (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
            VALUES
              (@RoleId,@TenantId,N'Operador',N'OPERADOR',N'Acceso de prueba',1,0,SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@Email", $"tenant-{tenantId:N}@auraly.test");
        command.Parameters.AddWithValue("@BusinessEmail", $"business-{businessId:N}@auraly.test");
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteAdditionalMembership(
        Guid tenantId,
        Guid businessId,
        Guid roleId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            DELETE dbo.UserRoles WHERE UserId=@UserId AND RoleId=@RoleId;
            DELETE dbo.AppRoles WHERE RoleId=@RoleId;
            DELETE dbo.Businesses WHERE BusinessId=@BusinessId;
            DELETE dbo.Tenants WHERE TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedCrossTenantWorkSessionScope(
        Guid tenantId,
        Guid businessId,
        Guid warehouseId,
        Guid roleId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            UPDATE dbo.WorkSessions SET Status=N'Closed',ClosedAt=SYSDATETIMEOFFSET() WHERE UserId=@UserId AND Status=N'Open';
            INSERT dbo.Tenants(TenantId,Name,Email,IsActive,CreatedAt)
            VALUES(@TenantId,N'Tenant POS',@Email,1,SYSUTCDATETIME());
            INSERT dbo.Businesses
              (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES
              (@BusinessId,@TenantId,N'Sede POS',N'Prueba',N'Bogotá',N'3000000000',
               @BusinessEmail,N'https://auraly.test',1,SYSUTCDATETIME());
            INSERT dbo.Warehouses
              (WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsActive,CreatedAt)
            VALUES
              (@WarehouseId,@BusinessId,N'PRINCIPAL',N'Bodega principal',1,1,SYSUTCDATETIME());
            INSERT dbo.AppRoles
              (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
            VALUES
              (@RoleId,@TenantId,N'Cajero',N'CAJERO',N'Acceso POS',1,0,SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@RoleId,PermissionId,SYSUTCDATETIME()
            FROM dbo.Permissions
            WHERE Resource IN (N'work-sessions.open',N'work-sessions.read');
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@Email", $"tenant-{tenantId:N}@auraly.test");
        command.Parameters.AddWithValue("@BusinessEmail", $"business-{businessId:N}@auraly.test");
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteCrossTenantWorkSessionScope(
        Guid tenantId,
        Guid businessId,
        Guid roleId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            DELETE dbo.WorkSessions WHERE UserId=@UserId AND BusinessId=@BusinessId;
            UPDATE dbo.WorkSessions SET Status=N'Open',ClosedAt=NULL WHERE WorkSessionId=@FixtureWorkSessionId;
            DELETE dbo.UserRoles WHERE UserId=@UserId AND RoleId=@RoleId;
            DELETE dbo.AppRoles WHERE RoleId=@RoleId;
            DELETE dbo.Warehouses WHERE BusinessId=@BusinessId;
            DELETE dbo.Businesses WHERE BusinessId=@BusinessId;
            DELETE dbo.Tenants WHERE TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@FixtureWorkSessionId", fixture.WorkSessionId);
        await command.ExecuteNonQueryAsync();
    }
    private async Task SeedFiscalConfigurationScope(Guid businessId, Guid roleId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            INSERT dbo.Businesses
              (BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,IsActive,CreatedAt)
            VALUES
              (@BusinessId,@TenantId,N'Sede fiscal',N'Prueba',N'Bogotá',N'3000000000',
               @BusinessEmail,N'https://auraly.test',1,SYSUTCDATETIME());
            INSERT dbo.AppRoles
              (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
            VALUES
              (@RoleId,@TenantId,N'Fiscal',N'FISCAL',N'Configuración fiscal',1,0,SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,@BusinessId,SYSUTCDATETIME());
            INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),@RoleId,PermissionId,SYSUTCDATETIME()
            FROM dbo.Permissions
            WHERE Resource IN (N'fiscal.configuration.read',N'fiscal.configuration.manage');
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        command.Parameters.AddWithValue("@BusinessEmail", $"fiscal-{businessId:N}@auraly.test");
        await command.ExecuteNonQueryAsync();
    }

    private async Task DeleteFiscalConfigurationScope(Guid businessId, Guid roleId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            DELETE dbo.SalesDocuments WHERE BusinessId=@BusinessId;
            DELETE dbo.DocumentSeries WHERE BusinessId=@BusinessId;
            DELETE dbo.Warehouses WHERE BusinessId=@BusinessId;
            DELETE c FROM dbo.FiscalSeriesCursors c
            JOIN dbo.FiscalSeries s ON s.SeriesId=c.SeriesId
            WHERE s.BusinessId=@BusinessId;
            DELETE dbo.FiscalSeries WHERE BusinessId=@BusinessId;
            DELETE dbo.FiscalAuthorizations WHERE BusinessId=@BusinessId;
            DELETE dbo.UserRoles WHERE UserId=@UserId AND RoleId=@RoleId;
            DELETE dbo.AppRoles WHERE RoleId=@RoleId;
            DELETE dbo.Businesses WHERE BusinessId=@BusinessId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@RoleId", roleId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        await command.ExecuteNonQueryAsync();
    }
    private sealed record TenantOption(Guid TenantId, string Name);
    private sealed record BusinessOption(Guid BusinessId, Guid TenantId, string Name);
}
