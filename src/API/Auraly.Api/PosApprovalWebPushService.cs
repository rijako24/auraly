using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;
using Auraly.Infrastructure.Persistence;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public sealed record PosApprovalPushSubscriptionRequest(
    string Endpoint,
    string P256dh,
    string Auth);

public sealed class PosApprovalWebPushService(
    SqlServerConnectionFactory connections,
    PushServiceClient pushClient,
    IConfiguration configuration,
    ILogger<PosApprovalWebPushService> logger)
{
    private readonly string? publicKey = configuration["Notifications:WebPush:PublicKey"];
    private readonly string? privateKey = configuration["Notifications:WebPush:PrivateKey"];
    private readonly string subject = configuration["Notifications:WebPush:Subject"] ?? "mailto:soporte@auraly.app";

    public string PublicKey() => !string.IsNullOrWhiteSpace(publicKey)
        ? publicKey
        : throw new PosApprovalException("PushUnavailable", "Las notificaciones push todavía no están configuradas.");

    public async Task SubscribeAsync(
        PosApprovalUserIdentity user,
        PosApprovalPushSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        if (!user.Permissions.Contains(CommercePermissionCodes.PosApprovalsReceiveNotifications))
            throw new PosApprovalException("Forbidden", "El usuario no tiene permiso para recibir notificaciones de autorización POS.");
        if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint) || endpoint.Scheme != Uri.UriSchemeHttps ||
            request.Endpoint.Length > 2000 || request.P256dh.Length is < 20 or > 512 || request.Auth.Length is < 8 or > 256)
            throw new PosApprovalException("InvalidPushSubscription", "La suscripción push no es válida.");

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(request.Endpoint));
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
        command.Parameters.AddWithValue("@Endpoint", request.Endpoint);
        command.Parameters.AddWithValue("@Hash", hash);
        command.Parameters.AddWithValue("@P256dh", request.P256dh);
        command.Parameters.AddWithValue("@Auth", request.Auth);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UnsubscribeAsync(
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

    public async Task NotifyAsync(PosApprovalRequestView request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogWarning("POS approval {ApprovalRequestId} was created without Web Push configuration.", request.ApprovalRequestId);
            return;
        }

        List<SubscriptionRow> subscriptions;
        try
        {
            subscriptions = await RecipientsAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Push is an additional delivery channel. A temporary notification
            // failure must never suppress the authorization dialog at the register.
            logger.LogWarning(exception,
                "Web Push recipients could not be resolved for POS approval {ApprovalRequestId}.",
                request.ApprovalRequestId);
            return;
        }
        if (subscriptions.Count == 0) return;
        var payload = JsonSerializer.Serialize(new
        {
            title = "Auraly · autorización POS",
            body = request.DeviceId is { } deviceId
                ? $"Caja {deviceId.ToString("N")[..8].ToUpperInvariant()} · {request.RequestedByName} solicita autorización."
                : $"{request.RequestedByName} solicita autorización para una acción protegida.",
            tag = request.ApprovalRequestId.ToString("D"),
            url = $"/dashboard?posApproval={request.ApprovalRequestId:D}"
        });
        using var authentication = new VapidAuthentication(publicKey, privateKey) { Subject = subject };

        foreach (var subscription in subscriptions)
        {
            try
            {
                var target = new PushSubscription
                {
                    Endpoint = subscription.Endpoint,
                    Keys = new Dictionary<string, string> { ["p256dh"] = subscription.P256dh, ["auth"] = subscription.Auth }
                };
                var message = new PushMessage(payload) { TimeToLive = 600, Topic = $"pos-{request.ApprovalRequestId:N}", Urgency = PushMessageUrgency.High };
                await pushClient.RequestPushMessageDeliveryAsync(target, message, authentication, cancellationToken);
            }
            catch (PushServiceClientException exception) when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                await DeleteSubscriptionAsync(subscription.SubscriptionId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Web Push delivery failed for POS approval {ApprovalRequestId}.", request.ApprovalRequestId);
            }
        }
    }

    private async Task<List<SubscriptionRow>> RecipientsAsync(PosApprovalRequestView request, CancellationToken cancellationToken)
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
        var rows = new List<SubscriptionRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    private async Task DeleteSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("DELETE dbo.PosApprovalPushSubscriptions WHERE SubscriptionId=@Id;", connection);
        command.Parameters.AddWithValue("@Id", subscriptionId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record SubscriptionRow(Guid SubscriptionId, string Endpoint, string P256dh, string Auth);
}
