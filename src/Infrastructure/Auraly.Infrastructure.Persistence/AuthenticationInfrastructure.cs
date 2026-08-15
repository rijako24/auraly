using System.Data;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Authentication;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Auraly.Infrastructure.Persistence;

public sealed class AuthenticationJwtOptions
{
    public const string SectionName = "Authentication:Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; set; } = 15;
    public int RefreshTokenExpirationDays { get; set; } = 7;
}

public sealed class BcryptAuthenticationPasswordVerifier : IAuthenticationPasswordVerifier
{
    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch (BCrypt.Net.SaltParseException)
        {
            return false;
        }
    }
}

public sealed class JwtAuthenticationTokenIssuer : IAuthenticationTokenIssuer
{
    private readonly AuthenticationJwtOptions _options;
    private readonly SymmetricSecurityKey _key;

    public JwtAuthenticationTokenIssuer(IOptions<AuthenticationJwtOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.Issuer) ||
            string.IsNullOrWhiteSpace(_options.Audience) ||
            Encoding.UTF8.GetByteCount(_options.SigningKey) < 32)
            throw new InvalidOperationException(
                "Authentication JWT issuer, audience and a signing key of at least 32 bytes are required.");
        if (_options.AccessTokenExpirationMinutes is < 1 or > 1440)
            throw new InvalidOperationException(
                "JWT access token expiration must be between 1 and 1440 minutes.");
        if (_options.RefreshTokenExpirationDays is < 1 or > 30)
            throw new InvalidOperationException(
                "Refresh token expiration must be between 1 and 30 days.");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
    }

    public TimeSpan AccessTokenLifetime =>
        TimeSpan.FromMinutes(_options.AccessTokenExpirationMinutes);

    public TimeSpan RefreshTokenLifetime =>
        TimeSpan.FromDays(_options.RefreshTokenExpirationDays);

    public string IssueAccessToken(
        AuthenticationSessionIdentity identity,
        AuthenticationUserRecord user,
        DateTimeOffset issuedAt)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString("D")),
            new(ClaimTypes.NameIdentifier, user.UserId.ToString("D")),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(AuthenticationDefaults.SessionIdClaim,
                identity.AuthenticationSessionId.ToString("D")),
            new(AuthenticationDefaults.TenantIdClaim, user.TenantId.ToString("D")),
            new("username", user.Username),
            new("full_name", $"{user.FirstName} {user.LastName}".Trim())
        };
        // Keep browser access tokens below cookie limits. Roles and permissions are
        // resolved from SQL for the selected tenant/business by ExecutionContextMiddleware.
        // The login response still carries them for immediate menu rendering.
        var token = new JwtSecurityToken(
            _options.Issuer,
            _options.Audience,
            claims,
            issuedAt.UtcDateTime,
            issuedAt.Add(AccessTokenLifetime).UtcDateTime,
            new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public ParsedAuthenticationToken ParseExpiredAccessToken(string accessToken)
    {
        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(
                accessToken,
                ValidationParameters(validateLifetime: false),
                out var validatedToken);
            if (validatedToken is not JwtSecurityToken jwt ||
                !string.Equals(
                    jwt.Header.Alg,
                    SecurityAlgorithms.HmacSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new AuthenticationDeniedException("Invalid access token.");
            return Parse(principal);
        }
        catch (SecurityTokenException exception)
        {
            throw new AuthenticationDeniedException(
                $"Invalid access token: {exception.GetType().Name}.");
        }
        catch (ArgumentException)
        {
            throw new AuthenticationDeniedException("Invalid access token.");
        }
    }

    public TokenValidationParameters ValidationParameters(bool validateLifetime = true) =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateLifetime = validateLifetime,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = ClaimTypes.NameIdentifier,
            RoleClaimType = ClaimTypes.Role
        };

    public static ParsedAuthenticationToken Parse(ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, AuthenticationDefaults.SessionIdClaim),
            RequiredGuid(principal, ClaimTypes.NameIdentifier, JwtRegisteredClaimNames.Sub),
            RequiredGuid(principal, AuthenticationDefaults.TenantIdClaim));

    private static Guid RequiredGuid(
        ClaimsPrincipal principal,
        string primary,
        string? fallback = null)
    {
        var value = principal.FindFirst(primary)?.Value ??
                    (fallback is null ? null : principal.FindFirst(fallback)?.Value);
        return Guid.TryParse(value, out var parsed)
            ? parsed
            : throw new AuthenticationDeniedException(
                $"The access token lacks claim '{primary}'.");
    }
}

public sealed class SqlAuthenticationSessionStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids) : IAuthenticationSessionStore
{
    public async Task<AuthenticationUserRecord?> FindUserAsync(
        string tenantKey, string normalizedUsername,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        return await ReadUserByUsernameAsync(
            connection, null, tenantKey, normalizedUsername, cancellationToken);
    }

    public async Task<AuthenticationUserRecord?> FindUserAsync(
        Guid tenantId,
        string normalizedUsername,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT u.UserId,u.TenantId,t.TenantKey,u.Username,u.Email,u.FirstName,u.LastName,u.AvatarUrl,
                   u.PasswordHash,u.IsActive,u.AccessFailedCount,u.LockoutEnd
            FROM dbo.AppUsers u
            INNER JOIN dbo.Tenants t ON t.TenantId=u.TenantId AND t.IsActive=1
            WHERE u.TenantId=@TenantId AND u.NormalizedUsername=@Username;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@Username", normalizedUsername);
        return await ReadUserAsync(connection, null, command, cancellationToken);
    }

    public async Task RecordFailedLoginAsync(
        Guid userId,
        DateTimeOffset now,
        int maxAttempts,
        TimeSpan lockoutDuration,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.AppUsers WITH (UPDLOCK)
            SET AccessFailedCount=AccessFailedCount+1,
                LockoutEnd=CASE
                    WHEN AccessFailedCount+1>=@MaxAttempts THEN @LockoutEnd
                    ELSE LockoutEnd END,
                UpdatedAt=@Now
            WHERE UserId=@UserId;
            """, connection);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@MaxAttempts", maxAttempts);
        command.Parameters.AddWithValue("@LockoutEnd", now.Add(lockoutDuration));
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<AuthenticationSessionRecord> OpenAsync(
        OpenAuthenticationSessionCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        await LockUserAsync(
            connection, transaction, command.User.TenantId,
            command.User.UserId, cancellationToken);
        await ExpireStaleSessionAsync(
            connection, transaction, command.User.TenantId, command.User.UserId,
            command.Now, cancellationToken);
        await ExpireStaleOfflineLeaseAsync(
            connection, transaction, command.User.TenantId, command.User.UserId,
            command.Now, cancellationToken);
        // A new successful login takes over the user's previous session. This is
        // intentional for POS and browser recovery: the previous token and lease
        // are revoked immediately and remain auditable.
        await RevokeActiveOfflineLeaseAsync(
            connection, transaction, command.User.TenantId,
            command.User.UserId, command.Now, cancellationToken);
        await RevokeActiveSessionsAsync(
            connection, transaction, command.User.TenantId,
            command.User.UserId, command.Now, cancellationToken);
        await RecordSuccessfulLoginAsync(connection, transaction, command, cancellationToken);
        var identity = new AuthenticationSessionIdentity(
            ids.NewId(), command.User.UserId, command.User.TenantId, command.ClientId);
        await using var insert = new SqlCommand("""
            INSERT dbo.AuthenticationSessions
              (AuthenticationSessionId,TenantId,UserId,ClientId,ClientDescription,
               IpAddress,RefreshTokenHash,IssuedAt,ExpiresAt,LastSeenAt,Status)
            VALUES
              (@SessionId,@TenantId,@UserId,@ClientId,@ClientDescription,
               @IpAddress,@RefreshTokenHash,@IssuedAt,@ExpiresAt,@IssuedAt,N'Active');
            """, connection, transaction);
        AddIdentityParameters(insert, identity);
        insert.Parameters.AddWithValue(
            "@ClientDescription", (object?)command.ClientDescription ?? DBNull.Value);
        insert.Parameters.AddWithValue("@IpAddress", (object?)command.IpAddress ?? DBNull.Value);
        insert.Parameters.Add("@RefreshTokenHash", SqlDbType.VarBinary, 32).Value =
            command.RefreshTokenHash;
        insert.Parameters.AddWithValue("@IssuedAt", command.Now);
        insert.Parameters.AddWithValue("@ExpiresAt", command.RefreshTokenExpiresAt);
        try
        {
            await insert.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            throw new AuthenticationSessionConflictException(
                "The user already has an active authentication session.");
        }
        return new AuthenticationSessionRecord(
            identity,
            command.User with
            {
                AccessFailedCount = 0,
                LockoutEnd = null
            },
            command.Now,
            command.RefreshTokenExpiresAt);
    }

    public async Task<AuthenticationSessionRecord> RotateAsync(
        RotateAuthenticationSessionCommand command,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await ReadSessionForUpdateAsync(
            connection, transaction, command.Identity, cancellationToken)
            ?? throw new AuthenticationDeniedException(
                "The authentication session does not exist.");
        if (!string.Equals(state.Status, "Active", StringComparison.Ordinal) ||
            state.ExpiresAt <= command.Now)
        {
            await ExpireByIdAsync(
                connection, transaction, command.Identity.AuthenticationSessionId,
                command.Now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthenticationDeniedException(
                "The authentication session is inactive or expired.");
        }
        if (state.ClientId != command.Identity.ClientId ||
            !CryptographicOperations.FixedTimeEquals(
                state.RefreshTokenHash, command.CurrentRefreshTokenHash))
        {
            await RevokeByIdAsync(
                connection, transaction, command.Identity.AuthenticationSessionId,
                "RefreshTokenMismatch", command.Now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new AuthenticationDeniedException(
                "Refresh token reuse or client mismatch was detected.");
        }

        await using var update = new SqlCommand("""
            UPDATE dbo.AuthenticationSessions
            SET RefreshTokenHash=@NewHash,ExpiresAt=@ExpiresAt,
                LastSeenAt=@Now,UpdatedAt=@Now
            WHERE AuthenticationSessionId=@SessionId AND Status=N'Active';
            """, connection, transaction);
        update.Parameters.AddWithValue(
            "@SessionId", command.Identity.AuthenticationSessionId);
        update.Parameters.Add("@NewHash", SqlDbType.VarBinary, 32).Value =
            command.NewRefreshTokenHash;
        update.Parameters.AddWithValue("@ExpiresAt", command.RefreshTokenExpiresAt);
        update.Parameters.AddWithValue("@Now", command.Now);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException(
                "The authentication session changed concurrently.");
        var user = await ReadUserByIdAsync(
            connection, transaction, command.Identity.UserId,
            command.Identity.TenantId, cancellationToken)
            ?? throw new AuthenticationDeniedException("The user is inactive or missing.");
        await transaction.CommitAsync(cancellationToken);
        return new AuthenticationSessionRecord(
            command.Identity, user, state.IssuedAt, command.RefreshTokenExpiresAt);
    }

    public async Task RevokeAsync(
        AuthenticationSessionIdentity identity,
        byte[] refreshTokenHash,
        string reason,
        DateTimeOffset revokedAt,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await ReadSessionForUpdateAsync(
            connection, transaction, identity, cancellationToken);
        if (state is null || !string.Equals(state.Status, "Active", StringComparison.Ordinal))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        if (state.ClientId != identity.ClientId ||
            !CryptographicOperations.FixedTimeEquals(state.RefreshTokenHash, refreshTokenHash))
            throw new AuthenticationDeniedException(
                "The refresh token does not belong to the active session.");
        await RevokeByIdAsync(
            connection, transaction, identity.AuthenticationSessionId,
            reason, revokedAt, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> IsActiveAsync(
        ParsedAuthenticationToken token,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.AuthenticationSessions
            SET LastSeenAt=CASE WHEN LastSeenAt<DATEADD(minute,-1,@Now)
                                THEN @Now ELSE LastSeenAt END,
                UpdatedAt=CASE WHEN LastSeenAt<DATEADD(minute,-1,@Now)
                               THEN @Now ELSE UpdatedAt END
            OUTPUT inserted.AuthenticationSessionId
            WHERE AuthenticationSessionId=@SessionId
              AND UserId=@UserId AND TenantId=@TenantId
              AND Status=N'Active' AND ExpiresAt>@Now
              AND EXISTS(SELECT 1 FROM dbo.Tenants tenant WHERE tenant.TenantId=@TenantId AND tenant.IsActive=1);
            """, connection);
        command.Parameters.AddWithValue("@SessionId", token.AuthenticationSessionId);
        command.Parameters.AddWithValue("@UserId", token.UserId);
        command.Parameters.AddWithValue("@TenantId", token.TenantId);
        command.Parameters.AddWithValue("@Now", now);
        return await command.ExecuteScalarAsync(cancellationToken) is Guid;
    }

    public async Task<AuthenticationUserRecord?> GetUserAsync(
        ParsedAuthenticationToken token,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT 1
            FROM dbo.AuthenticationSessions
            WHERE AuthenticationSessionId=@SessionId
              AND UserId=@UserId AND TenantId=@TenantId
              AND Status=N'Active' AND ExpiresAt>SYSDATETIMEOFFSET();
            """, connection);
        command.Parameters.AddWithValue("@SessionId", token.AuthenticationSessionId);
        command.Parameters.AddWithValue("@UserId", token.UserId);
        command.Parameters.AddWithValue("@TenantId", token.TenantId);
        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            return null;
        return await ReadUserByIdAsync(
            connection, null, token.UserId, token.TenantId, cancellationToken);
    }

    private static async Task<AuthenticationUserRecord?> ReadUserByUsernameAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string tenantKey, string normalizedUsername,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT u.UserId,u.TenantId,t.TenantKey,u.Username,u.Email,u.FirstName,u.LastName,u.AvatarUrl,
                   u.PasswordHash,u.IsActive,u.AccessFailedCount,u.LockoutEnd
            FROM dbo.AppUsers u
            INNER JOIN dbo.Tenants t ON t.TenantId=u.TenantId AND t.IsActive=1
            WHERE t.TenantKey=@TenantKey AND u.NormalizedUsername=@Username;

            """, connection, transaction);
        command.Parameters.AddWithValue("@Username", normalizedUsername);
        command.Parameters.AddWithValue("@TenantKey", tenantKey);
        return await ReadUserAsync(connection, transaction, command, cancellationToken);
    }

    private static async Task<AuthenticationUserRecord?> ReadUserByIdAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT u.UserId,u.TenantId,t.TenantKey,u.Username,u.Email,u.FirstName,u.LastName,u.AvatarUrl,
                   u.PasswordHash,u.IsActive,u.AccessFailedCount,u.LockoutEnd
            FROM dbo.AppUsers u
            INNER JOIN dbo.Tenants t ON t.TenantId=u.TenantId AND t.IsActive=1
            WHERE u.UserId=@UserId AND u.TenantId=@TenantId AND u.IsActive=1;

            """, connection, transaction);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        return await ReadUserAsync(connection, transaction, command, cancellationToken);
    }

    private static async Task<AuthenticationUserRecord?> ReadUserAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        SqlCommand command,
        CancellationToken cancellationToken)
    {
        Guid userId;
        Guid tenantId;
        string username;
        string tenantKey;
        string email;
        string firstName;
        string lastName;
        string? avatar;
        string? passwordHash;
        bool active;
        int failures;
        DateTimeOffset? lockout;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            userId = reader.GetGuid(0);
            tenantId = reader.GetGuid(1);
            tenantKey = reader.GetString(2);
            username = reader.GetString(3);
            email = reader.GetString(4);
            firstName = reader.GetString(5);
            lastName = reader.GetString(6);
            avatar = reader.IsDBNull(7) ? null : reader.GetString(7);
            passwordHash = reader.IsDBNull(8) ? null : reader.GetString(8);
            active = reader.GetBoolean(9);
            failures = reader.GetInt32(10);
            lockout = reader.IsDBNull(11) ? null : reader.GetDateTimeOffset(11);
        }
        var roles = await ReadStringsAsync(
            connection, transaction,
            """
            SELECT DISTINCT r.Name
            FROM dbo.UserRoles ur
            INNER JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1
            WHERE ur.UserId=@UserId
              AND (r.TenantId IS NULL OR r.TenantId=@TenantId)
            ORDER BY r.Name;
            """, userId, tenantId, cancellationToken);
        var permissions = await ReadStringsAsync(
            connection, transaction,
            """
            SELECT DISTINCT p.Resource
            FROM dbo.UserRoles ur
            INNER JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1
            INNER JOIN dbo.RolePermissions rp ON rp.RoleId=r.RoleId
            INNER JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
            WHERE ur.UserId=@UserId
              AND (r.TenantId IS NULL OR r.TenantId=@TenantId)
            ORDER BY p.Resource;
            """, userId, tenantId, cancellationToken);
        return new AuthenticationUserRecord(
            userId, tenantId, username, tenantKey, email, firstName, lastName, avatar,
            passwordHash, active, failures, lockout, roles, permissions);
    }

    private static async Task<IReadOnlyList<string>> ReadStringsAsync(
        SqlConnection connection,
        SqlTransaction? transaction,
        string sql,
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            values.Add(reader.GetString(0));
        return values;
    }

    private static async Task LockUserAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT UserId
            FROM dbo.AppUsers WITH (UPDLOCK,HOLDLOCK)
            WHERE TenantId=@TenantId AND UserId=@UserId AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        if (await command.ExecuteScalarAsync(cancellationToken) is not Guid)
            throw new AuthenticationDeniedException(
                "The user is inactive or missing.");
    }

    private static async Task ExpireStaleSessionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.AuthenticationSessions WITH (UPDLOCK,HOLDLOCK)
            SET Status=N'Expired',RevokedAt=@Now,
                RevocationReason=N'Expired',UpdatedAt=@Now
            WHERE TenantId=@TenantId AND UserId=@UserId
              AND Status=N'Active' AND ExpiresAt<=@Now;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RevokeActiveSessionsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.AuthenticationSessions WITH (UPDLOCK,HOLDLOCK)
            SET Status=N'Revoked',RevokedAt=@Now,
                RevocationReason=N'ReplacedByNewLogin',
                LastSeenAt=@Now,UpdatedAt=@Now
            WHERE TenantId=@TenantId AND UserId=@UserId AND Status=N'Active';
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static async Task ExpireStaleOfflineLeaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.OfflineAuthenticationLeases WITH (UPDLOCK,HOLDLOCK)
            SET Status=N'Expired',EndedAt=@Now,
                EndReason=N'Expired',UpdatedAt=@Now
            WHERE TenantId=@TenantId AND UserId=@UserId
              AND Status=N'Active' AND ExpiresAt<=@Now;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task RevokeActiveOfflineLeaseAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.OfflineAuthenticationLeases WITH (UPDLOCK,HOLDLOCK)
            SET Status=N'Revoked',EndedAt=@Now,
                EndReason=N'ReplacedByOnlineLogin',UpdatedAt=@Now
            WHERE TenantId=@TenantId AND UserId=@UserId AND Status=N'Active';
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    private static async Task RecordSuccessfulLoginAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OpenAuthenticationSessionCommand command,
        CancellationToken cancellationToken)
    {
        await using var update = new SqlCommand("""
            UPDATE dbo.AppUsers
            SET AccessFailedCount=0,LockoutEnd=NULL,LastLoginAt=@Now,UpdatedAt=@Now,
                PosOfflinePasswordSalt=COALESCE(PosOfflinePasswordSalt,@Salt),
                PosOfflinePasswordHash=COALESCE(PosOfflinePasswordHash,@Hash),
                PosOfflinePasswordIterations=COALESCE(PosOfflinePasswordIterations,@Iterations),
                PosOfflinePasswordChangedAt=COALESCE(PosOfflinePasswordChangedAt,@ChangedAt)
            WHERE UserId=@UserId AND TenantId=@TenantId AND IsActive=1;
            """, connection, transaction);
        update.Parameters.AddWithValue("@UserId", command.User.UserId);
        update.Parameters.AddWithValue("@TenantId", command.User.TenantId);
        update.Parameters.AddWithValue("@Now", command.Now);
        var verifier = command.OfflinePasswordVerifier
            ?? throw new AuthenticationValidationException(
                "The offline password verifier is required for a successful login.");
        update.Parameters.Add("@Salt", SqlDbType.VarBinary, 16).Value = verifier.Salt;
        update.Parameters.Add("@Hash", SqlDbType.VarBinary, 32).Value = verifier.Hash;
        update.Parameters.AddWithValue("@Iterations", verifier.Iterations);
        update.Parameters.AddWithValue("@ChangedAt", verifier.ChangedAt);
        if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new AuthenticationDeniedException("The user is inactive or missing.");
    }

    private static async Task<SessionState?> ReadSessionForUpdateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        AuthenticationSessionIdentity identity,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT ClientId,RefreshTokenHash,IssuedAt,ExpiresAt,Status
            FROM dbo.AuthenticationSessions WITH (UPDLOCK,HOLDLOCK)
            WHERE AuthenticationSessionId=@SessionId
              AND TenantId=@TenantId AND UserId=@UserId;
            """, connection, transaction);
        AddIdentityParameters(command, identity);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new SessionState(
                reader.GetGuid(0),
                (byte[])reader[1],
                reader.GetFieldValue<DateTimeOffset>(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetString(4))
            : null;
    }

    private static Task ExpireByIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        SetStatusAsync(
            connection, transaction, sessionId, "Expired", "Expired",
            now, cancellationToken);

    private static Task RevokeByIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        SetStatusAsync(
            connection, transaction, sessionId, "Revoked", reason,
            now, cancellationToken);

    private static async Task SetStatusAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid sessionId,
        string status,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.AuthenticationSessions
            SET Status=@Status,RevokedAt=@Now,RevocationReason=@Reason,
                LastSeenAt=@Now,UpdatedAt=@Now
            WHERE AuthenticationSessionId=@SessionId AND Status=N'Active';
            """, connection, transaction);
        command.Parameters.AddWithValue("@SessionId", sessionId);
        command.Parameters.AddWithValue("@Status", status);
        command.Parameters.AddWithValue("@Reason", reason);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddIdentityParameters(
        SqlCommand command,
        AuthenticationSessionIdentity identity)
    {
        command.Parameters.AddWithValue(
            "@SessionId", identity.AuthenticationSessionId);
        command.Parameters.AddWithValue("@TenantId", identity.TenantId);
        command.Parameters.AddWithValue("@UserId", identity.UserId);
        command.Parameters.AddWithValue("@ClientId", identity.ClientId);
    }

    private sealed record SessionState(
        Guid ClientId,
        byte[] RefreshTokenHash,
        DateTimeOffset IssuedAt,
        DateTimeOffset ExpiresAt,
        string Status);
}
