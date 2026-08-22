using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosApprovalPushSubscriptionStore(SqlServerConnectionFactory connections)
    : IPosApprovalPushSubscriptionStore
{
    public async Task UpsertAsync(
        PosApprovalUserIdentity user,
        string endpoint,
        string p256dh,
        string auth,
        CancellationToken cancellationToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            UPDATE dbo.PosApprovalPushSubscriptions
            SET TenantId=@TenantId,BusinessId=@BusinessId,Endpoint=@Endpoint,
                P256dh=@P256dh,Auth=@Auth,UpdatedAt=SYSUTCDATETIME()
            WHERE UserId=@UserId AND EndpointHash=@Hash;
            IF @@ROWCOUNT=0
              INSERT dbo.PosApprovalPushSubscriptions
                (SubscriptionId,TenantId,BusinessId,UserId,Endpoint,EndpointHash,P256dh,Auth,CreatedAt,UpdatedAt)
              VALUES(NEWID(),@TenantId,@BusinessId,@UserId,@Endpoint,@Hash,@P256dh,@Auth,SYSUTCDATETIME(),SYSUTCDATETIME());
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Endpoint", endpoint);
        command.Parameters.AddWithValue("@Hash", hash);
        command.Parameters.AddWithValue("@P256dh", p256dh);
        command.Parameters.AddWithValue("@Auth", auth);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        PosApprovalUserIdentity user,
        string endpoint,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return;
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(endpoint));
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            DELETE dbo.PosApprovalPushSubscriptions
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId AND UserId=@UserId AND EndpointHash=@Hash;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Hash", hash);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PosApprovalPushRecipient>> RecipientsAsync(
        PosApprovalRequestView request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT DISTINCT subscription.SubscriptionId,subscription.Endpoint,subscription.P256dh,subscription.Auth
            FROM dbo.PosApprovalPushSubscriptions subscription
            JOIN dbo.AppUsers app ON app.UserId=subscription.UserId AND app.IsActive=1
            WHERE subscription.TenantId=@TenantId AND subscription.BusinessId=@BusinessId
              AND subscription.UserId<>@RequesterId
              AND EXISTS(
                SELECT 1 FROM dbo.UserRoles assignment
                JOIN dbo.RolePermissions rolePermission ON rolePermission.RoleId=assignment.RoleId
                JOIN dbo.Permissions permission ON permission.PermissionId=rolePermission.PermissionId
                WHERE assignment.UserId=subscription.UserId
                  AND(assignment.BusinessId IS NULL OR assignment.BusinessId=@BusinessId)
                  AND permission.Resource=N'pos.approvals.receive_notifications');
            """, connection);
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@RequesterId", request.RequestedByUserId);
        var rows = new List<PosApprovalPushRecipient>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    public async Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "DELETE dbo.PosApprovalPushSubscriptions WHERE SubscriptionId=@Id;", connection);
        command.Parameters.AddWithValue("@Id", subscriptionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
