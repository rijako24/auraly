using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ReservationMetadataRepository : IReservationMetadataRepository
{
    private readonly ApplicationDbContext _context;

    public ReservationMetadataRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<ReservationMetadata>> GetByReservationIdAsync(Guid reservationId)
    {
        return await _context.Set<ReservationMetadata>()
            .Where(m => m.ReservationId == reservationId)
            .ToListAsync();
    }

    public Task<ReservationMetadata> CreateAsync(ReservationMetadata metadata)
    {
        _context.Set<ReservationMetadata>().Add(metadata);
        return Task.FromResult(metadata);
    }

    public async Task CreateBatchAsync(IEnumerable<ReservationMetadata> metadata)
    {
        await _context.Set<ReservationMetadata>().AddRangeAsync(metadata);
    }

    public async Task DeleteByReservationIdAsync(Guid reservationId)
    {
        var metadata = await _context.Set<ReservationMetadata>()
            .Where(m => m.ReservationId == reservationId)
            .ToListAsync();
        
        _context.Set<ReservationMetadata>().RemoveRange(metadata);
    }
}
