using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;

namespace Auraly.Platform.Infrastructure.Repositories;

public class ReservationAddOnRepository : IReservationAddOnRepository
{
    private readonly ApplicationDbContext _context;

    public ReservationAddOnRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(ReservationAddOn addOn)
    {
        await _context.ReservationAddOns.AddAsync(addOn);
    }

    public async Task<IReadOnlyList<ReservationAddOn>> GetByReservationIdAsync(Guid reservationId) =>
        await _context.ReservationAddOns
            .Include(a => a.AddOnService)
            .Where(a => a.ReservationId == reservationId)
            .ToListAsync();

    public Task DeleteAsync(ReservationAddOn addOn)
    {
        _context.ReservationAddOns.Remove(addOn);
        return Task.CompletedTask;
    }
}
