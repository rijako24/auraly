using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Authorization;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Host;

public sealed record PosLocalSensitiveAuthorization(
    Guid AuthorizationId,
    Guid RequestedByUserId,
    Guid AuthorizedByUserId,
    string PermissionResource,
    string Method);

public sealed partial class PosLocalIdentityStore
{
    public async Task<PosLocalSensitiveAuthorization> AuthorizeSensitiveAsync(
        PosLocalUserSession requester,
        string permissionResource,
        Guid draftId,
        Guid? lineId,
        string? supervisorSecret,
        CancellationToken cancellationToken = default)
    {
        ValidateSensitivePermission(permissionResource);
        var direct = requester.Permissions.Contains(permissionResource);
        var authorizerId = requester.UserId;
        var method = "DirectPermission";
        if (!direct)
        {
            if (string.IsNullOrWhiteSpace(supervisorSecret))
                throw new PosLocalApprovalException(
                    "ApprovalRequired",
                    "Esta acción requiere la credencial secundaria de un supervisor.");
            authorizerId = await ResolveSupervisorAsync(
                requester.UserId,
                permissionResource,
                supervisorSecret,
                cancellationToken)
                ?? throw new PosLocalApprovalException(
                    "InvalidSupervisorCredential",
                    "La credencial no corresponde a un supervisor autorizado en este dispositivo.");
            method = "OfflineSupervisorCredential";
        }

        var authorization = new PosLocalSensitiveAuthorization(
            ids.NewId(), requester.UserId, authorizerId, permissionResource, method);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO PosLocalApprovalAudits(
                AuthorizationId,RequestedByUserId,AuthorizedByUserId,
                PermissionResource,DraftId,LineId,AuthorizationMethod,
                Status,AuthorizedAt)
            VALUES($id,$requester,$authorizer,$permission,$draft,$line,$method,
                'Authorized',$now);
            """;
        command.Parameters.AddWithValue("$id", authorization.AuthorizationId.ToString("D"));
        command.Parameters.AddWithValue("$requester", requester.UserId.ToString("D"));
        command.Parameters.AddWithValue("$authorizer", authorizerId.ToString("D"));
        command.Parameters.AddWithValue("$permission", permissionResource);
        command.Parameters.AddWithValue("$draft", draftId.ToString("D"));
        command.Parameters.AddWithValue("$line", lineId?.ToString("D") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$method", method);
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        await command.ExecuteNonQueryAsync(cancellationToken);
        return authorization;
    }

    public async Task CompleteSensitiveAsync(
        PosLocalSensitiveAuthorization authorization,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE PosLocalApprovalAudits
            SET Status='Completed',CompletedAt=$now
            WHERE AuthorizationId=$id AND Status='Authorized';
            """;
        command.Parameters.AddWithValue("$id", authorization.AuthorizationId.ToString("D"));
        command.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException("The local sensitive authorization is not active.");
    }

    private sealed record ResolvedSupervisor(
        Guid UserId,
        bool IsOneTime,
        DateTimeOffset ChangedAt);

    private async Task<Guid?> ResolveSupervisorAsync(
        Guid requesterId,
        string permissionResource,
        string secret,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT u.UserId,u.ProtectedSupervisorCredential
            FROM PosOfflineUsers u
            WHERE u.UserId<>$requester
              AND u.ProtectedSupervisorCredential IS NOT NULL
              AND EXISTS(SELECT 1 FROM PosOfflineUserPermissions p
                         WHERE p.UserId=u.UserId AND p.PermissionCode=$permission)
              AND EXISTS(SELECT 1 FROM PosOfflineUserPermissions p
                         WHERE p.UserId=u.UserId AND p.PermissionCode=$authorize)
            ORDER BY u.UserId;
            """;
        command.Parameters.AddWithValue("$requester", requesterId.ToString("D"));
        command.Parameters.AddWithValue("$permission", permissionResource);
        command.Parameters.AddWithValue(
            "$authorize", CommercePermissionCodes.PosApprovalsAuthorize);
        ResolvedSupervisor? matched = null;
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var verifier = JsonSerializer.Deserialize<PosOfflineSupervisorCredentialVerifier>(
                    PosEdgeProtectedSecret.UnprotectIdentityVerifier(
                        keyDirectory, reader.GetString(1)))
                    ?? throw new InvalidDataException("The local supervisor verifier is invalid.");
                var derived = Rfc2898DeriveBytes.Pbkdf2(
                    secretBytes, verifier.Salt, verifier.Iterations,
                    HashAlgorithmName.SHA256, verifier.Hash.Length);
                if (CryptographicOperations.FixedTimeEquals(derived, verifier.Hash))
                    matched = new(
                        Guid.Parse(reader.GetString(0)),
                        verifier.IsOneTime,
                        verifier.ChangedAt);
            }
        }
        if (matched?.IsOneTime == true)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using var consume = connection.CreateCommand();
            consume.Transaction = (SqliteTransaction)transaction;
            consume.CommandText = """
                INSERT OR IGNORE INTO PosConsumedOneTimeSupervisorCredentials(
                    UserId,ChangedAt,ConsumedAt)
                VALUES($id,$changedAt,$now);
                UPDATE PosOfflineUsers
                SET ProtectedSupervisorCredential=NULL
                WHERE UserId=$id AND ProtectedSupervisorCredential IS NOT NULL;
                """;
            consume.Parameters.AddWithValue("$id", matched.UserId.ToString("D"));
            consume.Parameters.AddWithValue("$changedAt", Format(matched.ChangedAt));
            consume.Parameters.AddWithValue("$now", Format(timeProvider.GetUtcNow()));
            await consume.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return matched?.UserId;
    }

    private static void ValidateSensitivePermission(string permissionResource)
    {
        if (permissionResource is not (
            CommercePermissionCodes.SalesDiscount or
            CommercePermissionCodes.SalesRemoveLine or
            CommercePermissionCodes.SalesRestartDraft or
            Auraly.Contracts.WorkSessions.WorkSessionPermissionCodes.Close))
            throw new PosLocalApprovalException(
                "UnsupportedPermission", "La acción no admite autorización delegada.");
    }
}

public sealed class PosLocalApprovalException(string code, string message)
    : Exception(message)
{
    public string Code { get; } = code;
}
