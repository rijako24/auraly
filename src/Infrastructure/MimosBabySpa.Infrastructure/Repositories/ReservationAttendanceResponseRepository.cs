using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public sealed class ReservationAttendanceResponseRepository : IReservationAttendanceResponseRepository
{
    private readonly ApplicationDbContext _context;

    public ReservationAttendanceResponseRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ReservationAttendanceResponse?> GetLatestByReservationAsync(
        Guid businessId,
        Guid reservationId,
        CancellationToken ct = default)
    {
        return _context.ReservationAttendanceResponses
            .Where(r => r.BusinessId == businessId && r.ReservationId == reservationId)
            .OrderByDescending(r => r.RespondedAtUtc)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ReservationAttendanceResponse> AddAsync(
        ReservationAttendanceResponse response,
        CancellationToken ct = default)
    {
        _context.ReservationAttendanceResponses.Add(response);
        return Task.FromResult(response);
    }
}
