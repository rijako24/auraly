using System.Net;
using System.Text.Json;
using Auraly.Application.Authorization;
using Auraly.Contracts.Authorization;
using Lib.Net.Http.WebPush;
using Lib.Net.Http.WebPush.Authentication;

namespace Auraly.Api;

public sealed record PosApprovalPushSubscriptionRequest(
    string Endpoint,
    string P256dh,
    string Auth);

public sealed class PosApprovalWebPushService(
    IPosApprovalPushSubscriptionStore subscriptions,
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

        await subscriptions.UpsertAsync(
            user, request.Endpoint, request.P256dh, request.Auth, cancellationToken);
    }

    public async Task UnsubscribeAsync(
        PosApprovalUserIdentity user,
        string endpoint,
        CancellationToken cancellationToken)
    {
        await subscriptions.DeleteAsync(user, endpoint, cancellationToken);
    }

    public async Task NotifyAsync(PosApprovalRequestView request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || string.IsNullOrWhiteSpace(privateKey))
        {
            logger.LogWarning("POS approval {ApprovalRequestId} was created without Web Push configuration.", request.ApprovalRequestId);
            return;
        }

        IReadOnlyList<PosApprovalPushRecipient> recipients;
        try
        {
            recipients = await subscriptions.RecipientsAsync(request, cancellationToken);
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
        if (recipients.Count == 0) return;
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

        foreach (var subscription in recipients)
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
                await subscriptions.DeleteAsync(subscription.SubscriptionId, cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Web Push delivery failed for POS approval {ApprovalRequestId}.", request.ApprovalRequestId);
            }
        }
    }

}
