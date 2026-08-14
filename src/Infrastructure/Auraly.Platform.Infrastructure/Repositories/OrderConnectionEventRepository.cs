using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public sealed class OrderConnectionEventRepository : IOrderConnectionEventRepository
{
    private readonly ApplicationDbContext _context;

    public OrderConnectionEventRepository(ApplicationDbContext context) => _context = context;

    public Task<OrderConnectionEvent?> GetByOrderConnectionAsync(Guid orderId, Guid integrationConnectionId, CancellationToken ct = default) =>
        _context.OrderConnectionEvents.FirstOrDefaultAsync(e =>
            e.OrderId == orderId && e.IntegrationConnectionId == integrationConnectionId,
            ct);

    public Task<OrderConnectionEvent> CreateAsync(OrderConnectionEvent entity, CancellationToken ct = default)
    {
        _context.OrderConnectionEvents.Add(entity);
        return Task.FromResult(entity);
    }

    public Task<OrderConnectionEvent> UpdateAsync(OrderConnectionEvent entity, CancellationToken ct = default)
    {
        _context.OrderConnectionEvents.Update(entity);
        return Task.FromResult(entity);
    }
}
