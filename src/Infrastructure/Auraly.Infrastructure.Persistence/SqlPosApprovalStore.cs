using System.Data;
using Auraly.Application.Authorization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Authorization;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosApprovalStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IPosApprovalStore
{
    public async Task<PosApprovalRequestView> CreateAsync(
        PosApprovalUserIdentity user,
        CreatePosApprovalRequest request,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        if (!await ScopeMatchesAsync(connection, user.TenantId, request.BusinessId, user.UserId, cancellationToken))
            throw new PosApprovalException("Forbidden", "El negocio no pertenece al contexto autenticado.");
        var id = ids.NewId();
        var now = timeProvider.GetUtcNow();
        const string sql = """
            INSERT dbo.PosApprovalRequests(
              ApprovalRequestId,TenantId,BusinessId,DeviceId,WorkSessionId,DraftId,LineId,
              PermissionResource,RequestedByUserId,ContextJson,Status,RequestedAt,ExpiresAt)
            VALUES(@Id,@TenantId,@BusinessId,@DeviceId,@WorkSessionId,@DraftId,@LineId,
              @Permission,@UserId,@Context,N'Pending',@Now,@ExpiresAt);
            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'Approvals';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
            VALUES(@NotificationId,@BusinessId,N'Approvals',@Cursor,@Now);
            """;
        await using var command = new SqlCommand(sql, connection);
        Add(command,"@Id",id); Add(command,"@TenantId",user.TenantId); Add(command,"@BusinessId",request.BusinessId);
        Add(command,"@DeviceId",request.DeviceId); Add(command,"@WorkSessionId",request.WorkSessionId);
        Add(command,"@DraftId",request.DraftId); Add(command,"@LineId",request.LineId);
        Add(command,"@Permission",request.PermissionResource); Add(command,"@UserId",user.UserId);
        Add(command,"@Context",request.ContextJson); Add(command,"@Now",now); Add(command,"@ExpiresAt",expiresAt); Add(command,"@NotificationId",ids.NewId());
        await command.ExecuteNonQueryAsync(cancellationToken);
        return (await GetAsync(user,id,cancellationToken))!;
    }

    public async Task<PosApprovalRequestView?> GetAsync(
        PosApprovalUserIdentity user, Guid approvalRequestId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await ExpireAsync(connection, approvalRequestId, cancellationToken);
        return await ReadOneAsync(connection,user.TenantId,approvalRequestId,cancellationToken);
    }

    public async Task<IReadOnlyList<PosApprovalRequestView>> PendingAsync(
        PosApprovalUserIdentity user, Guid businessId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using (var expire = new SqlCommand("UPDATE dbo.PosApprovalRequests SET Status=N'Expired' WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND Status=N'Pending' AND ExpiresAt<=SYSUTCDATETIME();",connection))
        { Add(expire,"@TenantId",user.TenantId); Add(expire,"@BusinessId",businessId); await expire.ExecuteNonQueryAsync(cancellationToken); }
        const string sql = SelectSql + " WHERE a.TenantId=@TenantId AND a.BusinessId=@BusinessId AND a.Status=N'Pending' ORDER BY a.RequestedAt;";
        await using var command = new SqlCommand(sql,connection);
        Add(command,"@TenantId",user.TenantId); Add(command,"@BusinessId",businessId);
        return await ReadManyAsync(command,cancellationToken);
    }

    public async Task<PosApprovalDecisionResult> DecideAsync(
        PosApprovalUserIdentity user, Guid approvalRequestId, bool approve,
        string method, CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        const string sql="""
            UPDATE dbo.PosApprovalRequests WITH(UPDLOCK,ROWLOCK)
            SET Status=@Status,DecidedByUserId=@UserId,DecisionMethod=@Method,DecidedAt=@Now
            WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND Status=N'Pending' AND ExpiresAt>@Now;
            DECLARE @Changed INT=@@ROWCOUNT;
            IF @Changed=1
            BEGIN
              DECLARE @Cursor BIGINT;
              SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
              FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND Stream=N'Approvals';
              INSERT dbo.PosSynchronizationOutboxMessages
                (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
              VALUES(@NotificationId,@BusinessId,N'Approvals',@Cursor,@Now);
            END
            SELECT @Changed;
            """;
        await using var command=new SqlCommand(sql,connection);
        var now=timeProvider.GetUtcNow(); Add(command,"@Status",approve?PosApprovalStatus.Approved:PosApprovalStatus.Rejected);
        Add(command,"@UserId",user.UserId); Add(command,"@Method",method); Add(command,"@Now",now);
        Add(command,"@Id",approvalRequestId); Add(command,"@TenantId",user.TenantId);
        Add(command,"@BusinessId",user.BusinessId); Add(command,"@NotificationId",ids.NewId());
        if(Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken))!=1)
            throw new PosApprovalException("AlreadyDecidedOrExpired","La solicitud ya fue atendida o venciÃ³.");
        return new(approvalRequestId,approve?PosApprovalStatus.Approved:PosApprovalStatus.Rejected,user.UserId,now);
    }

    public async Task<IReadOnlyList<SupervisorCredentialVerifier>> AuthorizersAsync(
        Guid tenantId, Guid businessId, string permissionResource, CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        const string sql="""
            SELECT DISTINCT u.UserId,c.SecretSalt,c.SecretHash,c.SecretIterations
            FROM dbo.AppUsers u
            JOIN dbo.SupervisorCredentials c ON c.UserId=u.UserId AND c.IsActive=1
            JOIN dbo.UserRoles ur ON ur.UserId=u.UserId AND (ur.BusinessId IS NULL OR ur.BusinessId=@BusinessId)
            JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1 AND r.TenantId=@TenantId
            JOIN dbo.RolePermissions rp ON rp.RoleId=r.RoleId
            JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
            WHERE u.TenantId=@TenantId AND u.IsActive=1 AND p.Resource IN(@Requested,@Authorize)
            GROUP BY u.UserId,c.SecretSalt,c.SecretHash,c.SecretIterations
            HAVING COUNT(DISTINCT p.Resource)=2;
            """;
        await using var command=new SqlCommand(sql,connection); Add(command,"@TenantId",tenantId); Add(command,"@BusinessId",businessId);
        Add(command,"@Requested",permissionResource); Add(command,"@Authorize",CommercePermissionCodes.PosApprovalsAuthorize);
        var values=new List<SupervisorCredentialVerifier>(); await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) values.Add(new(reader.GetGuid(0),(byte[])reader[1],(byte[])reader[2],reader.GetInt32(3)));
        return values;
    }

    public async Task ConfigureCredentialAsync(
        PosApprovalUserIdentity user, byte[] salt, byte[] hash, int iterations,
        CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,cancellationToken);
        var now=timeProvider.GetUtcNow();
        await using var command=new SqlCommand("""
            UPDATE dbo.SupervisorCredentials
            SET IsActive=0,RevokedByUserId=@UserId,RevokedAt=@Now
            WHERE UserId=@UserId AND IsActive=1;
            INSERT dbo.SupervisorCredentials(CredentialId,UserId,SecretSalt,SecretHash,SecretIterations,IsActive,CreatedByUserId,CreatedAt)
            VALUES(@Id,@UserId,@Salt,@Hash,@Iterations,1,@UserId,@Now);
            """,connection,transaction);
        Add(command,"@Id",ids.NewId());Add(command,"@UserId",user.UserId);Add(command,"@Salt",salt);Add(command,"@Hash",hash);Add(command,"@Iterations",iterations);Add(command,"@Now",now);
        await command.ExecuteNonQueryAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReserveAsync(
        PosApprovalUserIdentity user, Guid approvalRequestId, Guid businessId,
        Guid draftId, Guid? lineId, string permissionResource, Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        const string sql="""
            UPDATE dbo.PosApprovalRequests WITH(UPDLOCK,ROWLOCK)
            SET Status=N'Reserved',ReservedAt=@Now,ConsumedByOperationId=@OperationId
            WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND DraftId=@DraftId AND ((LineId IS NULL AND @LineId IS NULL) OR LineId=@LineId)
              AND PermissionResource=@Permission AND RequestedByUserId=@UserId
              AND Status=N'Approved' AND ExpiresAt>@Now;
            IF @@ROWCOUNT = 1
                SELECT CAST(1 AS bit);
            ELSE IF EXISTS(
                SELECT 1 FROM dbo.PosApprovalRequests
                WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND DraftId=@DraftId AND ((LineId IS NULL AND @LineId IS NULL) OR LineId=@LineId)
                  AND PermissionResource=@Permission AND RequestedByUserId=@UserId
                  AND Status IN(N'Reserved',N'Consumed') AND ConsumedByOperationId=@OperationId)
                SELECT CAST(1 AS bit);
            ELSE
                SELECT CAST(0 AS bit);
            """;
        await using var command=new SqlCommand(sql,connection);Add(command,"@Now",timeProvider.GetUtcNow());Add(command,"@OperationId",operationId);
        Add(command,"@Id",approvalRequestId);Add(command,"@TenantId",user.TenantId);Add(command,"@BusinessId",businessId);
        Add(command,"@DraftId",draftId);Add(command,"@LineId",lineId);Add(command,"@Permission",permissionResource);Add(command,"@UserId",user.UserId);
        if(!Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
            throw new PosApprovalException("InvalidApproval","La aprobaciÃ³n no corresponde a esta acciÃ³n, venciÃ³ o ya fue utilizada.");
    }

    public async Task CompleteAsync(
        PosApprovalUserIdentity user, Guid approvalRequestId, Guid operationId,
        CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        const string sql="""
            UPDATE dbo.PosApprovalRequests WITH(UPDLOCK,ROWLOCK)
            SET Status=N'Consumed',ConsumedAt=@Now
            WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND RequestedByUserId=@UserId AND Status=N'Reserved'
              AND ConsumedByOperationId=@OperationId;
            DECLARE @Changed INT=@@ROWCOUNT;
            IF @Changed=1
            BEGIN
              DECLARE @Cursor BIGINT;
              SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
              FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND Stream=N'Approvals';
              INSERT dbo.PosSynchronizationOutboxMessages
                (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
              VALUES(@NotificationId,@BusinessId,N'Approvals',@Cursor,@Now);
            END
            IF @Changed=1 OR EXISTS(
                SELECT 1 FROM dbo.PosApprovalRequests
                WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND RequestedByUserId=@UserId AND Status=N'Consumed'
                  AND ConsumedByOperationId=@OperationId)
                SELECT CAST(1 AS bit);
            ELSE
                SELECT CAST(0 AS bit);
            """;
        await using var command=new SqlCommand(sql,connection);Add(command,"@Now",timeProvider.GetUtcNow());Add(command,"@OperationId",operationId);
        Add(command,"@Id",approvalRequestId);Add(command,"@TenantId",user.TenantId);Add(command,"@BusinessId",user.BusinessId);
        Add(command,"@UserId",user.UserId);Add(command,"@NotificationId",ids.NewId());
        if(!Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
            throw new PosApprovalException("InvalidApproval","La reserva de aprobaciÃ³n no corresponde a esta operaciÃ³n.");
    }

    public async Task<PosApprovalUserIdentity?> ResolveDeviceUserAsync(
        Guid tenantId,
        Guid deviceId,
        Guid businessId,
        Guid userId,
        Guid workSessionId,
        CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        const string scopeSql="""
            SELECT COUNT(*)
            FROM dbo.WorkSessions ws
            JOIN dbo.Businesses b ON b.BusinessId=ws.BusinessId
            JOIN dbo.EnrolledDevices d ON d.DeviceId=ws.DeviceId
            JOIN dbo.AppUsers u ON u.UserId=ws.UserId
            WHERE ws.WorkSessionId=@WorkSessionId AND ws.BusinessId=@BusinessId
              AND ws.UserId=@UserId AND ws.DeviceId=@DeviceId AND ws.Status=N'Open'
              AND b.TenantId=@TenantId AND d.TenantId=@TenantId AND d.IsActive=1
              AND u.TenantId=@TenantId AND u.IsActive=1;
            """;
        await using(var scope=new SqlCommand(scopeSql,connection))
        {
            Add(scope,"@WorkSessionId",workSessionId); Add(scope,"@BusinessId",businessId);
            Add(scope,"@UserId",userId); Add(scope,"@DeviceId",deviceId); Add(scope,"@TenantId",tenantId);
            if(Convert.ToInt32(await scope.ExecuteScalarAsync(cancellationToken))!=1) return null;
        }
        const string permissionSql="""
            SELECT DISTINCT p.Resource
            FROM dbo.UserRoles ur
            JOIN dbo.AppRoles r ON r.RoleId=ur.RoleId AND r.IsActive=1 AND r.TenantId=@TenantId
            JOIN dbo.RolePermissions rp ON rp.RoleId=r.RoleId
            JOIN dbo.Permissions p ON p.PermissionId=rp.PermissionId
            WHERE ur.UserId=@UserId AND (ur.BusinessId IS NULL OR ur.BusinessId=@BusinessId);
            """;
        await using var command=new SqlCommand(permissionSql,connection);
        Add(command,"@TenantId",tenantId); Add(command,"@UserId",userId); Add(command,"@BusinessId",businessId);
        var permissions=new HashSet<string>(StringComparer.Ordinal);
        await using var reader=await command.ExecuteReaderAsync(cancellationToken);
        while(await reader.ReadAsync(cancellationToken)) permissions.Add(reader.GetString(0));
        return new PosApprovalUserIdentity(userId,tenantId,businessId,permissions);
    }

    public async Task<PosApprovalDeviceReservation> ReserveForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid approvalRequestId,
        ReservePosApprovalForDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        const string sql="""
            UPDATE dbo.PosApprovalRequests WITH(UPDLOCK,ROWLOCK)
            SET Status=N'Reserved',ReservedAt=@Now,ConsumedByOperationId=@OperationId
            WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND DeviceId=@DeviceId AND WorkSessionId=@WorkSessionId
              AND DraftId=@DraftId AND ((LineId IS NULL AND @LineId IS NULL) OR LineId=@LineId)
              AND PermissionResource=@Permission AND RequestedByUserId=@UserId
              AND Status=N'Approved' AND ExpiresAt>@Now;
            IF @@ROWCOUNT = 1
                SELECT DecidedByUserId FROM dbo.PosApprovalRequests WHERE ApprovalRequestId=@Id;
            ELSE
                SELECT DecidedByUserId FROM dbo.PosApprovalRequests
                WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND DeviceId=@DeviceId AND WorkSessionId=@WorkSessionId
                  AND DraftId=@DraftId AND ((LineId IS NULL AND @LineId IS NULL) OR LineId=@LineId)
                  AND PermissionResource=@Permission AND RequestedByUserId=@UserId
                  AND Status IN(N'Reserved',N'Consumed') AND ConsumedByOperationId=@OperationId;
            """;
        await using var command=new SqlCommand(sql,connection);
        Add(command,"@Now",timeProvider.GetUtcNow()); Add(command,"@OperationId",request.OperationId);
        Add(command,"@Id",approvalRequestId); Add(command,"@TenantId",tenantId);
        Add(command,"@BusinessId",request.BusinessId); Add(command,"@DeviceId",deviceId);
        Add(command,"@WorkSessionId",request.WorkSessionId); Add(command,"@DraftId",request.DraftId);
        Add(command,"@LineId",request.LineId); Add(command,"@Permission",request.PermissionResource);
        Add(command,"@UserId",request.UserId);
        var decidedBy=await command.ExecuteScalarAsync(cancellationToken);
        if(decidedBy is not Guid authorizerId)
            throw new PosApprovalException("InvalidApproval","La aprobación no corresponde a este dispositivo, usuario, sesión o acción.");
        return new PosApprovalDeviceReservation(approvalRequestId,authorizerId,request.OperationId);
    }

    public async Task CompleteForDeviceAsync(
        Guid tenantId,
        Guid deviceId,
        Guid approvalRequestId,
        CompletePosApprovalForDeviceRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection=connections.Create(); await connection.OpenAsync(cancellationToken);
        const string sql="""
            UPDATE dbo.PosApprovalRequests WITH(UPDLOCK,ROWLOCK)
            SET Status=N'Consumed',ConsumedAt=@Now
            WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
              AND DeviceId=@DeviceId AND RequestedByUserId=@UserId
              AND Status=N'Reserved' AND ConsumedByOperationId=@OperationId;
            DECLARE @Changed INT=@@ROWCOUNT;
            IF @Changed=1
            BEGIN
              DECLARE @Cursor BIGINT;
              SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
              FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND Stream=N'Approvals';
              INSERT dbo.PosSynchronizationOutboxMessages
                (NotificationId,BusinessId,Stream,AvailableThroughCursor,OccurredAt)
              VALUES(@NotificationId,@BusinessId,N'Approvals',@Cursor,@Now);
            END
            IF @Changed=1 OR EXISTS(
                SELECT 1 FROM dbo.PosApprovalRequests
                WHERE ApprovalRequestId=@Id AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND DeviceId=@DeviceId AND RequestedByUserId=@UserId
                  AND Status=N'Consumed' AND ConsumedByOperationId=@OperationId)
                SELECT CAST(1 AS bit);
            ELSE SELECT CAST(0 AS bit);
            """;
        await using var command=new SqlCommand(sql,connection);
        Add(command,"@Now",timeProvider.GetUtcNow()); Add(command,"@OperationId",request.OperationId);
        Add(command,"@Id",approvalRequestId); Add(command,"@TenantId",tenantId);
        Add(command,"@BusinessId",request.BusinessId); Add(command,"@DeviceId",deviceId);
        Add(command,"@UserId",request.UserId); Add(command,"@NotificationId",ids.NewId());
        if(!Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken)))
            throw new PosApprovalException("InvalidApproval","La reserva remota no corresponde a esta operación.");
    }

    private const string SelectSql="""
        SELECT a.ApprovalRequestId,a.TenantId,a.BusinessId,a.DeviceId,a.WorkSessionId,a.DraftId,a.LineId,
               a.PermissionResource,a.RequestedByUserId,
               LTRIM(RTRIM(CONCAT(requester.FirstName,N' ',requester.LastName))),a.ContextJson,a.Status,
               a.RequestedAt,a.ExpiresAt,a.DecidedByUserId,
               LTRIM(RTRIM(CONCAT(decider.FirstName,N' ',decider.LastName))),a.DecisionMethod,a.DecidedAt
        FROM dbo.PosApprovalRequests a
        JOIN dbo.AppUsers requester ON requester.UserId=a.RequestedByUserId
        LEFT JOIN dbo.AppUsers decider ON decider.UserId=a.DecidedByUserId
        """;

    private static async Task<PosApprovalRequestView?> ReadOneAsync(SqlConnection connection,Guid tenantId,Guid id,CancellationToken ct)
    { await using var command=new SqlCommand(SelectSql+" WHERE a.TenantId=@TenantId AND a.ApprovalRequestId=@Id;",connection);Add(command,"@TenantId",tenantId);Add(command,"@Id",id);return (await ReadManyAsync(command,ct)).SingleOrDefault(); }
    private static async Task<IReadOnlyList<PosApprovalRequestView>> ReadManyAsync(SqlCommand command,CancellationToken ct)
    { var list=new List<PosApprovalRequestView>();await using var reader=await command.ExecuteReaderAsync(ct);while(await reader.ReadAsync(ct))list.Add(new(reader.GetGuid(0),reader.GetGuid(1),reader.GetGuid(2),reader.IsDBNull(3)?null:reader.GetGuid(3),reader.IsDBNull(4)?null:reader.GetGuid(4),reader.GetGuid(5),reader.IsDBNull(6)?null:reader.GetGuid(6),reader.GetString(7),reader.GetGuid(8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetFieldValue<DateTimeOffset>(12),reader.GetFieldValue<DateTimeOffset>(13),reader.IsDBNull(14)?null:reader.GetGuid(14),reader.IsDBNull(15)?null:reader.GetString(15),reader.IsDBNull(16)?null:reader.GetString(16),reader.IsDBNull(17)?null:reader.GetFieldValue<DateTimeOffset>(17)));return list; }
    private static async Task ExpireAsync(SqlConnection connection,Guid id,CancellationToken ct){await using var c=new SqlCommand("UPDATE dbo.PosApprovalRequests SET Status=N'Expired',ReservedAt=NULL,ConsumedByOperationId=NULL WHERE ApprovalRequestId=@Id AND Status IN(N'Pending',N'Approved',N'Reserved') AND ExpiresAt<=SYSUTCDATETIME();",connection);Add(c,"@Id",id);await c.ExecuteNonQueryAsync(ct);}
    private static async Task<bool> ScopeMatchesAsync(SqlConnection connection,Guid tenantId,Guid businessId,Guid userId,CancellationToken ct){await using var c=new SqlCommand("SELECT COUNT(1) FROM dbo.Businesses b JOIN dbo.AppUsers u ON u.UserId=@UserId AND u.TenantId=b.TenantId WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId AND b.IsActive=1 AND u.IsActive=1;",connection);Add(c,"@UserId",userId);Add(c,"@BusinessId",businessId);Add(c,"@TenantId",tenantId);return Convert.ToInt32(await c.ExecuteScalarAsync(ct))==1;}
    private static void Add(SqlCommand command,string name,object? value)=>command.Parameters.AddWithValue(name,value??DBNull.Value);
}
