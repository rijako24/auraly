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

    public Task<Uri> CreateClientAccessUriAsync(
        Guid tenantId,
        Guid businessId,
        Guid deviceId,
        CancellationToken cancellationToken = default) =>
        CreateAccessUriAsync(
            deviceId,
            [
                PosSynchronizationGroups.Business(tenantId, businessId),
                PosSynchronizationGroups.Device(tenantId, deviceId)
            ],
            cancellationToken);

    public Task<Uri> CreateUserClientAccessUriAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        CreateAccessUriAsync(
            userId,
            [
                PosSynchronizationGroups.Business(tenantId, businessId),
                PosSynchronizationGroups.User(tenantId, userId)
            ],
            cancellationToken);

    public Task<Uri> CreateRestaurantCommandsClientAccessUriAsync(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken = default) =>
        CreateAccessUriAsync(
            userId,
            [PosSynchronizationGroups.RestaurantCommands(tenantId, businessId)],
            cancellationToken);

    public Task SendAsync(
        PosSynchronizationInvalidation invalidation,
        CancellationToken cancellationToken = default) =>
        client.SendToGroupAsync(
            PosSynchronizationGroups.Target(invalidation),
            RequestContent.Create(JsonSerializer.Serialize(invalidation)),
            ContentType.ApplicationJson,
            excluded: null,
            filter: null,
            new RequestContext { CancellationToken = cancellationToken });

    private Task<Uri> CreateAccessUriAsync(
        Guid userId,
        IReadOnlyList<string> groups,
        CancellationToken cancellationToken) =>
        // SDK 1.6.0 drops groups on its synchronous TokenCredential path.
        // Keep negotiation asynchronous so managed identity clients join their
        // authorized groups as soon as the connection opens.
        client.GetClientAccessUriAsync(
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
