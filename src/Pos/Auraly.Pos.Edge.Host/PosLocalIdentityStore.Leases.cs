using System.Text.Json;
using Auraly.Contracts.Authentication;
using Auraly.Contracts.Authorization;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Host;

public sealed partial class PosLocalIdentityStore
{
    public async Task ApplyLeaseUserAsync(
        OfflineAuthenticationLeaseUser user,
        CancellationToken cancellationToken = default)
    {
        var verifier = new PosOfflinePasswordVerifier(
            user.PasswordSalt,
            user.PasswordHash,
            user.PasswordIterations,
            user.PasswordChangedAt);
        var protectedVerifier = PosEdgeProtectedSecret.ProtectIdentityVerifier(
            keyDirectory,
            JsonSerializer.Serialize(verifier));

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            await connection.BeginTransactionAsync(cancellationToken);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO PosOfflineUsers(
                    UserId,Username,NormalizedUsername,DisplayName,
                    ProtectedPasswordVerifier,FailedCount,LockedUntil)
                VALUES($id,$username,$normalized,$display,$verifier,0,NULL)
                ON CONFLICT(UserId) DO UPDATE SET
                    Username=excluded.Username,
                    NormalizedUsername=excluded.NormalizedUsername,
                    DisplayName=excluded.DisplayName,
                    ProtectedPasswordVerifier=excluded.ProtectedPasswordVerifier,
                    FailedCount=0,LockedUntil=NULL;
                DELETE FROM PosOfflineUserPermissions WHERE UserId=$id;
                """;
            command.Parameters.AddWithValue("$id", user.UserId.ToString("D"));
            command.Parameters.AddWithValue("$username", user.Username);
            command.Parameters.AddWithValue(
                "$normalized", user.Username.Trim().ToUpperInvariant());
            command.Parameters.AddWithValue("$display", user.DisplayName);
            command.Parameters.AddWithValue("$verifier", protectedVerifier);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var permission in user.Permissions.Distinct(StringComparer.Ordinal))
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO PosOfflineUserPermissions(UserId,PermissionCode)
                VALUES($id,$permission);
                """;
            command.Parameters.AddWithValue("$id", user.UserId.ToString("D"));
            command.Parameters.AddWithValue("$permission", permission);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }
}
