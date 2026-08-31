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
        CreateAccessUri(
            deviceId,
            [
                PosSynchronizationGroups.Business(tenantId, businessId),
                PosSynchronizationGroups.Device(tenantId, deviceId)
            ],
            cancellationToken);

    public Uri CreateUserClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        CreateAccessUri(
            userId,
            [
                PosSynchronizationGroups.Business(tenantId, businessId),
                PosSynchronizationGroups.User(tenantId, userId)
            ],
            cancellationToken);

    public Task SendAsync(
        PosSynchronizationInvalidation invalidation,
        CancellationToken cancellationToken = default) =>
        client.SendToGroupAsync(
            invalidation.TargetDeviceId is { } deviceId
                ? PosSynchronizationGroups.Device(invalidation.TenantId, deviceId)
                : PosSynchronizationGroups.Business(
                    invalidation.TenantId,
                    invalidation.BusinessId),
            RequestContent.Create(JsonSerializer.Serialize(invalidation)),
            ContentType.ApplicationJson,
            excluded: null,
            filter: null,
            new RequestContext { CancellationToken = cancellationToken });

    private Uri CreateAccessUri(
        Guid userId,
        IReadOnlyList<string> groups,
        CancellationToken cancellationToken) =>
        client.GetClientAccessUri(
            expiresAfter: AccessDuration,
            userId: userId.ToString("D"),
            roles: groups.Select(group => $"webpubsub.joinLeaveGroup.{group}"),
            groups: groups,
            cancellationToken: cancellationToken);
}

public sealed record PosSynchronizationNegotiationResponse(
    Uri ClientAccessUri,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<string>? Groups = null);
