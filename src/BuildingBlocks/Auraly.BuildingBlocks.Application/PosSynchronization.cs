namespace Auraly.BuildingBlocks.Application.Synchronization;

public static class PosSynchronizationStreams
{
    public const string Catalog = "Catalog";
    public const string Customers = "Customers";
    public const string Security = "Security";
    public const string FiscalStatus = "FiscalStatus";
    public const string FiscalProvisioning = "FiscalProvisioning";
    public const string Approvals = "Approvals";
    public const string LocalOutbox = "LocalOutbox";
    public const string Authentication = "Authentication";
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

public sealed record PosSynchronizationInvalidation(
    Guid NotificationId,
    Guid TenantId,
    Guid BusinessId,
    string Stream,
    long AvailableThroughCursor,
    DateTimeOffset OccurredAt);

public interface IPosSynchronizationPushGateway
{
    Uri CreateClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Uri CreateUserClientAccessUri(
        Guid tenantId,
        Guid businessId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task SendAsync(
        PosSynchronizationInvalidation invalidation,
        CancellationToken cancellationToken = default);
}

public interface IPosSynchronizationOutboxDispatcher
{
    Task DispatchPendingAsync(
        Guid tenantId,
        Guid businessId,
        CancellationToken cancellationToken = default);
}
