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
            "America/Bogota", "LatestReceiptCost", "CC", $"10{Random.Shared.Next(10000000, 99999999)}",
            "Administrador", suffix, email, "3007654321");

        using var admin = fixture.CreateAdminClient("tenants.create");
        using var created = await admin.PostAsJsonAsync("/api/v1/tenants", request);
        var creationBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.StatusCode == HttpStatusCode.Created, $"Expected Created but received {created.StatusCode}: {creationBody}");
        var result = await created.Content.ReadFromJsonAsync<ProvisionTenantResult>();
        Assert.NotNull(result);

        var state = await ReadProvisionedStateAsync(result!.TenantId, result.AdministratorUserId);
        Assert.Equal(1, state.Businesses);
        Assert.Equal(2, state.Warehouses);
        Assert.Equal(12, state.InventoryReasons);
        Assert.Equal(4, state.ProductUnits);
        Assert.Equal(1, state.DefaultCustomers);
        Assert.Equal(4, state.Roles);
        Assert.Equal(1, state.UserRoles);
        Assert.False(state.UserActive);
        Assert.Null(state.PasswordHash);
        Assert.Equal("Pending", state.InvitationStatus);

        var token = await ReadInvitationTokenAsync(result.TenantId);
        using var publicClient = fixture.CreateClient();
        using var accepted = await publicClient.PostAsJsonAsync(
            "/api/v1/auth/invitations/accept",
            new AcceptTenantInvitationRequest(token, Password, Password));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var acceptedState = await ReadProvisionedStateAsync(result.TenantId, result.AdministratorUserId);
        Assert.True(acceptedState.UserActive);
        Assert.NotNull(acceptedState.PasswordHash);
        Assert.Equal("Accepted", acceptedState.InvitationStatus);

        using var reused = await publicClient.PostAsJsonAsync(
            "/api/v1/auth/invitations/accept",
            new AcceptTenantInvitationRequest(token, Password, Password));
        Assert.Equal(HttpStatusCode.Conflict, reused.StatusCode);

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(new AuthenticationLoginRequest(email, Password))
        };
        loginRequest.Headers.Add(AuthenticationDefaults.ClientIdHeader, Guid.NewGuid().ToString("D"));
        using var login = await publicClient.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authentication = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.NotNull(authentication);
        Assert.Equal(result.TenantId, authentication!.User.TenantId);
        Assert.Equal(result.AdministratorUserId, authentication.User.UserId);
        Assert.Contains("Administrador", authentication.User.Roles);
        Assert.DoesNotContain(authentication.User.Permissions, permission =>
            permission.StartsWith("tenants.", StringComparison.OrdinalIgnoreCase));
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

    private async Task<ProvisionedState> ReadProvisionedStateAsync(Guid tenantId, Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT
              (SELECT COUNT(*) FROM dbo.Businesses WHERE TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.Warehouses w INNER JOIN dbo.Businesses b ON b.BusinessId=w.BusinessId WHERE b.TenantId=@TenantId AND w.Code IN(N'VEN',N'PED')),
              (SELECT COUNT(*) FROM dbo.InventoryReasons r INNER JOIN dbo.Businesses b ON b.BusinessId=r.BusinessId WHERE b.TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.ProductUnits u INNER JOIN dbo.Businesses b ON b.BusinessId=u.BusinessId WHERE b.TenantId=@TenantId),
              (SELECT COUNT(*) FROM dbo.Customers c INNER JOIN dbo.Parties p ON p.PartyId=c.PartyId WHERE p.TenantId=@TenantId AND p.DisplayName=N'Consumidor final'),
              (SELECT COUNT(*) FROM dbo.AppRoles WHERE TenantId=@TenantId AND NormalizedName IN(N'CASHIER',N'SUPERVISOR',N'ADMINISTRATIVE',N'TENANTADMINISTRATOR')),
              (SELECT COUNT(*) FROM dbo.UserRoles WHERE UserId=@UserId),
              u.IsActive,u.PasswordHash,i.Status
            FROM dbo.AppUsers u
            INNER JOIN dbo.TenantUserInvitations i ON i.UserId=u.UserId AND i.TenantId=u.TenantId
            WHERE u.TenantId=@TenantId AND u.UserId=@UserId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new ProvisionedState(
            reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3),
            reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6),
            reader.GetBoolean(7), reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9));
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

    private sealed record BusinessCreatedResponse(Guid BusinessId);

    private sealed record ProvisionedState(
        int Businesses, int Warehouses, int InventoryReasons, int ProductUnits,
        int DefaultCustomers, int Roles, int UserRoles,
        bool UserActive, string? PasswordHash, string InvitationStatus);
}
