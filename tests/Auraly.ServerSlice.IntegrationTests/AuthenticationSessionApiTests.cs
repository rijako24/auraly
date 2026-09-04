using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Organization;
using Auraly.Contracts.WorkSessions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AuthenticationSessionApiTests(ServerSliceFixture fixture)
{
    private const string Password = "Auraly-Test-Password-2026!";

    [Fact]
    public async Task Correct_credentials_enter_even_after_previous_failures_and_clear_the_lockout()
    {
        var user = await CreatePasswordUserAsync("auth-valid-after-failures");
        await SetActiveLockoutAsync(user.UserId);

        var login = await LoginAsync(user.Username, Guid.NewGuid());

        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        var state = await ReadLockoutStateAsync(user.UserId);
        Assert.Equal(0, state.FailedCount);
        Assert.Null(state.LockoutEnd);
    }

    [Fact]
    public async Task First_login_is_active_and_replacement_login_revokes_only_the_old_session()
    {
        var user = await CreatePasswordUserAsync("auth-login");
        var clientId = Guid.NewGuid();
        var login = await LoginAsync(user.Username, clientId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.True(login.AccessToken.Length < 3800, $"Access token length {login.AccessToken.Length} exceeds the safe cookie budget.");
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type == AuthenticationDefaults.PermissionClaim);
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
        using var firstMe = await firstTab.GetAsync("/api/v1/auth/me");
        using var secondMe = await secondTab.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, firstMe.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondMe.StatusCode);

        using var compactTokenClient = AuthenticatedClient(login, clientId);
        using var workspaceResponse = await compactTokenClient.GetAsync(
            "/api/commerce/v1/pos/workspace/bootstrap");
        Assert.Equal(HttpStatusCode.OK, workspaceResponse.StatusCode);
        var workspace = await workspaceResponse.Content
            .ReadFromJsonAsync<SalesWorkspaceBootstrap>();
        Assert.NotNull(workspace);
        Assert.Contains(workspace.Options, option =>
            option.BusinessId == fixture.BusinessId &&
            option.WarehouseId == fixture.WarehouseId);

        var secondClientId = Guid.NewGuid();
        using var replacementClient = CreateLoginRequest(
            user.Username, Password, secondClientId);
        using var replacement = await fixture.CreateClient().SendAsync(replacementClient);
        Assert.Equal(HttpStatusCode.OK, replacement.StatusCode);
        var replacementLogin = await ReadAuthenticationResponseAsync(replacement);
        using var originalClient = AuthenticatedClient(login, clientId);
        using var originalResponse = await originalClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, originalResponse.StatusCode);
        using var replacedRefreshRequest = CreateRefreshRequest(login, clientId);
        using var replacedRefresh = await fixture.CreateClient().SendAsync(replacedRefreshRequest);
        Assert.Equal(HttpStatusCode.Conflict, replacedRefresh.StatusCode);
        var replacedProblem = await replacedRefresh.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("AuthenticationSessionReplaced", replacedProblem?.Title);
        using var secondAuthenticated = AuthenticatedClient(replacementLogin, secondClientId);
        using var secondResponse = await secondAuthenticated.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        Assert.Equal("Revoked", (await ReadSessionAsync(sessionId)).Status);
        Assert.Equal("Active", (await ReadSessionAsync(
            SessionId(replacementLogin.AccessToken))).Status);
        Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
    }

    [Fact]
    public async Task Refresh_rotates_the_secret_and_stale_parallel_refresh_does_not_revoke_the_current_session()
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
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);

        using var authenticated = AuthenticatedClient(second, clientId);
        using var rejected = await authenticated.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal("Active", (await ReadSessionAsync(
            SessionId(second.AccessToken))).Status);
    }

    [Fact]
    public async Task Browser_session_has_a_twenty_four_hour_sliding_idle_window()
    {
        var user = await CreatePasswordUserAsync("auth-idle-window");
        var clientId = Guid.NewGuid();
        var first = await LoginAsync(user.Username, clientId);
        var firstState = await ReadSessionAsync(SessionId(first.AccessToken));

        Assert.InRange(
            firstState.ExpiresAt - firstState.LastSeenAt,
            TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
            TimeSpan.FromHours(24).Add(TimeSpan.FromMinutes(1)));

        var almostIdleLimit = DateTimeOffset.UtcNow.Subtract(TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)));
        await SetSessionWindowAsync(
            SessionId(first.AccessToken), almostIdleLimit, DateTimeOffset.UtcNow.AddMinutes(1));
        using var refreshRequest = CreateRefreshRequest(first, clientId);
        using var refreshResponse = await fixture.CreateClient().SendAsync(refreshRequest);
        refreshResponse.EnsureSuccessStatusCode();
        var second = await ReadAuthenticationResponseAsync(refreshResponse);
        var secondState = await ReadSessionAsync(SessionId(second.AccessToken));

        Assert.True(secondState.LastSeenAt > firstState.LastSeenAt);
        Assert.True(secondState.ExpiresAt > firstState.ExpiresAt);
        Assert.InRange(
            secondState.ExpiresAt - secondState.LastSeenAt,
            TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
            TimeSpan.FromHours(24).Add(TimeSpan.FromMinutes(1)));

        await SetSessionWindowAsync(
            SessionId(second.AccessToken), DateTimeOffset.UtcNow.AddHours(-24),
            DateTimeOffset.UtcNow.AddSeconds(-1));
        using var expiredRequest = CreateRefreshRequest(second, clientId);
        using var expiredResponse = await fixture.CreateClient().SendAsync(expiredRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_keeps_work_session_open_revokes_authentication_and_allows_new_login()
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
            HttpMethod.Post, "/api/v1/auth/revoke")
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

        using var noLongerAuthorized = await authenticated.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, noLongerAuthorized.StatusCode);
        Assert.Equal(1, await CountOpenWorkSessionsAsync(user.UserId));
        Assert.Equal(0, await CountWorkSessionClosuresAsync(user.UserId));

        var replacement = await LoginAsync(user.Username, Guid.NewGuid());
        Assert.NotEqual(SessionId(login.AccessToken), SessionId(replacement.AccessToken));
        Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
    }

    [Fact]
    public async Task New_login_from_another_client_keeps_the_open_work_session()
    {
        var user = await CreatePasswordUserAsync("auth-login-replacement");
        var firstClientId = Guid.NewGuid();
        var first = await LoginAsync(user.Username, firstClientId);
        using var authenticated = AuthenticatedClient(first, firstClientId);

        using var open = await authenticated.PostAsJsonAsync(
            "/api/commerce/v1/work-sessions/current",
            new OpenWorkSessionRequest(
                fixture.BusinessId, fixture.WarehouseId, null));
        open.EnsureSuccessStatusCode();
        var workSession = await open.Content.ReadFromJsonAsync<WorkSessionView>();
        Assert.NotNull(workSession);

        var replacementClientId = Guid.NewGuid();
        var replacement = await LoginAsync(user.Username, replacementClientId);

        Assert.NotEqual(SessionId(first.AccessToken), SessionId(replacement.AccessToken));
        Assert.Equal(1, await CountOpenWorkSessionsAsync(user.UserId));
        Assert.Equal(0, await CountWorkSessionClosuresAsync(user.UserId));
        Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
        using var replacementClient = AuthenticatedClient(
            replacement, replacementClientId);
        var resumed = await replacementClient.GetFromJsonAsync<WorkSessionView>(
            "/api/commerce/v1/work-sessions/current");
        Assert.NotNull(resumed);
        Assert.Equal(workSession.WorkSessionId, resumed.WorkSessionId);
    }

    [Fact]
    public async Task Same_browser_relogin_rejects_the_old_session_id_and_accepts_the_new_one()
    {
        var user = await CreatePasswordUserAsync("auth-same-client");
        var clientId = Guid.NewGuid();
        var first = await LoginAsync(user.Username, clientId);
        var second = await LoginAsync(user.Username, clientId);

        using var oldClient = AuthenticatedClient(first, clientId);
        using var oldResponse = await oldClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, oldResponse.StatusCode);

        using var currentClient = AuthenticatedClient(second, clientId);
        using var currentResponse = await currentClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, currentResponse.StatusCode);
        Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
    }

    [Fact]
    public async Task Active_session_accepts_only_its_persisted_browser_client()
    {
        var user = await CreatePasswordUserAsync("auth-client-boundary");
        var clientId = Guid.NewGuid();
        var login = await LoginAsync(user.Username, clientId);
        var sessionId = SessionId(login.AccessToken);
        var beforeValidation = await ReadSessionAsync(sessionId);

        using var matchingBrowser = AuthenticatedClient(login, clientId);
        using var accepted = await matchingBrowser.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var afterValidation = await ReadSessionAsync(sessionId);
        Assert.Equal(beforeValidation.LastSeenAt, afterValidation.LastSeenAt);

        using var differentBrowser = AuthenticatedClient(login, Guid.NewGuid());
        using var rejected = await differentBrowser.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);

        using var missingBrowser = fixture.CreateClient();
        missingBrowser.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", login.AccessToken);
        using var missing = await missingBrowser.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var stillActive = await matchingBrowser.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
        Assert.Equal("Active", (await ReadSessionAsync(sessionId)).Status);
    }

    [Fact]
    public async Task Refresh_from_another_client_is_denied_without_revoking_the_active_session()
    {
        var user = await CreatePasswordUserAsync("auth-refresh-client-boundary");
        var clientId = Guid.NewGuid();
        var login = await LoginAsync(user.Username, clientId);
        var sessionId = SessionId(login.AccessToken);

        using var mismatchedRefresh = CreateRefreshRequest(login, Guid.NewGuid());
        using var rejected = await fixture.CreateClient().SendAsync(mismatchedRefresh);
        Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        Assert.Equal("Active", (await ReadSessionAsync(sessionId)).Status);

        using var originalBrowser = AuthenticatedClient(login, clientId);
        using var stillAccepted = await originalBrowser.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, stillAccepted.StatusCode);
    }

    [Fact]
    public async Task Enrolled_pos_login_revokes_the_previous_browser_before_it_can_close_the_work_session()
    {
        var user = await CreatePasswordUserAsync("auth-pos-handoff");
        var browserClientId = Guid.NewGuid();
        var browserLogin = await LoginAsync(user.Username, browserClientId);
        using var browser = AuthenticatedClient(browserLogin, browserClientId);
        using (var open = await browser.PostAsJsonAsync(
                   "/api/commerce/v1/work-sessions/current",
                   new OpenWorkSessionRequest(
                       fixture.BusinessId, fixture.WarehouseId, null)))
            open.EnsureSuccessStatusCode();

        using var acquire = new HttpRequestMessage(
            HttpMethod.Post, "/api/pos/v1/authentication/offline-leases")
        {
            Content = JsonContent.Create(
                new OfflineAuthenticationLeaseAcquireRequest(
                    user.Username, Password))
        };
        acquire.Headers.Add(
            "X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        acquire.Headers.Add(
            "X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var acquired = await fixture.CreateClient().SendAsync(acquire);
        acquired.EnsureSuccessStatusCode();
        var acquiredLease = await acquired.Content
            .ReadFromJsonAsync<OfflineAuthenticationLeaseAcquireResponse>();
        Assert.NotNull(acquiredLease);

        using var staleAction = await browser.GetAsync(
            "/api/commerce/v1/work-sessions/current");
        Assert.Equal(HttpStatusCode.Unauthorized, staleAction.StatusCode);
        Assert.Equal(0, await CountActiveSessionsAsync(user.UserId));
        Assert.Equal(1, await CountOpenWorkSessionsAsync(user.UserId));
        Assert.Equal(0, await CountWorkSessionClosuresAsync(user.UserId));

        var leasePayload = OfflineAuthenticationLeaseTokenCodec.Deserialize(
            OfflineAuthenticationLeaseTokenCodec.Decode(acquiredLease.Lease.Payload));
        using var release = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/pos/v1/authentication/offline-leases/{leasePayload.LeaseId:D}/release");
        release.Headers.Add(
            "X-Auraly-Device-Id", fixture.DeviceId.ToString("D"));
        release.Headers.Add(
            "X-Auraly-Device-Secret", ServerSliceFixture.DeviceSecret);
        using var released = await fixture.CreateClient().SendAsync(release);
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
    }

    [Fact]
    public async Task Browser_login_invalidates_only_the_previous_pos_login_and_keeps_device_and_work_session()
    {
        var user = await CreatePasswordUserAsync("auth-pos-to-browser");
        var browserClientId = Guid.NewGuid();
        var browserLogin = await LoginAsync(user.Username, browserClientId);
        using var browser = AuthenticatedClient(browserLogin, browserClientId);
        using var opened = await browser.PostAsJsonAsync(
            "/api/commerce/v1/work-sessions/current",
            new OpenWorkSessionRequest(
                fixture.BusinessId, fixture.WarehouseId, null));
        opened.EnsureSuccessStatusCode();
        var workSession = await opened.Content.ReadFromJsonAsync<WorkSessionView>();
        Assert.NotNull(workSession);

        var posLease = await AcquireOfflineLeaseAsync(
            user.Username,
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret);
        Assert.True(await IsOfflineLeaseActiveAsync(
            posLease.LeaseId,
            user.UserId,
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret));

        var replacementClientId = Guid.NewGuid();
        var replacementLogin = await LoginAsync(user.Username, replacementClientId);

        Assert.False(await IsOfflineLeaseActiveAsync(
            posLease.LeaseId,
            user.UserId,
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret));
        Assert.Equal(1, await CountOpenWorkSessionsAsync(user.UserId));
        Assert.Equal(0, await CountWorkSessionClosuresAsync(user.UserId));
        Assert.Equal(1, await CountActiveDevicesAsync(fixture.DeviceId));

        using var replacement = AuthenticatedClient(
            replacementLogin, replacementClientId);
        var resumed = await replacement.GetFromJsonAsync<WorkSessionView>(
            "/api/commerce/v1/work-sessions/current");
        Assert.NotNull(resumed);
        Assert.Equal(workSession.WorkSessionId, resumed.WorkSessionId);
    }

    [Fact]
    public async Task Login_on_another_enrolled_device_invalidates_only_the_previous_pos_login()
    {
        var user = await CreatePasswordUserAsync("auth-pos-to-pos");
        var first = await AcquireOfflineLeaseAsync(
            user.Username,
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret);
        Assert.True(await IsOfflineLeaseActiveAsync(
            first.LeaseId,
            user.UserId,
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret));

        var second = await AcquireOfflineLeaseAsync(
            user.Username,
            fixture.DeniedDeviceId,
            ServerSliceFixture.DeniedDeviceSecret);

        Assert.False(await IsOfflineLeaseActiveAsync(
            first.LeaseId,
            user.UserId,
            fixture.DeviceId,
            ServerSliceFixture.DeviceSecret));
        Assert.True(await IsOfflineLeaseActiveAsync(
            second.LeaseId,
            user.UserId,
            fixture.DeniedDeviceId,
            ServerSliceFixture.DeniedDeviceSecret));
        Assert.Equal(1, await CountActiveDevicesAsync(fixture.DeviceId));
        Assert.Equal(1, await CountActiveDevicesAsync(fixture.DeniedDeviceId));
    }

    [Fact]
    public async Task Concurrent_logins_from_distinct_clients_leave_only_one_active()
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
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
            Assert.Equal(1, await CountActiveSessionsAsync(user.UserId));
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Replacing_one_multitenant_login_never_revokes_other_users_or_tenants()
    {
        var firstTenantUser = await CreatePasswordUserAsync("auth-matrix-first");
        var secondTenantUser = await CreatePasswordUserAsync("auth-matrix-second");
        var otherTenantUser = await CreatePasswordUserInNewTenantAsync("auth-matrix-other");
        var firstClientId = Guid.NewGuid();
        var secondUserClientId = Guid.NewGuid();
        var otherTenantClientId = Guid.NewGuid();
        var first = await LoginAsync(firstTenantUser.Username, firstClientId);
        var second = await LoginAsync(secondTenantUser.Username, secondUserClientId);
        var other = await LoginAsync(
            otherTenantUser.Username, otherTenantClientId, otherTenantUser.TenantKey);

        var winnerClientId = Guid.NewGuid();
        var winner = await LoginAsync(firstTenantUser.Username, winnerClientId);

        using var replaced = AuthenticatedClient(first, firstClientId);
        using var replacedResponse = await replaced.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, replacedResponse.StatusCode);
        using var winnerClient = AuthenticatedClient(winner, winnerClientId);
        using var winnerResponse = await winnerClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, winnerResponse.StatusCode);
        using var secondUserClient = AuthenticatedClient(second, secondUserClientId);
        using var secondUserResponse = await secondUserClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, secondUserResponse.StatusCode);
        using var otherClient = AuthenticatedClient(other, otherTenantClientId);
        using var otherResponse = await otherClient.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);

        Assert.Equal(1, await CountActiveSessionsAsync(firstTenantUser.UserId));
        Assert.Equal(1, await CountActiveSessionsAsync(secondTenantUser.UserId));
        Assert.Equal(1, await CountActiveSessionsAsync(otherTenantUser.UserId));
        Assert.Equal(fixture.TenantId, TenantId(winner.AccessToken));
        Assert.Equal(otherTenantUser.TenantId, TenantId(other.AccessToken));
    }

    private async Task<AuthenticationResponse> LoginAsync(
        string username,
        Guid clientId,
        string tenantKey = "@auraly-e2e")
    {
        using var request = CreateLoginRequest(username, Password, clientId, tenantKey);
        using var response = await fixture.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await ReadAuthenticationResponseAsync(response);
    }

    private static HttpRequestMessage CreateLoginRequest(
        string username,
        string password,
        Guid clientId,
        string tenantKey = "@auraly-e2e")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        {
            Content = JsonContent.Create(
                new AuthenticationLoginRequest(username, tenantKey, password))
        };
        request.Headers.Add(
            AuthenticationDefaults.ClientIdHeader, clientId.ToString("D"));
        return request;
    }

    private static HttpRequestMessage CreateRefreshRequest(
        AuthenticationResponse response,
        Guid clientId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh")
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
        if (TenantId(response.AccessToken) == fixture.TenantId)
            client.DefaultRequestHeaders.Add(
                "X-Business-Id", fixture.BusinessId.ToString("D"));
        return client;
    }

    private async Task<OfflineLeaseIdentity> AcquireOfflineLeaseAsync(
        string username,
        Guid deviceId,
        string deviceSecret)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/pos/v1/authentication/offline-leases")
        {
            Content = JsonContent.Create(
                new OfflineAuthenticationLeaseAcquireRequest(username, Password))
        };
        request.Headers.Add("X-Auraly-Device-Id", deviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", deviceSecret);
        using var response = await fixture.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        var lease = await response.Content
            .ReadFromJsonAsync<OfflineAuthenticationLeaseAcquireResponse>()
            ?? throw new InvalidOperationException("The offline lease response is empty.");
        var payload = OfflineAuthenticationLeaseTokenCodec.Deserialize(
            OfflineAuthenticationLeaseTokenCodec.Decode(lease.Lease.Payload));
        return new OfflineLeaseIdentity(payload.LeaseId);
    }

    private async Task<bool> IsOfflineLeaseActiveAsync(
        Guid leaseId,
        Guid userId,
        Guid deviceId,
        string deviceSecret)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"/api/pos/v1/authentication/offline-leases/{leaseId:D}/active?userId={userId:D}");
        request.Headers.Add("X-Auraly-Device-Id", deviceId.ToString("D"));
        request.Headers.Add("X-Auraly-Device-Secret", deviceSecret);
        using var response = await fixture.CreateClient().SendAsync(request);
        response.EnsureSuccessStatusCode();
        var state = await response.Content.ReadFromJsonAsync<OfflineLeaseActiveState>();
        return state?.Active == true;
    }

    private static async Task<AuthenticationResponse> ReadAuthenticationResponseAsync(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<AuthenticationResponse>()
        ?? throw new InvalidOperationException("The authentication response is empty.");

    private static Guid SessionId(string accessToken) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Single(
            claim => claim.Type == AuthenticationDefaults.SessionIdClaim).Value);

    private static Guid TenantId(string accessToken) =>
        Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.Single(
            claim => claim.Type == AuthenticationDefaults.TenantIdClaim).Value);

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

    private async Task<TestTenantUser> CreatePasswordUserInNewTenantAsync(string prefix)
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var tenantKey = $"@{prefix}-{tenantId:N}";
        var username = $"{prefix}-{userId:N}";
        var email = $"{username}@test.local";
        var passwordHash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 12);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.Tenants(TenantId,TenantKey,Name,Email,IsActive)
            VALUES(@TenantId,@TenantKey,N'Tenant de aislamiento',@Email,1);
            INSERT dbo.AppUsers
              (UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
               PasswordHash,FirstName,LastName,IsActive,CreatedAt)
            VALUES
              (@UserId,@TenantId,@Username,UPPER(@Username),@Email,UPPER(@Email),
               @PasswordHash,N'Usuario',N'Otro tenant',1,SYSUTCDATETIME());
            INSERT dbo.AppRoles
              (RoleId,TenantId,Name,NormalizedName,Description,IsActive,IsSystemRole,CreatedAt)
            VALUES
              (@RoleId,@TenantId,N'Administrator',N'ADMINISTRATOR',N'Integration tenant administrator',1,1,SYSUTCDATETIME());
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,AssignedAt)
            VALUES(NEWID(),@UserId,@RoleId,SYSUTCDATETIME());
            """;
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@TenantKey", tenantKey);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Username", username);
        command.Parameters.AddWithValue("@Email", email);
        command.Parameters.AddWithValue("@PasswordHash", passwordHash);
        command.Parameters.AddWithValue("@RoleId", Guid.NewGuid());
        await command.ExecuteNonQueryAsync();
        return new TestTenantUser(userId, username, tenantId, tenantKey);
    }

    private async Task<TestSessionRow> ReadSessionAsync(Guid sessionId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ClientId,RefreshTokenHash,Status,LastSeenAt,ExpiresAt
            FROM dbo.AuthenticationSessions
            WHERE AuthenticationSessionId=@SessionId;
            """;
        command.Parameters.AddWithValue("@SessionId", sessionId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new TestSessionRow(
            reader.GetGuid(0), (byte[])reader[1], reader.GetString(2),
            reader.GetDateTimeOffset(3), reader.GetDateTimeOffset(4));
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

    private async Task<int> CountActiveDevicesAsync(Guid deviceId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM dbo.EnrolledDevices " +
            "WHERE TenantId=@TenantId AND DeviceId=@DeviceId AND IsActive=1;",
            connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@DeviceId", deviceId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task SetSessionWindowAsync(
        Guid sessionId,
        DateTimeOffset lastSeenAt,
        DateTimeOffset expiresAt)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE dbo.AuthenticationSessions SET " +
            "IssuedAt=CASE WHEN IssuedAt>=@ExpiresAt THEN DATEADD(day,-1,@ExpiresAt) ELSE IssuedAt END, " +
            "LastSeenAt=@LastSeenAt, " +
            "ExpiresAt=@ExpiresAt WHERE AuthenticationSessionId=@SessionId;", connection);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@LastSeenAt", lastSeenAt);
        command.Parameters.AddWithValue("@ExpiresAt", expiresAt);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task SetActiveLockoutAsync(Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "UPDATE dbo.AppUsers SET AccessFailedCount=5, " +
            "LockoutEnd=DATEADD(minute,15,SYSUTCDATETIME()) WHERE UserId=@UserId;",
            connection);
        command.Parameters.AddWithValue("@UserId", userId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<LockoutState> ReadLockoutStateAsync(Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT AccessFailedCount,LockoutEnd FROM dbo.AppUsers WHERE UserId=@UserId;",
            connection);
        command.Parameters.AddWithValue("@UserId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return new LockoutState(
            reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetDateTimeOffset(1));
    }

    private async Task<int> CountAsync(string sql, Guid userId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private sealed record TestUser(Guid UserId, string Username);
    private sealed record TestTenantUser(
        Guid UserId, string Username, Guid TenantId, string TenantKey);
    private sealed record TestSessionRow(
        Guid ClientId, byte[] RefreshTokenHash, string Status,
        DateTimeOffset LastSeenAt, DateTimeOffset ExpiresAt);
    private sealed record LockoutState(int FailedCount, DateTimeOffset? LockoutEnd);
    private sealed record OfflineLeaseIdentity(Guid LeaseId);
    private sealed record OfflineLeaseActiveState(bool Active);
}
