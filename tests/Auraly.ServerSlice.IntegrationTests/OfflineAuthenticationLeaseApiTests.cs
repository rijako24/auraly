using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Auraly.Contracts.Authentication;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class OfflineAuthenticationLeaseApiTests(ServerSliceFixture fixture)
{
    private const string Password = "Auraly-Offline-Test-2026!";

    [Fact]
    public async Task Concurrent_acquisition_returns_one_signed_exclusive_lease_and_release_allows_online_login()
    {
        var user = await CreatePasswordUserAsync("offline-exclusive");
        using var firstRequest = CreateAcquireRequest(user.Username);
        using var secondRequest = CreateAcquireRequest(user.Username);
        using var firstClient = fixture.CreateClient();
        using var secondClient = fixture.CreateClient();
        var responses = await Task.WhenAll(
            firstClient.SendAsync(firstRequest),
            secondClient.SendAsync(secondRequest));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            var leases = await Task.WhenAll(responses.Select(response =>
                response.Content.ReadFromJsonAsync<OfflineAuthenticationLeaseAcquireResponse>()));
            var first = Assert.IsType<OfflineAuthenticationLeaseAcquireResponse>(leases[0]);
            var second = Assert.IsType<OfflineAuthenticationLeaseAcquireResponse>(leases[1]);
            Assert.Equal(first.Lease, second.Lease);

            var payloadBytes = OfflineAuthenticationLeaseTokenCodec.Decode(first.Lease.Payload);
            var payload = OfflineAuthenticationLeaseTokenCodec.Deserialize(payloadBytes);
            Assert.Equal(fixture.TenantId, payload.TenantId);
            Assert.Equal(fixture.DeviceId, payload.DeviceId);
            Assert.Equal(user.UserId, payload.UserId);
            Assert.Equal(first.User.UserId, payload.UserId);
            Assert.Equal(ServerSliceFixture.OfflineLeaseKeyId, first.Lease.KeyId);
            Assert.Equal(OfflineAuthenticationLeaseAlgorithms.RsaPssSha256, first.Lease.Algorithm);
            Assert.InRange(payload.ExpiresAt - payload.IssuedAt, TimeSpan.FromHours(7), TimeSpan.FromHours(8));
            using (var rsa = RSA.Create())
            {
                rsa.ImportFromPem(fixture.OfflineLeasePublicKeyPem);
                Assert.True(rsa.VerifyData(
                    payloadBytes,
                    OfflineAuthenticationLeaseTokenCodec.Decode(first.Lease.Signature),
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss));
            }

            Assert.Equal(1, await CountActiveLeasesAsync(user.UserId));
            using var onlineLogin = await SendOnlineLoginAsync(user.Username);
            Assert.Equal(HttpStatusCode.OK, onlineLogin.StatusCode);
            Assert.Equal(0, await CountActiveLeasesAsync(user.UserId));
            Assert.Equal("Revoked", await ReadLeaseStatusAsync(payload.LeaseId));
            using var release = await SendReleaseAsync(payload.LeaseId);
            Assert.Equal(HttpStatusCode.NoContent, release.StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Active_online_session_blocks_offline_acquisition()
    {
        var user = await CreatePasswordUserAsync("online-first");
        using var online = await SendOnlineLoginAsync(user.Username);
        online.EnsureSuccessStatusCode();

        using var request = CreateAcquireRequest(user.Username);
        using var response = await fixture.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountActiveLeasesAsync(user.UserId));
    }

    [Fact]
    public async Task Device_without_identity_permission_cannot_acquire_a_lease()
    {
        var user = await CreatePasswordUserAsync("denied-device");
        using var request = CreateAcquireRequest(
            user.Username,
            fixture.DeniedDeviceId,
            ServerSliceFixture.DeniedDeviceSecret);
        using var response = await fixture.CreateClient().SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await CountActiveLeasesAsync(user.UserId));
    }

    private HttpRequestMessage CreateAcquireRequest(
        string username,
        Guid? deviceId = null,
        string secret = ServerSliceFixture.DeviceSecret)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/pos/v1/authentication/offline-leases/")
        {
            Content = JsonContent.Create(
                new OfflineAuthenticationLeaseAcquireRequest(username, Password))
        };
        request.Headers.Add(
            "X-Auraly-Device-Id",
            (deviceId ?? fixture.DeviceId).ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", secret);
        return request;
    }

    private async Task<HttpResponseMessage> SendReleaseAsync(Guid leaseId)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/authentication/offline-leases/{leaseId:D}/release");
        request.Headers.Add("X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        return await fixture.CreateClient().SendAsync(request);
    }

    private async Task<HttpResponseMessage> SendOnlineLoginAsync(string username)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new AuthenticationLoginRequest(username, "@auraly-e2e", Password))
        };
        request.Headers.Add(
            AuthenticationDefaults.ClientIdHeader,
            Guid.NewGuid().ToString("D"));
        return await fixture.CreateClient().SendAsync(request);
    }

    private async Task<TestUser> CreatePasswordUserAsync(string prefix)
    {
        var userId = Guid.NewGuid();
        var username = $"{prefix}-{userId:N}";
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
               @PasswordHash,N'Usuario',N'Offline',1,SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@Email", $"{username}@test.local");
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        await command.ExecuteNonQueryAsync();
        return new TestUser(userId, username);
    }

    private async Task<int> CountActiveLeasesAsync(Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM dbo.OfflineAuthenticationLeases
            WHERE TenantId=@TenantId AND UserId=@UserId AND Status=N'Active';
            """;
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task<string> ReadLeaseStatusAsync(Guid leaseId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Status FROM dbo.OfflineAuthenticationLeases WHERE LeaseId=@LeaseId;";
        command.Parameters.AddWithValue("@LeaseId", leaseId);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    private sealed record TestUser(Guid UserId, string Username);
}
