using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Contracts.Authentication;
using Auraly.Platform.Application.Identity.DTOs;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PasswordRecoveryFlowTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Recovery_request_confirmation_and_login_complete_the_real_flow()
    {
        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N");
        var username = $"recovery-{suffix}";
        var email = $"recovery-{suffix}@auraly.test";
        const string newPassword = "Auraly-Recovered-2026!";
        await SeedUserAndSessionAsync(userId, sessionId, username, email);

        using var client = fixture.CreateClient();
        using var neutral = await client.PostAsJsonAsync(
            "/api/v1/auth/password-recovery/request",
            new RequestPasswordRecoveryRequest("@auraly-e2e", username, $"wrong-{email}"));
        Assert.Equal(HttpStatusCode.Accepted, neutral.StatusCode);
        Assert.Equal(0, await CountRequestsAsync(userId));

        using var requested = await client.PostAsJsonAsync(
            "/api/v1/auth/password-recovery/request",
            new RequestPasswordRecoveryRequest("@auraly-e2e", username, email));
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var result = await requested.Content.ReadFromJsonAsync<RequestPasswordRecoveryResult>();
        Assert.NotNull(result);
        Assert.Equal($"re***{email[email.IndexOf('@')..]}", result!.MaskedEmail);
        Assert.Equal("Requested", result.Status);
        Assert.Equal(1, await CountRequestsAsync(userId));

        var token = await ReadResetTokenAsync(email);
        using var confirmed = await client.PostAsJsonAsync(
            "/api/v1/auth/password-recovery/confirm",
            new ConfirmPasswordRecoveryRequest(token, newPassword, newPassword));
        Assert.Equal(HttpStatusCode.NoContent, confirmed.StatusCode);

        var state = await ReadStateAsync(userId, sessionId);
        Assert.Equal("Used", state.RequestStatus);
        Assert.True(state.HasPasswordHash);
        Assert.True(state.HasOfflinePassword);
        Assert.Equal("Revoked", state.SessionStatus);
        Assert.Equal("PasswordReset", state.RevocationReason);

        using var replay = await client.PostAsJsonAsync(
            "/api/v1/auth/password-recovery/confirm",
            new ConfirmPasswordRecoveryRequest(token, newPassword, newPassword));
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);

        using var loginRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new AuthenticationLoginRequest(username, "@auraly-e2e", newPassword))
        };
        loginRequest.Headers.Add(AuthenticationDefaults.ClientIdHeader, Guid.NewGuid().ToString("D"));
        using var login = await client.SendAsync(loginRequest);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var authentication = await login.Content.ReadFromJsonAsync<AuthenticationResponse>();
        Assert.Equal(userId, authentication?.User.UserId);
    }

    private async Task SeedUserAndSessionAsync(
        Guid userId,
        Guid sessionId,
        string username,
        string email)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              PasswordHash,FirstName,LastName,IsActive,EmailConfirmed,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
              N'old-password-hash',N'Recuperación',N'E2E',1,1,SYSUTCDATETIME());

            INSERT dbo.AuthenticationSessions(
              AuthenticationSessionId,TenantId,UserId,ClientId,RefreshTokenHash,
              IssuedAt,ExpiresAt,LastSeenAt,Status)
            VALUES(
              @SessionId,@TenantId,@UserId,NEWID(),
              HASHBYTES('SHA2_256',CONVERT(nvarchar(36),@SessionId)),
              SYSDATETIMEOFFSET(),DATEADD(day,1,SYSDATETIMEOFFSET()),
              SYSDATETIMEOFFSET(),N'Active');
            """, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@Email", email);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<int> CountRequestsAsync(Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.PasswordResetRequests WHERE UserId=@UserId;",
            connection);
        command.Parameters.AddWithValue("@UserId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string> ReadResetTokenAsync(string email)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT TOP(1) Payload
            FROM dbo.TenantProvisioningOutboxMessages
            WHERE TenantId=@TenantId AND Type=N'PasswordRecoveryEmail'
              AND JSON_VALUE(Payload,'$.email')=@Email
            ORDER BY OccurredAt DESC;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Email", email);
        var payload = (string?)await command.ExecuteScalarAsync();
        Assert.False(string.IsNullOrWhiteSpace(payload));
        using var json = JsonDocument.Parse(payload!);
        return json.RootElement.GetProperty("resetToken").GetString()
            ?? throw new InvalidOperationException("The recovery token is missing.");
    }

    private async Task<RecoveryState> ReadStateAsync(Guid userId, Guid sessionId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT
              (SELECT TOP(1) Status FROM dbo.PasswordResetRequests
               WHERE UserId=@UserId ORDER BY CreatedAt DESC),
              CASE WHEN u.PasswordHash IS NULL OR u.PasswordHash=N'old-password-hash' THEN 0 ELSE 1 END,
              CASE WHEN u.PosOfflinePasswordSalt IS NULL OR u.PosOfflinePasswordHash IS NULL
                   OR u.PosOfflinePasswordIterations IS NULL THEN 0 ELSE 1 END,
              s.Status,s.RevocationReason
            FROM dbo.AppUsers u
            JOIN dbo.AuthenticationSessions s
              ON s.UserId=u.UserId AND s.AuthenticationSessionId=@SessionId
            WHERE u.UserId=@UserId;
            """, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new RecoveryState(
            reader.GetString(0),
            reader.GetInt32(1) == 1,
            reader.GetInt32(2) == 1,
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4));
    }

    private sealed record RecoveryState(
        string RequestStatus,
        bool HasPasswordHash,
        bool HasOfflinePassword,
        string SessionStatus,
        string? RevocationReason);
}