using System.Data;
using Auraly.Application.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlRestaurantAttentionStore(SqlServerConnectionFactory connections)
    : IRestaurantAttentionStore
{
    private const string EligibleWaiter = """
        FROM dbo.AppUsers [user]
        JOIN parties.Waiters waiter
          ON waiter.TenantId=[user].TenantId
         AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
        WHERE [user].TenantId=@TenantId AND [user].IsActive=1
          AND EXISTS (
            SELECT 1 FROM dbo.UserRoles assignment
            JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
              AND role.TenantId=@TenantId AND role.IsActive=1
            JOIN dbo.RolePermissions grantRow ON grantRow.RoleId=role.RoleId
            JOIN dbo.Permissions permission ON permission.PermissionId=grantRow.PermissionId
            WHERE assignment.UserId=[user].UserId
              AND (assignment.BusinessId IS NULL OR assignment.BusinessId=@BusinessId)
              AND permission.Resource=N'restaurant.attend')
        """;

    public async Task<RestaurantWaiterPage> WaitersAsync(
        RestaurantAttentionRequester requester, string? search, int page,
        int pageSize, CancellationToken ct)
    {
        var sql = $"""
            SELECT RestaurantRequireWaiterCredential,RestaurantRestrictTableToWaiter
            FROM dbo.Businesses
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND IsActive=1 AND PointOfSaleMode=N'RestaurantBar';

            SELECT COUNT(*) {EligibleWaiter}
              AND (@Search=N'' OR [user].FirstName LIKE N'%'+@Search+N'%'
                OR [user].LastName LIKE N'%'+@Search+N'%'
                OR waiter.ShortName LIKE N'%'+@Search+N'%');

            SELECT [user].UserId,LEFT(CONCAT([user].FirstName,N' ',[user].LastName),201),waiter.ShortName
            {EligibleWaiter}
              AND (@Search=N'' OR [user].FirstName LIKE N'%'+@Search+N'%'
                OR [user].LastName LIKE N'%'+@Search+N'%'
                OR waiter.ShortName LIKE N'%'+@Search+N'%')
            ORDER BY [user].FirstName,[user].LastName,[user].UserId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        Scope(command, requester);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 100).Value = search ?? "";
        command.Parameters.AddWithValue("@Skip", checked((page - 1) * pageSize));
        command.Parameters.AddWithValue("@Take", pageSize);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new RestaurantAccountForbiddenException(
                "La sede no está disponible para atención de mesas.");
        var require = reader.GetBoolean(0);
        var restrict = reader.GetBoolean(1);
        await reader.NextResultAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<RestaurantWaiterOption>(pageSize);
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        return new(require, restrict, items, page, pageSize, count);
    }

    public async Task<RestaurantAttentionCandidate?> CandidateAsync(
        RestaurantAttentionRequester requester, Guid tableId,
        Guid waiterUserId, CancellationToken ct)
    {
        const string sql = """
            SELECT [user].UserId,[table].TableId,
                   business.RestaurantRequireWaiterCredential,
                   business.RestaurantRestrictTableToWaiter,
                   account.ResponsibleUserId,[user].PasswordHash,
                   credential.SecretSalt,credential.SecretHash,
                   credential.SecretIterations,
                   supervisor.SecretSalt,supervisor.SecretHash,
                   supervisor.SecretIterations,
                   COALESCE(attempt.FailedCount,0),attempt.LockedUntil
            FROM dbo.Businesses business
            JOIN sales.RestaurantTables [table]
              ON [table].BusinessId=business.BusinessId
             AND [table].TenantId=business.TenantId
             AND [table].TableId=@TableId AND [table].IsActive=1
            JOIN dbo.AppUsers [user]
              ON [user].TenantId=business.TenantId
             AND [user].UserId=@WaiterUserId AND [user].IsActive=1
            JOIN parties.Waiters waiter
              ON waiter.TenantId=[user].TenantId
             AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
            LEFT JOIN sales.RestaurantAccounts account
              ON account.BusinessId=business.BusinessId
             AND account.TableId=[table].TableId
             AND account.ReleasedAt IS NULL
            LEFT JOIN [authorization].RestaurantWaiterCredentials credential
              ON credential.UserId=[user].UserId
            LEFT JOIN dbo.SupervisorCredentials supervisor
              ON supervisor.UserId=[user].UserId
             AND supervisor.IsActive=1 AND supervisor.IsOneTime=0
             AND (supervisor.ValidUntil IS NULL OR supervisor.ValidUntil>SYSUTCDATETIME())
            LEFT JOIN [authorization].RestaurantCredentialAttempts attempt
              ON attempt.UserId=[user].UserId
            WHERE business.TenantId=@TenantId
              AND business.BusinessId=@BusinessId
              AND business.IsActive=1
              AND business.PointOfSaleMode=N'RestaurantBar'
              AND EXISTS (
                SELECT 1 FROM dbo.UserRoles assignment
                JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
                  AND role.TenantId=business.TenantId AND role.IsActive=1
                JOIN dbo.RolePermissions grantRow ON grantRow.RoleId=role.RoleId
                JOIN dbo.Permissions permission ON permission.PermissionId=grantRow.PermissionId
                WHERE assignment.UserId=[user].UserId
                  AND (assignment.BusinessId IS NULL OR assignment.BusinessId=business.BusinessId)
                  AND permission.Resource=N'restaurant.attend');
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        Scope(command, requester);
        command.Parameters.AddWithValue("@TableId", tableId);
        command.Parameters.AddWithValue("@WaiterUserId", waiterUserId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(reader.GetGuid(0), reader.GetGuid(1), reader.GetBoolean(2),
            reader.GetBoolean(3), reader.IsDBNull(4) ? null : reader.GetGuid(4),
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : (byte[])reader[6],
            reader.IsDBNull(7) ? null : (byte[])reader[7],
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : (byte[])reader[9],
            reader.IsDBNull(10) ? null : (byte[])reader[10],
            reader.IsDBNull(11) ? null : reader.GetInt32(11),
            reader.GetInt32(12),
            reader.IsDBNull(13) ? null : reader.GetDateTimeOffset(13));
    }

    public async Task RecordFailedCredentialAsync(Guid tenantId,
        Guid waiterUserId, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            UPDATE [authorization].RestaurantCredentialAttempts WITH(UPDLOCK,HOLDLOCK)
            SET FailedCount=CASE WHEN LockedUntil<=@Now THEN 1 ELSE FailedCount+1 END,
                LockedUntil=CASE WHEN LockedUntil>@Now THEN LockedUntil
                  WHEN LockedUntil<=@Now THEN NULL
                  WHEN FailedCount+1>=5 THEN DATEADD(MINUTE,15,@Now)
                  ELSE NULL END,
                UpdatedAt=@Now
            WHERE UserId=@UserId AND TenantId=@TenantId;
            IF @@ROWCOUNT=0
              INSERT [authorization].RestaurantCredentialAttempts
                (UserId,TenantId,FailedCount,UpdatedAt)
              VALUES(@UserId,@TenantId,1,@Now);
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@UserId", waiterUserId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ClearFailedCredentialAsync(Guid tenantId,
        Guid waiterUserId, DateTimeOffset now, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE [authorization].RestaurantCredentialAttempts
            SET FailedCount=0,LockedUntil=NULL,UpdatedAt=@Now
            WHERE UserId=@UserId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@UserId", waiterUserId);
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveAsync(RestaurantAttentionTicket ticket,
        CancellationToken ct)
    {
        const string sql = """
            INSERT sales.RestaurantAttentionSessions
              (AttentionSessionId,TenantId,BusinessId,TableId,
               ConnectedUserId,AuthenticationSessionId,WaiterUserId,
               TokenHash,Method,IssuedAt)
            SELECT @Id,@TenantId,@BusinessId,[table].TableId,
                   session.UserId,session.AuthenticationSessionId,
                   [user].UserId,@Hash,@Method,@Now
            FROM sales.RestaurantTables [table]
            JOIN dbo.Businesses business
              ON business.BusinessId=[table].BusinessId
             AND business.TenantId=[table].TenantId
             AND business.IsActive=1
             AND business.PointOfSaleMode=N'RestaurantBar'
            JOIN dbo.AuthenticationSessions session
              ON session.AuthenticationSessionId=@AuthenticationSessionId
             AND session.UserId=@ConnectedUserId
             AND session.TenantId=@TenantId
             AND session.Status=N'Active' AND session.ExpiresAt>@Now
            JOIN dbo.AppUsers [user]
              ON [user].UserId=@WaiterUserId
             AND [user].TenantId=@TenantId AND [user].IsActive=1
            JOIN parties.Waiters waiter
              ON waiter.TenantId=[user].TenantId
             AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
            WHERE [table].TableId=@TableId AND [table].BusinessId=@BusinessId
              AND [table].TenantId=@TenantId AND [table].IsActive=1
              AND (business.RestaurantRequireWaiterCredential=0
                OR @Method<>N'Selection')
              AND EXISTS (
                SELECT 1 FROM dbo.UserRoles assignment
                JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
                  AND role.TenantId=@TenantId AND role.IsActive=1
                JOIN dbo.RolePermissions grantRow ON grantRow.RoleId=role.RoleId
                JOIN dbo.Permissions permission ON permission.PermissionId=grantRow.PermissionId
                WHERE assignment.UserId=[user].UserId
                  AND (assignment.BusinessId IS NULL OR assignment.BusinessId=@BusinessId)
                  AND permission.Resource=N'restaurant.attend');
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand(sql, connection, transaction);
        Scope(command, ticket.TenantId, ticket.BusinessId);
        command.Parameters.AddWithValue("@Id", ticket.AttentionSessionId);
        command.Parameters.AddWithValue("@TableId", ticket.TableId);
        command.Parameters.AddWithValue("@ConnectedUserId", ticket.ConnectedUserId);
        command.Parameters.AddWithValue("@AuthenticationSessionId", ticket.AuthenticationSessionId);
        command.Parameters.AddWithValue("@WaiterUserId", ticket.WaiterUserId);
        command.Parameters.Add("@Hash", SqlDbType.VarBinary, 32).Value = ticket.TokenHash;
        command.Parameters.Add("@Method", SqlDbType.NVarChar, 16).Value = ticket.Method;
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = ticket.IssuedAt;
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new RestaurantAccountForbiddenException(
                "La sede, mesa o mesero cambió antes de iniciar la atención.");
        if (ticket.ExpectedResponsibleUserId is { } expectedResponsible)
        {
            await using var transfer = new SqlCommand("""
                UPDATE account
                SET ResponsibleUserId=@WaiterUserId,Version=Version+1,
                    UpdatedAt=@Now
                FROM sales.RestaurantAccounts account
                WHERE account.TenantId=@TenantId
                  AND account.BusinessId=@BusinessId
                  AND account.TableId=@TableId
                  AND account.ResponsibleUserId=@ExpectedResponsibleUserId
                  AND account.Status=N'Open';
                """, connection, transaction);
            Scope(transfer, ticket.TenantId, ticket.BusinessId);
            transfer.Parameters.AddWithValue("@TableId", ticket.TableId);
            transfer.Parameters.AddWithValue("@WaiterUserId", ticket.WaiterUserId);
            transfer.Parameters.AddWithValue("@ExpectedResponsibleUserId", expectedResponsible);
            transfer.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = ticket.IssuedAt;
            if (await transfer.ExecuteNonQueryAsync(ct) != 1)
                throw new RestaurantAccountConflictException(
                    "La mesa cambió de responsable. Vuelve a abrirla antes de continuar.");

            await using var audit = new SqlCommand("""
                INSERT dbo.AuditLogs
                  (AuditLogId,UserId,TenantId,BusinessId,Action,
                   EntityType,EntityId,NewValues,Timestamp)
                VALUES(NEWID(),@ConnectedUserId,@TenantId,@BusinessId,
                       N'RestaurantWaiterChanged',N'RestaurantTable',
                       CONVERT(nvarchar(100),@TableId),
                       CONCAT(CONVERT(nvarchar(36),@ExpectedResponsibleUserId),
                              N' -> ',CONVERT(nvarchar(36),@WaiterUserId)),@Now);
                """, connection, transaction);
            Scope(audit, ticket.TenantId, ticket.BusinessId);
            audit.Parameters.AddWithValue("@ConnectedUserId", ticket.ConnectedUserId);
            audit.Parameters.AddWithValue("@TableId", ticket.TableId);
            audit.Parameters.AddWithValue("@WaiterUserId", ticket.WaiterUserId);
            audit.Parameters.AddWithValue("@ExpectedResponsibleUserId", expectedResponsible);
            audit.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = ticket.IssuedAt;
            await audit.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    public async Task<RestaurantAttentionGrant?> ResolveAsync(
        RestaurantAttentionRequester requester, Guid attentionSessionId,
        byte[] tokenHash, DateTimeOffset now, CancellationToken ct)
    {
        const string sql = """
            SELECT attention.TableId,attention.WaiterUserId,
                   attention.ConnectedUserId,attention.Method,
                   business.RestaurantRestrictTableToWaiter,
                   account.ResponsibleUserId
            FROM sales.RestaurantAttentionSessions attention
            JOIN dbo.Businesses business
              ON business.BusinessId=attention.BusinessId
             AND business.TenantId=attention.TenantId
             AND business.IsActive=1
             AND business.PointOfSaleMode=N'RestaurantBar'
            JOIN sales.RestaurantTables [table]
              ON [table].BusinessId=attention.BusinessId
             AND [table].TableId=attention.TableId AND [table].IsActive=1
            JOIN dbo.AuthenticationSessions session
              ON session.AuthenticationSessionId=attention.AuthenticationSessionId
             AND session.UserId=attention.ConnectedUserId
             AND session.Status=N'Active' AND session.ExpiresAt>@Now
            JOIN dbo.AppUsers [user]
              ON [user].UserId=attention.WaiterUserId
             AND [user].TenantId=attention.TenantId AND [user].IsActive=1
            JOIN parties.Waiters waiter
              ON waiter.TenantId=[user].TenantId
             AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
            LEFT JOIN sales.RestaurantAccounts account
              ON account.BusinessId=attention.BusinessId
             AND account.TableId=attention.TableId
             AND account.ReleasedAt IS NULL
            WHERE attention.AttentionSessionId=@Id
              AND attention.TokenHash=@Hash
              AND attention.TenantId=@TenantId
              AND attention.BusinessId=@BusinessId
              AND attention.ConnectedUserId=@ConnectedUserId
              AND attention.AuthenticationSessionId=@AuthenticationSessionId
              AND attention.EndedAt IS NULL
              AND (business.RestaurantRequireWaiterCredential=0
                OR attention.Method<>N'Selection')
              AND EXISTS (
                SELECT 1 FROM dbo.UserRoles assignment
                JOIN dbo.AppRoles role ON role.RoleId=assignment.RoleId
                  AND role.TenantId=@TenantId AND role.IsActive=1
                JOIN dbo.RolePermissions grantRow ON grantRow.RoleId=role.RoleId
                JOIN dbo.Permissions permission ON permission.PermissionId=grantRow.PermissionId
                WHERE assignment.UserId=[user].UserId
                  AND (assignment.BusinessId IS NULL OR assignment.BusinessId=@BusinessId)
                  AND permission.Resource=N'restaurant.attend');
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        Scope(command, requester);
        command.Parameters.AddWithValue("@Id", attentionSessionId);
        command.Parameters.Add("@Hash", SqlDbType.VarBinary, 32).Value = tokenHash;
        command.Parameters.AddWithValue("@ConnectedUserId", requester.UserId);
        command.Parameters.AddWithValue("@AuthenticationSessionId", requester.AuthenticationSessionId);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
                reader.GetString(3), reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetGuid(5))
            : null;
    }

    public async Task EndAsync(RestaurantAttentionRequester requester,
        Guid attentionSessionId, byte[] tokenHash, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            UPDATE sales.RestaurantAttentionSessions
            SET EndedAt=COALESCE(EndedAt,@Now)
            WHERE AttentionSessionId=@Id AND TokenHash=@Hash
              AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND ConnectedUserId=@ConnectedUserId
              AND AuthenticationSessionId=@AuthenticationSessionId;
            """, connection);
        Scope(command, requester);
        command.Parameters.AddWithValue("@Id", attentionSessionId);
        command.Parameters.Add("@Hash", SqlDbType.VarBinary, 32).Value = tokenHash;
        command.Parameters.AddWithValue("@ConnectedUserId", requester.UserId);
        command.Parameters.AddWithValue("@AuthenticationSessionId", requester.AuthenticationSessionId);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task ConfigureSecondaryAsync(
        RestaurantAttentionRequester requester, Guid waiterUserId,
        byte[] salt, byte[] hash, int iterations, DateTimeOffset now,
        CancellationToken ct)
    {
        const string sql = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.AppUsers [user]
              JOIN parties.Waiters waiter ON waiter.TenantId=[user].TenantId
                AND waiter.PartyId=[user].PartyId AND waiter.IsActive=1
              WHERE [user].UserId=@WaiterUserId AND [user].TenantId=@TenantId
                AND [user].IsActive=1)
              THROW 51081,'El mesero no tiene un usuario activo.',1;
            UPDATE [authorization].RestaurantWaiterCredentials
            SET SecretSalt=@Salt,SecretHash=@Hash,
                SecretIterations=@Iterations,UpdatedAt=@Now,
                UpdatedByUserId=@ActorUserId
            WHERE UserId=@WaiterUserId AND TenantId=@TenantId;
            IF @@ROWCOUNT=0
              INSERT [authorization].RestaurantWaiterCredentials
                (UserId,TenantId,SecretSalt,SecretHash,SecretIterations,
                 UpdatedAt,UpdatedByUserId)
              VALUES(@WaiterUserId,@TenantId,@Salt,@Hash,@Iterations,
                     @Now,@ActorUserId);
            UPDATE sales.RestaurantAttentionSessions
            SET EndedAt=@Now
            WHERE WaiterUserId=@WaiterUserId AND TenantId=@TenantId
              AND Method=N'Secondary' AND EndedAt IS NULL;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand(sql, connection, transaction);
        Scope(command, requester);
        command.Parameters.AddWithValue("@WaiterUserId", waiterUserId);
        command.Parameters.AddWithValue("@ActorUserId", requester.UserId);
        command.Parameters.Add("@Salt", SqlDbType.VarBinary, 32).Value = salt;
        command.Parameters.Add("@Hash", SqlDbType.VarBinary, 32).Value = hash;
        command.Parameters.AddWithValue("@Iterations", iterations);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51081)
        { throw new RestaurantAccountValidationException("El mesero no tiene un usuario activo."); }
        await transaction.CommitAsync(ct);
    }

    public async Task<bool> HasSecondaryAsync(
        RestaurantAttentionRequester requester, Guid waiterUserId,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT 1 FROM [authorization].RestaurantWaiterCredentials
            WHERE UserId=@WaiterUserId AND TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@WaiterUserId", waiterUserId);
        command.Parameters.AddWithValue("@TenantId", requester.TenantId);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    public async Task RevokeSecondaryAsync(
        RestaurantAttentionRequester requester, Guid waiterUserId,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            DELETE FROM [authorization].RestaurantWaiterCredentials
            WHERE UserId=@WaiterUserId AND TenantId=@TenantId;
            UPDATE sales.RestaurantAttentionSessions
            SET EndedAt=SYSUTCDATETIME()
            WHERE WaiterUserId=@WaiterUserId AND TenantId=@TenantId
              AND Method=N'Secondary' AND EndedAt IS NULL;
            """, connection);
        command.Parameters.AddWithValue("@WaiterUserId", waiterUserId);
        command.Parameters.AddWithValue("@TenantId", requester.TenantId);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static void Scope(SqlCommand command, RestaurantAttentionRequester requester) =>
        Scope(command, requester.TenantId, requester.BusinessId);

    private static void Scope(SqlCommand command, Guid tenantId, Guid businessId)
    {
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
    }
}
