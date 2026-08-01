using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AuthenticationSessionApiTests(ServerSliceFixture fixture)
{
    private const string Password = "Auraly-Test-Password-2026!";

    [Fact]
    public async Task Login_creates_one_hashed_session_shared_by_browser_tabs()
    {
        var user = await CreatePasswordUserAsync("auth-login");
        var clientId = Guid.NewGuid();
        var login = await LoginAsync(user.Username, clientId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        var sessionId = Guid.Parse(jwt.Claims.Single(
            claim => claim.Type == AuthenticationDefaults.SessionIdClaim).Value);
        Assert.NotEqual(Guid.Empty, sessionId);

        var persisted = await ReadSessionAsync(sessionId);
        Assert.Equal("Active", persisted.Status);
        Assert.Equal(clientId, persisted.ClientId);
        Assert.Equal(32, persisted.RefreshTokenHash.Length);
        Assert.True(CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(login.RefreshToken)),
            persisted.RefreshTokenHash));

        using var firstTab = AuthenticatedClient(login, clientId);
        using var secondTab = AuthenticatedClient(login, clientId);
        using var firstMe = await firstTab.GetAsync("/api/auth/me");
        using var secondMe = await secondTab.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, firstMe.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondMe.StatusCode);

        using var conflictingClient = CreateLoginRequest(
            user.Username, Password, Guid.NewGuid());
        using var conflict = await fixture.CreateClient().SendAsync(conflictingClient);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
    }

    [Fact]
    public async Task Refresh_rotates_the_secret_and_reuse_revokes_the_session()
    {
        var user = await CreatePasswordUserAsync("auth-refresh");
        var clientId = Guid.NewGuid();
        var first = await LoginAsync(user.Username, clientId);

        using var refreshRequest = CreateRefreshRequest(first, clientId);
        using var refreshResponse = await fixture.CreateClient().SendAsync(refreshRequest);
        refreshResponse.EnsureSuccessStatusCode();
        var second = await ReadAuthenticationResponseAsync(refreshResponse);
        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(SessionId(first.AccessToken), SessionId(second.AccessToken));

        using var replayRequest = CreateRefreshRequest(first, clientId);
        using var replay = await fixture.CreateClient().SendAsync(replayRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        using var authenticated = AuthenticatedClient(second, clientId);
        using var rejected = await authenticated.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal("Revoked", (await ReadSessionAsync(
            SessionId(second.AccessToken))).Status);
    }

    [Fact]
    public async Task Logout_closes_work_session_revokes_authentication_and_allows_new_login()
    {
        var user = await CreatePasswordUserAsync("auth-logout");
        var clientId = Guid.NewGuid();
        var login = await LoginAsync(user.Username, clientId);
        using var authenticated = AuthenticatedClient(login, clientId);

        using (var open = await authenticated.PostAsJsonAsync(
                   "/api/commerce/v1/work-sessions/current",
                   new OpenWorkSessionRequest(
                       fixture.BusinessId, fixture.WarehouseId, null)))
            open.EnsureSuccessStatusCode();

        using var revoke = new HttpRequestMessage(
            HttpMethod.Post, "/api/auth/revoke")
        {
            Content = JsonContent.Create(
                new AuthenticationRevokeRequest(login.RefreshToken))
        };
        revoke.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", login.AccessToken);
        revoke.Headers.Add(
            AuthenticationDefaults.ClientIdHeader, clientId.ToString("D"));
        using var revoked = await fixture.CreateClient().SendAsync(revoke);
        Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);

        using var noLongerAuthorized = await authenticated.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, noLongerAuthorized.StatusCode);
        Assert.Equal(0, await CountOpenWorkSessionsAsync(user.UserId));
        Assert.Equal(1, await CountWorkSessionClosuresAsync(user.UserId));

        var replacement = await LoginAsync(user.Username, Guid.NewGuid());
        Assert.NotEqual(SessionId(login.AccessToken), SessionId(replacement.AccessToken));
        Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
    }

    [Fact]
    public async Task Concurrent_logins_have_exactly_one_winner()
    {
        var user = await CreatePasswordUserAsync("auth-concurrent");
        using var first = CreateLoginRequest(user.Username, Password, Guid.NewGuid());
        using var second = CreateLoginRequest(user.Username, Password, Guid.NewGuid());
        using var firstClient = fixture.CreateClient();
        using var secondClient = fixture.CreateClient();

        var responses = await Task.WhenAll(
            firstClient.SendAsync(first),
            secondClient.SendAsync(second));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    private async Task<AuthenticationResponse> LoginAsync(
        string username,
        Guid clientId)
    {
        using var request = CreateLoginRequest(username, Password, clientId);
        using var response = await fixture.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await ReadAuthenticationResponseAsync(response);
    }

    private static HttpRequestMessage CreateLoginRequest(
        string username,
        string password,
        Guid clientId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(
                new AuthenticationLoginRequest(username, password))
        };
        request.Headers.Add(
            AuthenticationDefaults.ClientIdHeader, clientId.ToString("D"));
        return request;
    }

    private static HttpRequestMessage CreateRefreshRequest(
        AuthenticationResponse response,
        Guid clientId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh")
        {
            Content = JsonContent.Create(
                new AuthenticationRefreshRequest(
                    response.AccessToken, response.RefreshToken))
        };
        request.Headers.Add(
            AuthenticationDefaults.ClientIdHeader, clientId.ToString("D"));
        return request;
    }

    private HttpClient AuthenticatedClient(
        AuthenticationResponse response,
        Guid clientId)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", response.AccessToken);
        client.DefaultRequestHeaders.Add(
            AuthenticationDefaults.ClientIdHeader, clientId.ToString("D"));
        return client;
    }

    private static async Task<AuthenticationResponse> ReadAuthenticationResponseAsync(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<AuthenticationResponse>()
        ?? throw new InvalidOperationException("The authentication response is empty.");

    private static Guid SessionId(string accessToken) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Single(
            claim => claim.Type == AuthenticationDefaults.SessionIdClaim).Value);

    private async Task<TestUser> CreatePasswordUserAsync(string prefix)
    {
        var userId = Guid.NewGuid();
        var username = $"{prefix}-{userId:N}";
        var email = $"{username}@test.local";
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 12);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               PasswordHash,FirstName,LastName,IsActive,CreatedAt)
            VALUES
              (@UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
               @PasswordHash,N'Usuario',N'Autenticacion',1,SYSUTCDATETIME());

            IF NOT EXISTS (SELECT 1 FROM dbo.AppRoles WHERE TenantId=@TenantId AND NormalizedName=N'ADMINISTRATOR')
            BEGIN
                INSERT dbo.AppRoles
                  (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
                VALUES
                  (NEWID(),@TenantId,N'Administrator',N'ADMINISTRATOR',N'Integration test administrator',1,1,SYSUTCDATETIME());
            END;

            INSERT dbo.RolePermissions (RolePermissionId,RoleId,PermissionId,AssignedAt)
            SELECT NEWID(),r.RoleId,p.PermissionId,SYSUTCDATETIME()
            FROM dbo.AppRoles r
            CROSS JOIN dbo.Permissions p
            WHERE r.TenantId=@TenantId AND r.NormalizedName=N'ADMINISTRATOR'
              AND NOT EXISTS
              (
                  SELECT 1 FROM dbo.RolePermissions rp
                  WHERE rp.RoleId=r.RoleId AND rp.PermissionId=p.PermissionId
              );

            INSERT dbo.UserRoles (UserRoleId,UserId,RoleId,AssignedAt)
            SELECT NEWID(),@UserId,r.RoleId,SYSUTCDATETIME()
            FROM dbo.AppRoles r
            WHERE r.NormalizedName=N'ADMINISTRATOR' AND r.IsActive=1
              AND (r.TenantId IS NULL OR r.TenantId=@TenantId);
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@Email", email);
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        await command.ExecuteNonQueryAsync();
        return new TestUser(userId, username);
    }

    private async Task<TestSessionRow> ReadSessionAsync(Guid sessionId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ClientId,RefreshTokenHash,Status
            FROM dbo.AuthenticationSessions
            WHERE AuthenticationSessionId=@SessionId;
            """;
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new TestSessionRow(
            reader.GetGuid(0), (byte[])reader[1], reader.GetString(2));
    }

    private Task<int> CountActiveSessionsAsync(Guid userId) =>
        CountAsync(
            "SELECT COUNT(*) FROM dbo.AuthenticationSessions " +
            "WHERE UserId=@UserId AND Status=N'Active';", userId);

    private Task<int> CountOpenWorkSessionsAsync(Guid userId) =>
        CountAsync(
            "SELECT COUNT(*) FROM dbo.WorkSessions " +
            "WHERE UserId=@UserId AND Status=N'Open';", userId);

    private Task<int> CountWorkSessionClosuresAsync(Guid userId) =>
        CountAsync(
            "SELECT COUNT(*) FROM dbo.WorkSessionClosures c " +
            "INNER JOIN dbo.WorkSessions s ON s.WorkSessionId=c.WorkSessionId " +
            "WHERE s.UserId=@UserId;", userId);

    private async Task<int> CountAsync(string sql, Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record TestUser(Guid UserId, string Username);
    private sealed record TestSessionRow(
        Guid ClientId, byte[] RefreshTokenHash, string Status);
}
