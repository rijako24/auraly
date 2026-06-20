using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

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
