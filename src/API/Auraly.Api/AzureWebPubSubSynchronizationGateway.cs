using System.Text.Json;
using Auraly.BuildingBlocks.Application.Synchronization;
using Azure;
using Azure.Core;
using Azure.Messaging.WebPubSub;

namespace Auraly.Api;

public sealed class AzureWebPubSubSynchronizationGateway(
    WebPubSubServiceClient client) : IPosSynchronizationPushGateway
{
    private static readonly TimeSpan AccessDuration = TimeSpan.FromMinutes(15);

    public Uri CreateClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid deviceId,
        CancellationToken cancellationToken = default) =>
        client.GetClientAccessUri(
            expiresAfter: AccessDuration,
            userId: deviceId.ToString("D"),
            groups:
            [
                PosSynchronizationGroups.Business(tenantId, businessId),
                PosSynchronizationGroups.Device(tenantId, deviceId)
            ],
            cancellationToken: cancellationToken);

    public Uri CreateUserClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        client.GetClientAccessUri(
            expiresAfter: AccessDuration,
            userId: userId.ToString("D"),
            groups:
            [
                PosSynchronizationGroups.Business(tenantId, businessId),
                PosSynchronizationGroups.User(tenantId, userId)
            ],
            cancellationToken: cancellationToken);

    public Task SendAsync(
        PosSynchronizationInvalidation invalidation,
        CancellationToken cancellationToken = default) =>
        client.SendToGroupAsync(
            PosSynchronizationGroups.Business(
                invalidation.TenantId,
                invalidation.BusinessId),
            RequestContent.Create(JsonSerializer.Serialize(invalidation)),
            ContentType.ApplicationJson,
            excluded: null,
            filter: null,
            new RequestContext { CancellationToken = cancellationToken });
}

public static class PosSynchronizationGroups
{
    public static string Business(Guid tenantId, Guid businessId) =>
        $"tenant:{tenantId:D}:business:{businessId:D}";

    public static string Device(Guid tenantId, Guid deviceId) =>
        $"tenant:{tenantId:D}:device:{deviceId:D}";

    public static string User(Guid tenantId, Guid userId) =>
        $"tenant:{tenantId:D}:user:{userId:D}";
}

public sealed record PosSynchronizationNegotiationResponse(
    Uri ClientAccessUri,
    DateTimeOffset ExpiresAt);
