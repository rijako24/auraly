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
    Guid UserId,
    string Username,
    string DisplayName,
    IReadOnlyList<string> Permissions,
    DateTimeOffset ExpiresAt,
    string? Token);

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
    private static readonly TimeSpan SessionDuration = TimeSpan.FromHours(12);

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
                UserId TEXT NOT NULL,
                TokenHash BLOB NOT NULL UNIQUE,
                StartedAt TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                EndedAt TEXT NULL,
                EndReason TEXT NULL,
                FOREIGN KEY(UserId) REFERENCES PosOfflineUsers(UserId));
            CREATE INDEX IF NOT EXISTS IX_PosLocalUserSessions_Active
                ON PosLocalUserSessions(EndedAt,ExpiresAt);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            "DELETE FROM PosOfflineUsers WHERE UserId NOT IN (SELECT UserId FROM PosLocalUserSessions WHERE EndedAt IS NULL);",
            cancellationToken);

        foreach (var user in snapshot.Users)
        {
            var protectedVerifier = PosEdgeProtectedSecret.ProtectIdentityVerifier(
                keyDirectory,
                JsonSerializer.Serialize(user.PasswordVerifier));
            await using var userCommand = connection.CreateCommand();
            userCommand.Transaction = (SqliteTransaction)transaction;
            userCommand.CommandText = """
                INSERT INTO PosOfflineUsers(
                    UserId,Username,NormalizedUsername,DisplayName,
                    ProtectedPasswordVerifier,FailedCount,LockedUntil)
                VALUES($id,$username,$normalized,$display,$verifier,0,NULL)
                ON CONFLICT(UserId) DO UPDATE SET
                    Username=excluded.Username,
                    NormalizedUsername=excluded.NormalizedUsername,
                    DisplayName=excluded.DisplayName,
                    ProtectedPasswordVerifier=excluded.ProtectedPasswordVerifier;
                """;
            userCommand.Parameters.AddWithValue("$id", user.UserId.ToString("D"));
            userCommand.Parameters.AddWithValue("$username", user.Username);
            userCommand.Parameters.AddWithValue(
                "$normalized", Normalize(user.Username));
            userCommand.Parameters.AddWithValue("$display", user.DisplayName);
            userCommand.Parameters.AddWithValue("$verifier", protectedVerifier);
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

    public async Task<bool> HasValidSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ValidUntil FROM PosIdentityState WHERE Singleton=1;";
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is string text &&
               DateTimeOffset.Parse(text) > timeProvider.GetUtcNow();
    }

    public async Task<PosLocalUserSession> LoginAsync(
        PosLocalLoginRequest request,
        Guid expectedUserId,
        DateTimeOffset leaseExpiresAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrEmpty(request.Password))
            throw new PosLocalLoginException(
                "InvalidCredentials", "Usuario y contraseña son obligatorios.");
        if (!await HasValidSnapshotAsync(cancellationToken))
            throw new PosLocalLoginException(
                "IdentityUnavailable",
                "La información de acceso local aún no está lista o venció. Conecta la caja con Auraly.");

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
        if (user.UserId != expectedUserId)
            throw new PosLocalLoginException(
                "OfflineLeaseUserMismatch",
                "La identidad local no coincide con la concesion offline activa.");
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

        await using (var close = connection.CreateCommand())
        {
            close.Transaction = (SqliteTransaction)transaction;
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
        var expiresAt = new[] { now.Add(SessionDuration), leaseExpiresAt }.Min();
        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO PosLocalUserSessions(
                    SessionId,UserId,TokenHash,StartedAt,ExpiresAt)
                VALUES($session,$user,$hash,$now,$expires);
                """;
            insert.Parameters.AddWithValue("$session", sessionId.ToString("D"));
            insert.Parameters.AddWithValue("$user", user.UserId.ToString("D"));
            insert.Parameters.AddWithValue("$hash", TokenHash(token));
            insert.Parameters.AddWithValue("$now", Format(now));
            insert.Parameters.AddWithValue("$expires", Format(expiresAt));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        var permissions = await ReadPermissionsAsync(
            connection, (SqliteTransaction)transaction, user.UserId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PosLocalUserSession(
            sessionId, user.UserId, user.Username, user.DisplayName,
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
            SELECT s.SessionId,u.UserId,u.Username,u.DisplayName,s.ExpiresAt
            FROM PosLocalUserSessions s
            JOIN PosOfflineUsers u ON u.UserId=s.UserId
            WHERE s.TokenHash=$hash AND s.EndedAt IS NULL AND s.ExpiresAt>$now;
            """;
        command.Parameters.AddWithValue("$hash", TokenHash(token));
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var sessionId = Guid.Parse(reader.GetString(0));
        var userId = Guid.Parse(reader.GetString(1));
        var username = reader.GetString(2);
        var displayName = reader.GetString(3);
        var expires = DateTimeOffset.Parse(reader.GetString(4));
        await reader.CloseAsync();
        var permissions = await ReadPermissionsAsync(
            connection, null, userId, cancellationToken);
        return new PosLocalUserSession(
            sessionId, userId, username, displayName, permissions, expires, null);
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
}
