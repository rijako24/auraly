using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Host;

public sealed record PosLocalLoginRequest(string Username, string Password);

public sealed record PosLocalUserSession(
    Guid SessionId,
    Guid WorkSessionId,
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    DateTimeOffset ExpiresAt,
    string? Token);

public sealed record PosLocalIdentitySummary(
    Guid UserId,
    string Username,
    string DisplayName,
    DateTimeOffset PasswordChangedAt,
    IReadOnlyList<string> Permissions);

public sealed class PosLocalLoginException(
    string code,
    string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed partial class PosLocalIdentityStore(
    string connectionString,
    string keyDirectory,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider)
{
    private const int MaximumFailures = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SessionDuration = TimeSpan.FromHours(24);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS PosOfflineUsers(
                UserId TEXT NOT NULL PRIMARY KEY,
                Username TEXT NOT NULL,
                NormalizedUsername TEXT NOT NULL UNIQUE,
                DisplayName TEXT NOT NULL,
                ProtectedPasswordVerifier TEXT NOT NULL,
                ProtectedSupervisorCredential TEXT NULL,
                FailedCount INTEGER NOT NULL DEFAULT 0,
                LockedUntil TEXT NULL);
            CREATE TABLE IF NOT EXISTS PosOfflineUserPermissions(
                UserId TEXT NOT NULL,
                PermissionCode TEXT NOT NULL,
                PRIMARY KEY(UserId,PermissionCode),
                FOREIGN KEY(UserId) REFERENCES PosOfflineUsers(UserId) ON DELETE CASCADE);
            CREATE TABLE IF NOT EXISTS PosIdentityState(
                Singleton INTEGER NOT NULL PRIMARY KEY CHECK(Singleton=1),
                Revision TEXT NOT NULL,
                IssuedAt TEXT NOT NULL,
                ValidUntil TEXT NOT NULL,
                LastSynchronizedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PosLocalUserSessions(
                SessionId TEXT NOT NULL PRIMARY KEY,
                WorkSessionId TEXT NOT NULL,
                UserId TEXT NOT NULL,
                TokenHash BLOB NOT NULL UNIQUE,
                StartedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                EndReason TEXT NULL,
                FOREIGN KEY(UserId) REFERENCES PosOfflineUsers(UserId));
            CREATE INDEX IF NOT EXISTS IX_PosLocalUserSessions_Active
                ON PosLocalUserSessions(EndedAt,ExpiresAt);
            CREATE TABLE IF NOT EXISTS PosClosedWorkSessions(
                WorkSessionId TEXT NOT NULL PRIMARY KEY,
                UserId TEXT NOT NULL,
                ClosedAt TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS PosLocalApprovalAudits(
                AuthorizationId TEXT NOT NULL PRIMARY KEY,
                RequestedByUserId TEXT NOT NULL,
                AuthorizedByUserId TEXT NOT NULL,
                PermissionResource TEXT NOT NULL,
                DraftId TEXT NOT NULL,
                LineId TEXT NULL,
                AuthorizationMethod TEXT NOT NULL,
                Status TEXT NOT NULL,
                AuthorizedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                FOREIGN KEY(RequestedByUserId) REFERENCES PosOfflineUsers(UserId),
                FOREIGN KEY(AuthorizedByUserId) REFERENCES PosOfflineUsers(UserId));
            CREATE INDEX IF NOT EXISTS IX_PosLocalApprovalAudits_Draft
                ON PosLocalApprovalAudits(DraftId,AuthorizedAt);
            CREATE TABLE IF NOT EXISTS PosConsumedOneTimeSupervisorCredentials(
                UserId TEXT NOT NULL,
                ChangedAt TEXT NOT NULL,
                ConsumedAt TEXT NOT NULL,
                PRIMARY KEY(UserId,ChangedAt));
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await UpgradeWorkSessionsAsync(connection, cancellationToken);
        await UpgradeSupervisorCredentialsAsync(connection, cancellationToken);
    }

    public async Task ApplySnapshotAsync(
        PosOfflineIdentitySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(connection, transaction,
            "DELETE FROM PosOfflineUserPermissions;", cancellationToken);
        await ExecuteAsync(connection, transaction,
            "DELETE FROM PosLocalUserSessions WHERE EndedAt IS NOT NULL;", cancellationToken);
        await ExecuteAsync(connection, transaction,
            """
            DELETE FROM PosOfflineUsers
            WHERE UserId NOT IN (SELECT UserId FROM PosLocalUserSessions WHERE EndedAt IS NULL)
              AND UserId NOT IN (SELECT RequestedByUserId FROM PosLocalApprovalAudits)
              AND UserId NOT IN (SELECT AuthorizedByUserId FROM PosLocalApprovalAudits);
            """,
            cancellationToken);

        foreach (var user in snapshot.Users)
        {
            var protectedVerifier = PosEdgeProtectedSecret.ProtectIdentityVerifier(
                keyDirectory,
                JsonSerializer.Serialize(user.PasswordVerifier));
            var supervisorCredential = user.SupervisorCredential;
            if (supervisorCredential?.IsOneTime == true)
            {
                await using var consumed = connection.CreateCommand();
                consumed.Transaction = (SqliteTransaction)transaction;
                consumed.CommandText = """
                    SELECT COUNT(1)
                    FROM PosConsumedOneTimeSupervisorCredentials
                    WHERE UserId=$id AND ChangedAt=$changedAt;
                    """;
                consumed.Parameters.AddWithValue("$id", user.UserId.ToString("D"));
                consumed.Parameters.AddWithValue("$changedAt", Format(supervisorCredential.ChangedAt));
                if (Convert.ToInt32(await consumed.ExecuteScalarAsync(cancellationToken)) > 0)
                    supervisorCredential = null;
            }
            var protectedSupervisorCredential = supervisorCredential is null
                ? null
                : PosEdgeProtectedSecret.ProtectIdentityVerifier(
                    keyDirectory,
                    JsonSerializer.Serialize(supervisorCredential));
            await using var userCommand = connection.CreateCommand();
            userCommand.Transaction = (SqliteTransaction)transaction;
            userCommand.CommandText = """
                INSERT INTO PosOfflineUsers(
                    UserId,Username,NormalizedUsername,DisplayName,
                    ProtectedPasswordVerifier,ProtectedSupervisorCredential,
                    FailedCount,LockedUntil)
                VALUES($id,$username,$normalized,$display,$verifier,$supervisor,0,NULL)
                ON CONFLICT(UserId) DO UPDATE SET
                    Username=excluded.Username,
                    NormalizedUsername=excluded.NormalizedUsername,
                    DisplayName=excluded.DisplayName,
                    ProtectedPasswordVerifier=excluded.ProtectedPasswordVerifier,
                    ProtectedSupervisorCredential=excluded.ProtectedSupervisorCredential;
                """;
            userCommand.Parameters.AddWithValue("$id", user.UserId.ToString("D"));
            userCommand.Parameters.AddWithValue("$username", user.Username);
            userCommand.Parameters.AddWithValue(
                "$normalized", Normalize(user.Username));
            userCommand.Parameters.AddWithValue("$display", user.DisplayName);
            userCommand.Parameters.AddWithValue("$verifier", protectedVerifier);
            userCommand.Parameters.AddWithValue(
                "$supervisor", (object?)protectedSupervisorCredential ?? DBNull.Value);
            await userCommand.ExecuteNonQueryAsync(cancellationToken);

            foreach (var permission in user.Permissions.Distinct(StringComparer.Ordinal))
            {
                await using var permissionCommand = connection.CreateCommand();
                permissionCommand.Transaction = (SqliteTransaction)transaction;
                permissionCommand.CommandText = """
                    INSERT OR IGNORE INTO PosOfflineUserPermissions(UserId,PermissionCode)
                    VALUES($id,$permission);
                    """;
                permissionCommand.Parameters.AddWithValue(
                    "$id", user.UserId.ToString("D"));
                permissionCommand.Parameters.AddWithValue("$permission", permission);
                await permissionCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using var state = connection.CreateCommand();
        state.Transaction = (SqliteTransaction)transaction;
        state.CommandText = """
            INSERT INTO PosIdentityState(
                Singleton,Revision,IssuedAt,ValidUntil,LastSynchronizedAt)
            VALUES(1,$revision,$issued,$valid,$now)
            ON CONFLICT(Singleton) DO UPDATE SET
                Revision=excluded.Revision,
                IssuedAt=excluded.IssuedAt,
                ValidUntil=excluded.ValidUntil,
                LastSynchronizedAt=excluded.LastSynchronizedAt;
            """;
        state.Parameters.AddWithValue("$revision", snapshot.Revision);
        state.Parameters.AddWithValue("$issued", Format(snapshot.IssuedAt));
        state.Parameters.AddWithValue("$valid", Format(snapshot.ValidUntil));
        state.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        await state.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PosLocalIdentitySummary>> ReadIdentitySummariesAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.UserId,u.Username,u.DisplayName,u.ProtectedPasswordVerifier,
                   p.PermissionCode
            FROM PosOfflineUsers u
            LEFT JOIN PosOfflineUserPermissions p ON p.UserId=u.UserId
            ORDER BY u.UserId,p.PermissionCode;
            """;
        var users = new Dictionary<Guid, MutableIdentitySummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var userId = Guid.Parse(reader.GetString(0));
            if (!users.TryGetValue(userId, out var user))
            {
                var verifier = JsonSerializer.Deserialize<PosOfflinePasswordVerifier>(
                    PosEdgeProtectedSecret.UnprotectIdentityVerifier(
                        keyDirectory, reader.GetString(3)))
                    ?? throw new InvalidDataException(
                        $"El verificador local del usuario {userId:D} no es válido.");
                user = new MutableIdentitySummary(
                    userId, reader.GetString(1), reader.GetString(2), verifier.ChangedAt);
                users.Add(userId, user);
            }
            if (!reader.IsDBNull(4)) user.Permissions.Add(reader.GetString(4));
        }
        return users.Values
            .Select(user => new PosLocalIdentitySummary(
                user.UserId, user.Username, user.DisplayName,
                user.PasswordChangedAt, user.Permissions.ToArray()))
            .ToArray();
    }

    public async Task<bool> HasIdentitySnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM PosIdentityState WHERE Singleton=1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<bool> ContainsUserAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(1)
            FROM PosOfflineUsers
            WHERE NormalizedUsername=$username;
            """;
        command.Parameters.AddWithValue("$username", Normalize(username));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrEmpty(request.Password))
            throw new PosLocalLoginException(
                "InvalidCredentials", "Usuario y contraseña son obligatorios.");
        if (!await HasIdentitySnapshotAsync(cancellationToken))
            throw new PosLocalLoginException(
                "IdentityUnavailable",
                "La información de acceso local aún no está lista. Conecta este dispositivo con Auraly para completar la preparación.");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        var user = await ReadUserAsync(
            connection, (SqliteTransaction)transaction, request.Username,
            cancellationToken)
            ?? throw new PosLocalLoginException(
                "InvalidCredentials", "Usuario o contraseña incorrectos.");
        var now = timeProvider.GetUtcNow();
        if (user.LockedUntil is not null && user.LockedUntil > now)
            throw new PosLocalLoginException(
                "Locked",
                $"Acceso bloqueado temporalmente hasta {user.LockedUntil:HH:mm}.");

        var verifier = JsonSerializer.Deserialize<PosOfflinePasswordVerifier>(
            PosEdgeProtectedSecret.UnprotectIdentityVerifier(
                keyDirectory, user.ProtectedVerifier))
            ?? throw new InvalidDataException("The local password verifier is invalid.");
        if (!PosOfflinePasswordHasher.Verify(request.Password, verifier))
        {
            await RecordFailureAsync(
                connection, (SqliteTransaction)transaction, user.UserId,
                user.FailedCount + 1, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw new PosLocalLoginException(
                "InvalidCredentials", "Usuario o contraseña incorrectos.");
        }

        return await CreateSessionAsync(
            connection,
            (SqliteTransaction)transaction,
            user,
            now,
            cancellationToken);
    }

    public async Task<PosLocalUserSession> LoginFromEnrollmentAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (!await HasIdentitySnapshotAsync(cancellationToken))
            throw new PosLocalLoginException(
                "IdentityUnavailable",
                "La información de acceso local aún no está lista. Espera a que termine la descarga inicial.");
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var user = await ReadUserByIdAsync(
            connection, (SqliteTransaction)transaction, userId, cancellationToken)
            ?? throw new PosLocalLoginException(
                "IdentityUnavailable",
                "El usuario que preparó la caja no está disponible para trabajar localmente.");
        var permissions = await ReadPermissionsAsync(
            connection, (SqliteTransaction)transaction, user.UserId, cancellationToken);
        if (!permissions.Contains(CommercePermissionCodes.SalesCreate, StringComparer.Ordinal))
            throw new PosLocalLoginException(
                "PermissionDenied",
                "El usuario que preparó la caja no tiene permiso para crear ventas.");
        return await CreateSessionAsync(
            connection,
            (SqliteTransaction)transaction,
            user,
            timeProvider.GetUtcNow(),
            cancellationToken);
    }

    private async Task<PosLocalUserSession> CreateSessionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        LocalUser user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using (var close = connection.CreateCommand())
        {
            close.Transaction = transaction;
            close.CommandText = """
                UPDATE PosLocalUserSessions
                SET EndedAt=$now,EndReason='UserChanged'
                WHERE EndedAt IS NULL;
                UPDATE PosOfflineUsers
                SET FailedCount=0,LockedUntil=NULL
                WHERE UserId=$userId;
                """;
            close.Parameters.AddWithValue("$now", Format(now));
            close.Parameters.AddWithValue("$userId", user.UserId.ToString("D"));
            await close.ExecuteNonQueryAsync(cancellationToken);
        }

        var sessionId = ids.NewId();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var expiresAt = now.Add(SessionDuration);
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO PosLocalUserSessions(
                    SessionId,WorkSessionId,UserId,TokenHash,StartedAt,ExpiresAt)
                VALUES($session,$workSession,$user,$hash,$now,$expires);
                """;
            insert.Parameters.AddWithValue("$session", sessionId.ToString("D"));
            // Authentication does not open an operational cash session. The POS
            // entry point assigns the locally-created WorkSession explicitly.
            insert.Parameters.AddWithValue("$workSession", Guid.Empty.ToString("D"));
            insert.Parameters.AddWithValue("$user", user.UserId.ToString("D"));
            insert.Parameters.AddWithValue("$hash", TokenHash(token));
            insert.Parameters.AddWithValue("$now", Format(now));
            insert.Parameters.AddWithValue("$expires", Format(expiresAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        var permissions = await ReadPermissionsAsync(
            connection, transaction, user.UserId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PosLocalUserSession(
            sessionId, Guid.Empty, user.UserId, user.Username, user.DisplayName,
            permissions, expiresAt, token);
    }

    public async Task<PosLocalUserSession?> ResolveAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT s.SessionId,s.WorkSessionId,u.UserId,u.Username,u.DisplayName,s.ExpiresAt
            FROM PosLocalUserSessions s
            JOIN PosOfflineUsers u ON u.UserId=s.UserId
            WHERE s.TokenHash=$hash AND s.EndedAt IS NULL AND s.ExpiresAt>$now;
            """;
        command.Parameters.AddWithValue("$hash", TokenHash(token));
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var sessionId = Guid.Parse(reader.GetString(0));
        var workSessionId = Guid.Parse(reader.GetString(1));
        var userId = Guid.Parse(reader.GetString(2));
        var username = reader.GetString(3);
        var displayName = reader.GetString(4);
        var expires = timeProvider.GetUtcNow().Add(SessionDuration);
        await reader.CloseAsync();
        await using (var touch = connection.CreateCommand())
        {
            touch.CommandText = """
                UPDATE PosLocalUserSessions SET ExpiresAt=$expires
                WHERE SessionId=$session AND EndedAt IS NULL;
                """;
            touch.Parameters.AddWithValue("$expires", Format(expires));
            touch.Parameters.AddWithValue("$session", sessionId.ToString("D"));
            await touch.ExecuteNonQueryAsync(cancellationToken);
        }
        var permissions = await ReadPermissionsAsync(
            connection, null, userId, cancellationToken);
        return new PosLocalUserSession(
            sessionId, workSessionId, userId, username, displayName, permissions, expires, null);
    }

    public async Task AssignWorkSessionAsync(
        Guid sessionId,
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PosLocalUserSessions
            SET WorkSessionId=$workSession
            WHERE SessionId=$session AND EndedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$workSession", workSessionId.ToString("D"));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                "La sesión local cambió mientras se vinculaba con Auraly Server.");
    }

    public async Task LogoutAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PosLocalUserSessions
            SET EndedAt=$now,EndReason='Logout'
            WHERE TokenHash=$hash AND EndedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        command.Parameters.AddWithValue("$hash", TokenHash(token));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RevokeActiveSessionsAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PosLocalUserSessions
            SET EndedAt=$now,EndReason=$reason
            WHERE EndedAt IS NULL;
            """;
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        command.Parameters.AddWithValue("$reason", reason);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> SessionEndReasonAsync(
        string? token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EndReason
            FROM PosLocalUserSessions
            WHERE TokenHash=$hash AND EndedAt IS NOT NULL
            ORDER BY EndedAt DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$hash", TokenHash(token));
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<DateTimeOffset> WorkSessionOpenedAtAsync(
        Guid workSessionId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OpenedAt FROM PosLocalWorkSessions
            WHERE WorkSessionId=$workSession;
            """;
        command.Parameters.AddWithValue("$workSession", workSessionId.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken) as string;
        return value is null
            ? timeProvider.GetUtcNow()
            : DateTimeOffset.Parse(value);
    }

    private static async Task UpgradeWorkSessionsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('PosLocalUserSessions');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(1));
        }
        if (columns.Contains("WorkSessionId")) return;

        await using var upgrade = connection.CreateCommand();
        upgrade.CommandText = """
            ALTER TABLE PosLocalUserSessions ADD COLUMN WorkSessionId TEXT NULL;
            UPDATE PosLocalUserSessions
            SET WorkSessionId=SessionId
            WHERE WorkSessionId IS NULL;
            CREATE UNIQUE INDEX IF NOT EXISTS UX_PosLocalUserSessions_WorkSession
                ON PosLocalUserSessions(WorkSessionId);
            """;
        await upgrade.ExecuteNonQueryAsync(cancellationToken);
    }
    private static async Task UpgradeSupervisorCredentialsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('PosOfflineUsers');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(1));
        }
        if (columns.Contains("ProtectedSupervisorCredential")) return;
        await using var upgrade = connection.CreateCommand();
        upgrade.CommandText =
            "ALTER TABLE PosOfflineUsers ADD COLUMN ProtectedSupervisorCredential TEXT NULL;";
        await upgrade.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<LocalUser?> ReadUserAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string username,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT UserId,Username,DisplayName,ProtectedPasswordVerifier,
                   FailedCount,LockedUntil
            FROM PosOfflineUsers
            WHERE NormalizedUsername=$username;
            """;
        command.Parameters.AddWithValue("$username", Normalize(username));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new LocalUser(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)));
    }

    private static async Task<LocalUser?> ReadUserByIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT UserId,Username,DisplayName,ProtectedPasswordVerifier,
                   FailedCount,LockedUntil
            FROM PosOfflineUsers
            WHERE UserId=$userId;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new LocalUser(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.IsDBNull(5) ? null : DateTimeOffset.Parse(reader.GetString(5)));
    }

    private static async Task<IReadOnlyList<string>> ReadPermissionsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT PermissionCode FROM PosOfflineUserPermissions
            WHERE UserId=$userId ORDER BY PermissionCode;
            """;
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        var permissions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            permissions.Add(reader.GetString(0));
        return permissions;
    }

    private static async Task RecordFailureAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid userId,
        int failures,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE PosOfflineUsers
            SET FailedCount=$failures,
                LockedUntil=$locked
            WHERE UserId=$userId;
            """;
        command.Parameters.AddWithValue("$failures", failures);
        command.Parameters.AddWithValue(
            "$locked",
            failures >= MaximumFailures
                ? Format(now.Add(LockoutDuration))
                : DBNull.Value);
        command.Parameters.AddWithValue("$userId", userId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] TokenHash(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static string Normalize(string value) =>
        value.Trim().ToUpperInvariant();

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O");

    private sealed record LocalUser(
        Guid UserId,
        string Username,
        string DisplayName,
        string ProtectedVerifier,
        int FailedCount,
        DateTimeOffset? LockedUntil);

    private sealed record MutableIdentitySummary(
        Guid UserId,
        string Username,
        string DisplayName,
        DateTimeOffset PasswordChangedAt)
    {
        public List<string> Permissions { get; } = [];
    }
}
