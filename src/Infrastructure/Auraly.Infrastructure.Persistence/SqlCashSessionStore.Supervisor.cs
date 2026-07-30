using System.Data;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Cash;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Cash;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCashSessionStore
{
    private const string SupervisorBarcodePrefix = "AUR-SUP-";
    private static readonly TimeSpan SupervisorGrantLifetime = TimeSpan.FromSeconds(90);

    public async Task<SupervisorAuthorizationGrant> AuthorizeHandoffAsync(
        CashUserIdentity actor,
        Guid registerId,
        SupervisorAuthorizationRequest request,
        CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var register = await ValidateRegisterAsync(
            connection, transaction, actor, registerId, null, ct);
        _ = await RequireCurrentForActorAsync(connection, transaction, actor, registerId, ct);

        var supervisor = request.Credential.StartsWith(
            SupervisorBarcodePrefix, StringComparison.Ordinal)
            ? await AuthenticateBarcodeAsync(
                connection, transaction, actor.TenantId, request.Credential.Trim(), ct)
            : await AuthenticatePasswordAsync(
                connection, transaction, actor.TenantId, request.Username,
                request.Credential, ct);

        if (!await HasPermissionAsync(
                connection, transaction, actor.TenantId, register.BusinessId,
                supervisor.UserId, CommercePermissionCodes.CashHandoffApprove, ct))
        {
            throw new CashForbiddenException(
                "El usuario autenticado no puede autorizar entregas de caja en este negocio.");
        }

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(SupervisorGrantLifetime);
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SupervisorAuthorizationGrants
              (AuthorizationGrantId,BusinessId,RegisterId,RequestedByUserId,
               AuthorizedByUserId,PermissionCode,TokenHash,CreatedAt,ExpiresAt)
            VALUES
              (@GrantId,@BusinessId,@RegisterId,@RequestedBy,@AuthorizedBy,
               @PermissionCode,@TokenHash,@Now,@ExpiresAt);
            """, ct,
            P("@GrantId", _ids.NewId()), P("@BusinessId", register.BusinessId),
            P("@RegisterId", registerId), P("@RequestedBy", actor.UserId),
            P("@AuthorizedBy", supervisor.UserId),
            P("@PermissionCode", CommercePermissionCodes.CashHandoffApprove),
            Binary("@TokenHash", tokenHash), P("@Now", now), P("@ExpiresAt", expiresAt));
        await transaction.CommitAsync(ct);
        return new SupervisorAuthorizationGrant(
            token, supervisor.UserId, supervisor.UserName,
            CommercePermissionCodes.CashHandoffApprove, expiresAt);
    }

    public async Task<ProvisionSupervisorCredentialResult> ProvisionSupervisorCredentialAsync(
        CashUserIdentity actor,
        ProvisionSupervisorCredentialRequest request,
        CancellationToken ct)
    {
        await using var connection = _connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var user = await ActiveUserAsync(
            connection, transaction, actor.TenantId, request.UserId, ct);
        var now = _timeProvider.GetUtcNow();
        var credentialId = _ids.NewId();
        var printableCredential =
            $"{SupervisorBarcodePrefix}{credentialId:N}.{Base64Url(RandomNumberGenerator.GetBytes(32))}";
        var credential = PosDeviceCredentialHasher.Create(printableCredential);

        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SupervisorCredentials
            SET IsActive=0,RevokedByUserId=@ActorId,RevokedAt=@Now
            WHERE UserId=@UserId AND IsActive=1;

            INSERT dbo.SupervisorCredentials
              (CredentialId,UserId,SecretSalt,SecretHash,SecretIterations,IsActive,
               CreatedByUserId,CreatedAt)
            VALUES
              (@CredentialId,@UserId,@Salt,@Hash,@Iterations,1,@ActorId,@Now);
            """, ct,
            P("@ActorId", actor.UserId), P("@Now", now), P("@UserId", user.UserId),
            P("@CredentialId", credentialId),
            new SqlParameter("@Salt", SqlDbType.VarBinary, 32) { Value = credential.Salt },
            new SqlParameter("@Hash", SqlDbType.VarBinary, 32) { Value = credential.Hash },
            P("@Iterations", credential.Iterations));
        await transaction.CommitAsync(ct);
        return new ProvisionSupervisorCredentialResult(
            credentialId, user.UserId, user.UserName, printableCredential, now);
    }

    private async Task<Guid> ConsumeSupervisorAuthorizationAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        CashUserIdentity actor,
        CashSessionView session,
        string token,
        string permission,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new CashForbiddenException("La autorización del supervisor es obligatoria.");
        var now = _timeProvider.GetUtcNow();
        var tokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        await using var command = new SqlCommand("""
            UPDATE dbo.SupervisorAuthorizationGrants WITH (UPDLOCK,HOLDLOCK)
            SET ConsumedAt=@Now,ConsumedByUserId=@UserId
            OUTPUT inserted.AuthorizedByUserId
            WHERE BusinessId=@BusinessId
              AND RegisterId=@RegisterId
              AND RequestedByUserId=@UserId
              AND PermissionCode=@PermissionCode
              AND TokenHash=@TokenHash
              AND ConsumedAt IS NULL
              AND ExpiresAt>=@Now;
            """, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@Now", now), P("@UserId", actor.UserId),
            P("@BusinessId", session.BusinessId), P("@RegisterId", session.RegisterId),
            P("@PermissionCode", permission), Binary("@TokenHash", tokenHash)
        ]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is Guid authorizedBy
            ? authorizedBy
            : throw new CashForbiddenException(
                "La autorización del supervisor venció, ya fue utilizada o no corresponde a esta caja.");
    }

    private static async Task<SupervisorUser> AuthenticatePasswordAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        string? username,
        string password,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
            throw new CashValidationException(
                "El usuario del supervisor es obligatorio cuando se utiliza contraseña.");
        await using var command = new SqlCommand("""
            SELECT UserId,CONCAT(FirstName,N' ',LastName),PasswordHash
            FROM dbo.AppUsers
            WHERE TenantId=@TenantId AND NormalizedUsername=@Username
              AND IsActive=1 AND (LockoutEnd IS NULL OR LockoutEnd<=SYSUTCDATETIME());
            """, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@TenantId", tenantId),
            P("@Username", username.Trim().ToUpperInvariant())
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct) || reader.IsDBNull(2))
            throw new CashForbiddenException("La credencial del supervisor no es válida.");
        var user = new SupervisorUser(reader.GetGuid(0), reader.GetString(1));
        var hash = reader.GetString(2);
        await reader.DisposeAsync();
        bool valid;
        try
        {
            valid = BCrypt.Net.BCrypt.Verify(password, hash);
        }
        catch (ArgumentException)
        {
            valid = false;
        }
        if (!valid)
            throw new CashForbiddenException("La credencial del supervisor no es válida.");
        return user;
    }

    private static async Task<SupervisorUser> AuthenticateBarcodeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        string printableCredential,
        CancellationToken ct)
    {
        var separator = printableCredential.IndexOf('.', SupervisorBarcodePrefix.Length);
        if (separator < 0 ||
            !Guid.TryParseExact(
                printableCredential[SupervisorBarcodePrefix.Length..separator],
                "N",
                out var credentialId))
        {
            throw new CashForbiddenException("La credencial del supervisor no es válida.");
        }

        await using var command = new SqlCommand("""
            SELECT c.UserId,CONCAT(u.FirstName,N' ',u.LastName),
                   c.SecretSalt,c.SecretHash,c.SecretIterations
            FROM dbo.SupervisorCredentials c
            INNER JOIN dbo.AppUsers u ON u.UserId=c.UserId
            WHERE c.CredentialId=@CredentialId AND c.IsActive=1
              AND u.TenantId=@TenantId AND u.IsActive=1
              AND (u.LockoutEnd IS NULL OR u.LockoutEnd<=SYSUTCDATETIME());
            """, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@CredentialId", credentialId),
            P("@TenantId", tenantId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new CashForbiddenException("La credencial del supervisor no es válida.");
        var user = new SupervisorUser(reader.GetGuid(0), reader.GetString(1));
        var salt = (byte[])reader[2];
        var hash = (byte[])reader[3];
        var iterations = reader.GetInt32(4);
        await reader.DisposeAsync();
        if (!PosDeviceCredentialHasher.Verify(
                printableCredential, salt, hash, iterations))
        {
            throw new CashForbiddenException("La credencial del supervisor no es válida.");
        }
        return user;
    }

    private static async Task<bool> HasPermissionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid businessId,
        Guid userId,
        string permission,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(1)
            FROM dbo.AppUsers u
            INNER JOIN dbo.UserRoles ur ON ur.UserId=u.UserId
            INNER JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1
            INNER JOIN dbo.RolePermissions rp ON rp.RoleId=r.RoleId
            INNER JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
            WHERE u.UserId=@UserId AND u.TenantId=@TenantId AND u.IsActive=1
              AND (r.TenantId IS NULL OR r.TenantId=@TenantId)
              AND (ur.BusinessId IS NULL OR ur.BusinessId=@BusinessId)
              AND p.Resource=@Permission;
            """, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@UserId", userId), P("@TenantId", tenantId),
            P("@BusinessId", businessId), P("@Permission", permission)
        ]);
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<SupervisorUser> ActiveUserAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid tenantId,
        Guid userId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT UserId,CONCAT(FirstName,N' ',LastName)
            FROM dbo.AppUsers
            WHERE TenantId=@TenantId AND UserId=@UserId AND IsActive=1;
            """, connection, transaction);
        command.Parameters.AddRange(
        [
            P("@TenantId", tenantId),
            P("@UserId", userId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new SupervisorUser(reader.GetGuid(0), reader.GetString(1))
            : throw new CashValidationException("El supervisor no existe o está inactivo.");
    }

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record SupervisorUser(Guid UserId, string UserName);
}
