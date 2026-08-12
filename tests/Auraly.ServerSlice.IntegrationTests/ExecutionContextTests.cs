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
    public async Task Fiscal_configuration_starts_at_the_selected_number_and_cannot_restart_after_consumption()
    {
        var businessId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var authorizationNumber = $"TEST-{Guid.NewGuid():N}";
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
                new SaveSalesInvoiceNumberingConfiguration(250));
            Assert.Equal(HttpStatusCode.OK, numberingResponse.StatusCode);

            var request = new SaveFiscalResolutionConfiguration(
                authorizationNumber,
                "900123456",
                2,
                "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=",
                "1",
                null,
                new DateOnly(2026, 1, 1),
                new DateOnly(2028, 12, 31),
                "SET",
                1,
                1_000,
                250,
                true,
                false);

            using var createdResponse = await client.PutAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration?businessId={businessId:D}",
                request);

            Assert.Equal(HttpStatusCode.OK, createdResponse.StatusCode);
            var created = await createdResponse.Content.ReadFromJsonAsync<FiscalResolutionConfiguration>();
            Assert.NotNull(created);
            Assert.Equal(1, created.RangeStart);
            Assert.Equal(1_000, created.RangeEnd);
            Assert.Equal(250, created.InitialConsecutive);
            Assert.Equal(250, created.NextConsecutive);
            Assert.True(created.CanSetInitialConsecutive);
            await SetInitialFiscalConsecutive(businessId, authorizationNumber, null);
            var incomplete = await client.GetFromJsonAsync<FiscalResolutionConfiguration>(
                $"/api/commerce/v1/fiscal/configuration?businessId={businessId:D}");
            Assert.NotNull(incomplete);
            Assert.Null(incomplete.InitialConsecutive);
            Assert.False(incomplete.IsReadyForOnlineSales);
            Assert.False(incomplete.IsReadyForEnrollment);
            await SetInitialFiscalConsecutive(businessId, authorizationNumber, 250);
            await SeedIssuedSalesDocument(businessId, authorizationNumber);
            await SetNextFiscalConsecutive(businessId, authorizationNumber, 251);

            using var numberingRestart = await client.PutAsJsonAsync(
                $"/api/commerce/v1/fiscal/numbering?businessId={businessId:D}",
                new SaveSalesInvoiceNumberingConfiguration(100));
            Assert.Equal(HttpStatusCode.Conflict, numberingRestart.StatusCode);

            using var restartResponse = await client.PutAsJsonAsync(
                $"/api/commerce/v1/fiscal/configuration?businessId={businessId:D}",
                request with { Prefix = "ALT" });

            Assert.Equal(HttpStatusCode.Conflict, restartResponse.StatusCode);
            var current = await client.GetFromJsonAsync<FiscalResolutionConfiguration>(
                $"/api/commerce/v1/fiscal/configuration?businessId={businessId:D}");
            Assert.NotNull(current);
            Assert.Equal(250, current.InitialConsecutive);
            Assert.Equal(251, current.NextConsecutive);
            Assert.False(current.CanSetInitialConsecutive);
        }
        finally
        {
            await DeleteFiscalConfigurationScope(businessId, roleId);
        }
    }
    [Fact]
    public async Task Operational_numbering_can_be_saved_before_the_DIAN_resolution_is_complete()
    {
        var businessId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        await SeedFiscalConfigurationScope(businessId, roleId);
        await SeedIncompleteFiscalAuthorization(businessId);
        try
        {
            using var client = fixture.CreateAdminClientWithBusinessHeader(
                businessId,
                FiscalPermissionCodes.ConfigurationRead,
                FiscalPermissionCodes.ConfigurationManage);
            client.DefaultRequestHeaders.Add("X-Tenant-Id", fixture.TenantId.ToString("D"));

            using var response = await client.PutAsJsonAsync(
                $"/api/commerce/v1/fiscal/numbering?businessId={businessId:D}",
                new SaveSalesInvoiceNumberingConfiguration(17));

            var responseBody = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"Expected OK but received {response.StatusCode}: {responseBody}");
            var saved = JsonSerializer.Deserialize<SalesInvoiceNumberingConfiguration>(
                responseBody,
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Assert.NotNull(saved);
            Assert.Equal(17, saved.InitialConsecutive);
            Assert.Equal(17, saved.NextConsecutive);

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                "SELECT InitialConsecutive FROM dbo.FiscalAuthorizations WHERE BusinessId=@BusinessId;",
                connection);
            command.Parameters.AddWithValue("@BusinessId", businessId);
            Assert.Equal(DBNull.Value, await command.ExecuteScalarAsync());
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

    private async Task SeedIncompleteFiscalAuthorization(Guid businessId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            INSERT dbo.FiscalAuthorizations(
                FiscalAuthorizationId,BusinessId,AuthorizationNumber,SupplierTaxId,
                Environment,QrValidationUrl,TechnicalKeyVersion,ValidFrom,ValidUntil,
                AuthorizedRangeStart,AuthorizedRangeEnd,InitialConsecutive,IsActive,CreatedAt)
            VALUES(
                NEWID(),@BusinessId,N'PENDING-DIAN',N'900123456',2,
                N'https://catalogo-vpfe-hab.dian.gov.co/document/searchqr?documentkey=',
                N'v1','2026-01-01','2028-12-31',NULL,NULL,NULL,1,SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task SeedIssuedSalesDocument(
        Guid businessId,
        string authorizationNumber)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            DECLARE @WarehouseId uniqueidentifier=NEWID(),
                    @DocumentSeriesId uniqueidentifier=NEWID(),
                    @AuthorizationId uniqueidentifier,
                    @FiscalSeriesId uniqueidentifier;
            SELECT @AuthorizationId=FiscalAuthorizationId
            FROM dbo.FiscalAuthorizations
            WHERE BusinessId=@BusinessId AND AuthorizationNumber=@AuthorizationNumber;
            SELECT TOP(1) @FiscalSeriesId=SeriesId
            FROM dbo.FiscalSeries
            WHERE FiscalAuthorizationId=@AuthorizationId
              AND EmitterKind=N'Server' AND DeviceId IS NULL AND IsActive=1;

            INSERT dbo.Warehouses(
                WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsActive,CreatedAt)
            VALUES(@WarehouseId,@BusinessId,N'VENTA-TEST',N'Venta test',1,1,SYSDATETIMEOFFSET());
            INSERT dbo.DocumentSeries(
                DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
            VALUES(@DocumentSeriesId,@BusinessId,NULL,N'SalesInvoice',N'VTA',N'00',
                8,1,99999999,0,1,SYSDATETIMEOFFSET());
            INSERT dbo.SalesDocuments(
                DocumentId,BusinessId,WarehouseId,DeviceId,SourceMode,DocumentSeriesId,
                DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,
                FiscalSeriesId,FiscalAuthorizationId,DocumentType,IdempotencyKey,PayloadHash,
                FiscalNumber,FiscalPrefix,FiscalConsecutive,IssuedAt,CustomerIdentification,
                UntaxedAmount,TaxAmount,PayableAmount,CreditAmount,CufeReceived,CufeCalculated,
                FiscalStatus,ProcessingStatus,ReceivedAt)
            VALUES(NEWID(),@BusinessId,@WarehouseId,NULL,N'Online',@DocumentSeriesId,
                N'VTA00-00000250',N'VTA',N'00',250,@FiscalSeriesId,@AuthorizationId,
                N'SalesInvoice',CONVERT(nvarchar(128),NEWID()),HASHBYTES('SHA2_256',N'fiscal-numbering-test'),
                N'SET250',N'SET',250,SYSDATETIMEOFFSET(),N'222222222222',
                100,19,119,0,REPLICATE(N'A',96),REPLICATE(N'A',96),
                N'FiscalVerified',N'Processed',SYSDATETIMEOFFSET());
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@AuthorizationNumber", authorizationNumber);
        Assert.Equal(3, await command.ExecuteNonQueryAsync());
    }

    private async Task SetInitialFiscalConsecutive(
        Guid businessId,
        string authorizationNumber,
        long? initialConsecutive)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE dbo.FiscalAuthorizations SET InitialConsecutive=@Initial " +
            "WHERE BusinessId=@BusinessId AND AuthorizationNumber=@AuthorizationNumber;",
            connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@AuthorizationNumber", authorizationNumber);
        command.Parameters.AddWithValue(
            "@Initial", (object?)initialConsecutive ?? DBNull.Value);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task SetNextFiscalConsecutive(
        Guid businessId,
        string authorizationNumber,
        long nextConsecutive)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            """
            UPDATE c SET NextConsecutive=@Next,UpdatedAt=SYSDATETIMEOFFSET()
            FROM dbo.FiscalSeriesCursors c
            JOIN dbo.FiscalSeries s ON s.SeriesId=c.SeriesId
            JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=s.FiscalAuthorizationId
            WHERE a.BusinessId=@BusinessId AND a.AuthorizationNumber=@AuthorizationNumber
              AND s.EmitterKind=N'Server' AND s.IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@AuthorizationNumber", authorizationNumber);
        command.Parameters.AddWithValue("@Next", nextConsecutive);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
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
            DELETE dbo.SalesInvoiceNumberingConfigurations WHERE BusinessId=@BusinessId;
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