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
    private readonly Uri publicAppUri = ResolvePublicAppUri(configuration);

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
        var title = "Auraly · autorización POS";
        var body = request.DeviceId is { } deviceId
            ? $"Caja {deviceId.ToString("N")[..8].ToUpperInvariant()} · {request.RequestedByName} solicita autorización."
            : $"{request.RequestedByName} solicita autorización para una acción protegida.";
        var relativeUrl = $"/dashboard?posApproval={request.ApprovalRequestId:D}";
        var navigateUrl = new Uri(publicAppUri, relativeUrl).AbsoluteUri;
        var payload = JsonSerializer.Serialize(new
        {
            web_push = 8030,
            notification = new
            {
                title,
                body,
                navigate = navigateUrl,
                silent = false
            },
            title,
            body,
            tag = request.ApprovalRequestId.ToString("D"),
            url = relativeUrl
        });
        await Task.WhenAll(recipients.Select(subscription =>
            DeliverAsync(subscription, request.ApprovalRequestId, payload, cancellationToken)));
    }

    private async Task DeliverAsync(
        PosApprovalPushRecipient subscription,
        Guid approvalRequestId,
        string payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var target = new PushSubscription
            {
                Endpoint = subscription.Endpoint,
                Keys = new Dictionary<string, string>
                {
                    ["p256dh"] = subscription.P256dh,
                    ["auth"] = subscription.Auth
                }
            };
            var message = new PushMessage(payload)
            {
                TimeToLive = 600,
                // RFC 8030 limits Topic to 32 URL-safe characters. The previous
                // "pos-" prefix made the GUID 36 characters and push services
                // could reject the notification while realtime still worked.
                Topic = approvalRequestId.ToString("N"),
                Urgency = PushMessageUrgency.High
            };
            using var authentication = new VapidAuthentication(publicKey!, privateKey!)
            {
                Subject = subject
            };
            await pushClient.RequestPushMessageDeliveryAsync(
                target,
                message,
                authentication,
                cancellationToken);
        }
        catch (PushServiceClientException exception)
            when (exception.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            try
            {
                await subscriptions.DeleteAsync(subscription.SubscriptionId, cancellationToken);
            }
            catch (Exception cleanupException) when (cleanupException is not OperationCanceledException)
            {
                logger.LogWarning(
                    cleanupException,
                    "Expired Web Push subscription {SubscriptionId} could not be removed.",
                    subscription.SubscriptionId);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Web Push delivery failed for POS approval {ApprovalRequestId}.",
                approvalRequestId);
        }
    }

    private static Uri ResolvePublicAppUri(IConfiguration configuration)
    {
        var configured = configuration["Notifications:WebPush:PublicAppUrl"]
            ?? configuration["Auraly:Email:PublicAppUrl"]
            ?? "https://auralyapp.co";
        if (!Uri.TryCreate(configured.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                "Notifications:WebPush:PublicAppUrl must be an absolute HTTPS URL.");
        return uri;
    }

}
