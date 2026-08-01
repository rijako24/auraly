namespace Auraly.BuildingBlocks.Application.Synchronization;

public static class PosSynchronizationStreams
{
    public const string Catalog = "Catalog";
    public const string Customers = "Customers";
    public const string Security = "Security";
    public const string FiscalStatus = "FiscalStatus";
    public const string LocalOutbox = "LocalOutbox";
    public const string Authentication = "Authentication";
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
