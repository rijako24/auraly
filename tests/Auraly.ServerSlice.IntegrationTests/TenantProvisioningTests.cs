using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Authentication;
using Microsoft.Data.SqlClient;
using Auraly.Contracts.Tenants;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class TenantProvisioningTests(ServerSliceFixture fixture)
{
    private const string Password = "Auraly-New-Tenant-2026!";

    [Fact]
    public async Task Provision_and_accept_invitation_creates_a_complete_usable_tenant()
    {
        var geography = await ReadGeographyAsync();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var email = $"admin-{suffix}@auraly.test";
        var request = new ProvisionTenantRequest(
            Guid.NewGuid(), $"Empresa {suffix} SAS", $"Empresa {suffix}",
            $"90{Random.Shared.Next(10000000, 99999999)}", "1",
            geography.CountryId, geography.DivisionId, geography.CityId,
            "Calle 1 # 2-3", "3001234567", $"empresa-{suffix}@auraly.test", "R-99-PN",
            "Sede principal", "Calle 1 # 2-3", "3001234567", $"sede-{suffix}@auraly.test",
            "America/Bogota", "LatestReceiptCost", email, 10, 3);

        using var admin = fixture.CreateAdminClient("tenants.create", "tenants.update");
        using var created = await admin.PostAsJsonAsync("/api/v1/tenants", request);
        var creationBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"Expected Created but received {created.StatusCode}: {creationBody}");
        var result = await created.Content.ReadFromJsonAsync<ProvisionTenantResult>();
        Assert.NotNull(result);

        Assert.Null(result!.AdministratorUserId);
        var state = await ReadProvisionedStateAsync(result.TenantId, null);
        Assert.Equal(1, state.Businesses);
        Assert.Equal(2, state.Warehouses);
        Assert.True(state.InventoryReasons >= 12);
        Assert.Equal(4, state.ProductUnits);
        Assert.Equal(1, state.DefaultCustomers);
        Assert.Equal(26, state.AccountingAccounts);
        Assert.Equal(26, state.AccountingMappings);
        Assert.Equal(1, state.OpenAccountingPeriods);
        Assert.Equal(1, state.DefaultCostCenters);
        Assert.Equal(1, state.AccountingVoucherCursors);
        Assert.Equal(4, state.Roles);
        Assert.Equal(0, state.UserRoles);
        Assert.Null(state.UserActive);
        Assert.Null(state.PasswordHash);
        Assert.Equal("Pending", state.InvitationStatus);

        using var attemptedKeyChange = await admin.PutAsJsonAsync(
            $"/api/v1/tenants/{result.TenantId:D}",
            new { tenantKey = $"@changed-{suffix}" });
        Assert.Equal(HttpStatusCode.OK, attemptedKeyChange.StatusCode);
        var unchangedTenant = await attemptedKeyChange.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(result.TenantKey, unchangedTenant.GetProperty("tenantKey").GetString());

        var token = await ReadInvitationTokenAsync(result.TenantId);
        using var publicClient = fixture.CreateClient();
        using var accepted = await publicClient.PostAsJsonAsync(
            "/api/v1/auth/invitations/accept",
            new AcceptTenantInvitationRequest(
                token, "CC", $"10{Random.Shared.Next(10000000, 99999999)}",
                "Administrador", suffix, email, "3007654321", "Calle 1 # 2-3",
                Password, Password));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var acceptedResult = await accepted.Content.ReadFromJsonAsync<AcceptTenantInvitationResult>();
        Assert.NotNull(acceptedResult);
        var acceptedState = await ReadProvisionedStateAsync(result.TenantId, acceptedResult!.UserId);
        Assert.True(acceptedState.UserActive);
        Assert.NotNull(acceptedState.PasswordHash);
        Assert.Equal("Accepted", acceptedState.InvitationStatus);

        using var reused = await publicClient.PostAsJsonAsync(
            "/api/v1/auth/invitations/accept",
            new AcceptTenantInvitationRequest(
                token, "CC", $"10{Random.Shared.Next(10000000, 99999999)}",
                "Administrador", suffix, email, "3007654321", "Calle 1 # 2-3",
                Password, Password));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new AuthenticationLoginRequest(email, result.TenantKey, Password))
        };
        loginRequest.Headers.Add(AuthenticationDefaults.ClientIdHeader, Guid.NewGuid().ToString("D"));
        using var login = await publicClient.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authentication = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(authentication);
        Assert.Equal(result.TenantId, authentication!.User.TenantId);
        Assert.Equal(acceptedResult.UserId, authentication.User.UserId);
        Assert.Contains("Administrador", authentication.User.Roles);
        Assert.DoesNotContain(authentication.User.Permissions, permission =>
            permission.StartsWith("tenants.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Administrator_email_and_identification_are_unique_per_tenant()
    {
        var geography = await ReadGeographyAsync();
        var sharedEmail = $"shared-admin-{Guid.NewGuid():N}@auraly.test";
        var sharedIdentification = $"10{Random.Shared.NextInt64(100000000, 999999999)}";
        using var admin = fixture.CreateAdminClient("tenants.create", "tenants.update");

        async Task<(ProvisionTenantResult Result, string Token)> ProvisionAsync(string suffix)
        {
            var request = new ProvisionTenantRequest(
                Guid.NewGuid(), $"Empresa {suffix} SAS", $"Empresa {suffix}",
                $"9{Random.Shared.NextInt64(100000000, 999999999)}", "1",
                geography.CountryId, geography.DivisionId, geography.CityId,
                "Calle 1 # 2-3", "3001234567", $"empresa-{suffix}@auraly.test", "R-99-PN",
                "Sede principal", "Calle 1 # 2-3", "3001234567", $"sede-{suffix}@auraly.test",
                "America/Bogota", "LatestReceiptCost", sharedEmail, 10, 3);
            using var response = await admin.PostAsJsonAsync("/api/v1/tenants", request);
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.Created,
                $"Expected Created but received {response.StatusCode}: {body}");
            var result = await response.Content.ReadFromJsonAsync<ProvisionTenantResult>();
            Assert.NotNull(result);
            return (result!, await ReadInvitationTokenAsync(result!.TenantId));
        }

        var first = await ProvisionAsync($"one-{Guid.NewGuid():N}"[..14]);
        var second = await ProvisionAsync($"two-{Guid.NewGuid():N}"[..14]);
        Assert.NotEqual(first.Result.TenantId, second.Result.TenantId);

        using var publicClient = fixture.CreateClient();
        async Task<AcceptTenantInvitationResult> AcceptAsync(string token, string lastName)
        {
            using var response = await publicClient.PostAsJsonAsync(
                "/api/v1/auth/invitations/accept",
                new AcceptTenantInvitationRequest(
                    token, "CC", sharedIdentification, "Administrador", lastName,
                    sharedEmail, "3007654321", "Calle 1 # 2-3", Password, Password));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.StatusCode == HttpStatusCode.OK,
                $"Expected OK but received {response.StatusCode}: {body}");
            return await response.Content.ReadFromJsonAsync<AcceptTenantInvitationResult>()
                ?? throw new InvalidOperationException("Invitation acceptance response is missing.");
        }

        var firstAccepted = await AcceptAsync(first.Token, "Primer tenant");
        var secondAccepted = await AcceptAsync(second.Token, "Segundo tenant");
        Assert.True((await ReadProvisionedStateAsync(
            first.Result.TenantId, firstAccepted.UserId)).UserActive);
        Assert.True((await ReadProvisionedStateAsync(
            second.Result.TenantId, secondAccepted.UserId)).UserActive);
    }
    [Fact]
    public async Task Creating_any_later_business_also_provisions_sales_and_orders_warehouses()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        using var admin = fixture.CreateAdminClient("businesses.create");
        using var response = await admin.PostAsJsonAsync(
            "/api/v1/businesses",
            new
            {
                Name = $"Sede norte {suffix}",
                Description = "Segunda sede",
                Address = "Carrera 2 # 3-4",
                Phone = "3000000000",
                Email = $"norte-{suffix}@auraly.test",
                LogoUrl = (string?)null,
                TimeZone = "America/Bogota"
            });
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected Created but received {response.StatusCode}: {body}");
        var business = await response.Content.ReadFromJsonAsync<BusinessCreatedResponse>();
        Assert.NotNull(business);
        Assert.Equal(2, await CountDefaultWarehousesAsync(fixture.TenantId, business!.BusinessId));
        Assert.Equal(1, await CountDefaultCostCentersAsync(fixture.TenantId, business.BusinessId));
    }

    private async Task<(Guid CountryId, Guid DivisionId, Guid CityId)> ReadGeographyAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP(1) c.CountryId,d.AdministrativeDivisionId,ci.CityId
            FROM dbo.Countries c
            INNER JOIN dbo.AdministrativeDivisions d ON d.CountryId=c.CountryId AND d.IsActive=1
            INNER JOIN dbo.Cities ci ON ci.AdministrativeDivisionId=d.AdministrativeDivisionId AND ci.IsActive=1
            WHERE c.IsActive=1 ORDER BY c.Name,d.Name,ci.Name;
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2));
    }

    private async Task<ProvisionedState> ReadProvisionedStateAsync(Guid tenantId, Guid? userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.Businesses WHERE TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.Warehouses w INNER JOIN dbo.Businesses b ON b.BusinessId=w.BusinessId WHERE b.TenantId=@TenantId AND w.Code IN(N'VEN',N'PED')),
              (SELECT COUNT(*) FROM dbo.BusinessReasons r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId WHERE b.TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.ProductUnits u INNER JOIN dbo.Businesses b ON b.BusinessId=u.BusinessId WHERE b.TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.Customers c INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE p.TenantId=@TenantId AND p.DisplayName=N'Consumidor final'),
              (SELECT COUNT(*) FROM dbo.AccountingAccounts WHERE TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND BusinessId IS NULL),
              (SELECT COUNT(*) FROM dbo.AccountingPeriods WHERE TenantId=@TenantId AND Status=N'Open'),
              (SELECT COUNT(*) FROM dbo.AccountingCostCenters c INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId WHERE b.TenantId=@TenantId AND c.IsDefault=1 AND c.IsActive=1),
              (SELECT COUNT(*) FROM dbo.AccountingVoucherCursors WHERE TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.AppRoles WHERE TenantId=@TenantId AND NormalizedName IN(N'CASHIER',N'SUPERVISOR',N'ADMINISTRATIVE',N'ADMINISTRATOR')),
              (SELECT COUNT(*) FROM dbo.UserRoles WHERE UserId=@UserId),
              (SELECT IsActive FROM dbo.AppUsers WHERE TenantId=@TenantId AND UserId=@UserId),
              (SELECT PasswordHash FROM dbo.AppUsers WHERE TenantId=@TenantId AND UserId=@UserId),
              (SELECT TOP(1) Status FROM dbo.TenantUserInvitations WHERE TenantId=@TenantId ORDER BY CreatedAt DESC);
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId.HasValue ? userId.Value : DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ProvisionedState(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
            reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7),
            reader.GetInt32(8), reader.GetInt32(9), reader.GetInt32(10), reader.GetInt32(11),
            reader.IsDBNull(12) ? null : reader.GetBoolean(12),
            reader.IsDBNull(13) ? null : reader.GetString(13), reader.GetString(14));
    }

    private async Task<string> ReadInvitationTokenAsync(Guid tenantId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP(1) Payload FROM dbo.TenantProvisioningOutboxMessages
            WHERE TenantId=@TenantId AND Type=N'TenantAdministratorInvitation' ORDER BY OccurredAt DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        var payload = (string?)await command.ExecuteScalarAsync();
        Assert.False(string.IsNullOrWhiteSpace(payload));
        using var json = JsonDocument.Parse(payload!);
        return json.RootElement.GetProperty("activationToken").GetString()
            ?? throw new InvalidOperationException("Invitation token is missing.");
    }


    private async Task<int> CountDefaultWarehousesAsync(Guid tenantId, Guid businessId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.Warehouses w
            INNER JOIN dbo.Businesses b ON b.BusinessId=w.BusinessId
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
              AND w.Code IN(N'VEN',N'PED') AND w.IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<int> CountDefaultCostCentersAsync(Guid tenantId, Guid businessId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*)
            FROM dbo.AccountingCostCenters c
            INNER JOIN dbo.Businesses b ON b.BusinessId=c.BusinessId
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId
              AND c.IsDefault=1 AND c.IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record BusinessCreatedResponse(Guid BusinessId);

    private sealed record ProvisionedState(
        int Businesses, int Warehouses, int InventoryReasons, int ProductUnits,
        int DefaultCustomers, int AccountingAccounts, int AccountingMappings,
        int OpenAccountingPeriods, int DefaultCostCenters, int AccountingVoucherCursors,
        int Roles, int UserRoles,
        bool? UserActive, string? PasswordHash, string InvitationStatus);
}
