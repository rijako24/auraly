using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Domain.Repositories;

public interface IIntegrationConnectionRepository
{
    Task<IReadOnlyList<IntegrationConnection>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default);
    Task<IReadOnlyList<IntegrationConnection>> GetByBusinessConnectionTypeAsync(
        Guid businessId,
        ConnectionType connectionType,
        CancellationToken ct = default);
    Task<IntegrationConnection?> GetByBusinessProviderCapabilityAsync(
        Guid businessId,
        IntegrationProvider provider,
        IntegrationCapability capability,
        CancellationToken ct = default);
    Task<IntegrationConnection?> GetCommerceConnectionAsync(
        Guid businessId,
        CommerceProvider provider,
        CommerceCapability capability = CommerceCapability.CatalogAndOrders,
        CancellationToken ct = default);
    Task<IReadOnlyList<IntegrationConnection>> GetEnabledCommerceConnectionsAsync(
        CommerceProvider provider,
        CommerceCapability capability = CommerceCapability.CatalogAndOrders,
        CancellationToken ct = default);
    Task<IntegrationChannelWarehouse?> GetChannelWarehouseAsync(
        Guid businessId,
        Guid integrationConnectionId,
        Guid businessWhatsAppNumberId,
        CancellationToken ct = default);
    Task<IReadOnlyList<IntegrationChannelWarehouse>> GetChannelWarehousesAsync(
        Guid businessId, Guid integrationConnectionId, CancellationToken ct = default);
    Task<IntegrationChannelWarehouse> UpsertChannelWarehouseAsync(
        IntegrationChannelWarehouse warehouse, CancellationToken ct = default);
    Task<IntegrationConnection> CreateAsync(IntegrationConnection connection, CancellationToken ct = default);
    Task<IntegrationConnection> UpdateAsync(IntegrationConnection connection, CancellationToken ct = default);
}
