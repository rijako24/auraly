using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class IntegrationConnectionRepository : IIntegrationConnectionRepository
{
    private readonly ApplicationDbContext _context;

    public IntegrationConnectionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<IntegrationConnection>> GetByBusinessIdAsync(Guid businessId, CancellationToken ct = default)
    {
        return await _context.IntegrationConnections
            .Where(c => c.BusinessId == businessId)
            .OrderBy(c => c.ConnectionType)
            .ThenBy(c => c.Provider)
            .ThenBy(c => c.Capability)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<IntegrationConnection>> GetByBusinessConnectionTypeAsync(
        Guid businessId,
        ConnectionType connectionType,
        CancellationToken ct = default)
    {
        return await _context.IntegrationConnections
            .Where(c => c.BusinessId == businessId && c.ConnectionType == connectionType)
            .OrderBy(c => c.Provider)
            .ThenBy(c => c.Capability)
            .ToListAsync(ct);
    }

    public async Task<IntegrationConnection?> GetByBusinessProviderCapabilityAsync(
        Guid businessId,
        IntegrationProvider provider,
        IntegrationCapability capability,
        CancellationToken ct = default)
    {
        return await _context.IntegrationConnections
            .FirstOrDefaultAsync(c =>
                c.BusinessId == businessId &&
                c.ConnectionType == ConnectionType.Integration &&
                c.Provider == (int)provider &&
                c.Capability == (int)capability,
                ct);
    }

    public async Task<IntegrationConnection?> GetCommerceConnectionAsync(
        Guid businessId,
        CommerceProvider provider,
        CommerceCapability capability = CommerceCapability.CatalogAndOrders,
        CancellationToken ct = default)
    {
        return await _context.IntegrationConnections
            .FirstOrDefaultAsync(c =>
                c.BusinessId == businessId &&
                c.ConnectionType == ConnectionType.Commerce &&
                c.Provider == (int)provider &&
                c.Capability == (int)capability,
                ct);
    }

    public async Task<IReadOnlyList<IntegrationConnection>> GetEnabledCommerceConnectionsAsync(
        CommerceProvider provider,
        CommerceCapability capability = CommerceCapability.CatalogAndOrders,
        CancellationToken ct = default) =>
        await _context.IntegrationConnections
            .Where(connection =>
                connection.ConnectionType == ConnectionType.Commerce
                && connection.Provider == (int)provider
                && connection.Capability == (int)capability
                && connection.IsEnabled)
            .OrderBy(connection => connection.BusinessId)
            .ToListAsync(ct);

    public Task<IntegrationChannelWarehouse?> GetChannelWarehouseAsync(
        Guid businessId, Guid integrationConnectionId, Guid businessWhatsAppNumberId,
        CancellationToken ct = default) =>
        _context.IntegrationChannelWarehouses.FirstOrDefaultAsync(mapping =>
            mapping.BusinessId == businessId
            && mapping.IntegrationConnectionId == integrationConnectionId
            && mapping.BusinessWhatsAppNumberId == businessWhatsAppNumberId, ct);

    public async Task<IReadOnlyList<IntegrationChannelWarehouse>> GetChannelWarehousesAsync(
        Guid businessId, Guid integrationConnectionId, CancellationToken ct = default) =>
        await _context.IntegrationChannelWarehouses
            .Where(mapping => mapping.BusinessId == businessId
                && mapping.IntegrationConnectionId == integrationConnectionId)
            .OrderBy(mapping => mapping.BusinessWhatsAppNumberId)
            .ToListAsync(ct);

    public async Task<IntegrationChannelWarehouse> UpsertChannelWarehouseAsync(
        IntegrationChannelWarehouse warehouse, CancellationToken ct = default)
    {
        var existing = await GetChannelWarehouseAsync(
            warehouse.BusinessId,
            warehouse.IntegrationConnectionId,
            warehouse.BusinessWhatsAppNumberId,
            ct);
        if (existing is null)
        {
            _context.IntegrationChannelWarehouses.Add(warehouse);
            return warehouse;
        }

        existing.WarehouseCode = warehouse.WarehouseCode;
        existing.WarehouseName = warehouse.WarehouseName;
        existing.IsActive = warehouse.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;
        return existing;
    }

    public Task<IntegrationConnection> CreateAsync(IntegrationConnection connection, CancellationToken ct = default)
    {
        _context.IntegrationConnections.Add(connection);
        return Task.FromResult(connection);
    }

    public Task<IntegrationConnection> UpdateAsync(IntegrationConnection connection, CancellationToken ct = default)
    {
        _context.IntegrationConnections.Update(connection);
        return Task.FromResult(connection);
    }
}
