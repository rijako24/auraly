using Microsoft.EntityFrameworkCore;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;
using MimosBabySpa.Infrastructure.Data;

namespace MimosBabySpa.Infrastructure.Repositories;

public class ReservationRepository : IReservationRepository
{
    private readonly ApplicationDbContext _context;

    public ReservationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Reservation?> GetByIdAsync(Guid reservationId)
    {
        return await _context.Reservations
            .Include(r => r.Business)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);
    }

    public async Task<IEnumerable<Reservation>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.Reservations
            .Where(r => r.BusinessId == businessId)
            .OrderBy(r => r.ReservationDate)
            .ThenBy(r => r.ReservationTime)
            .ToListAsync();
    }

    public async Task<IEnumerable<Reservation>> GetByBusinessIdAndDateRangeAsync(
        Guid businessId, 
        DateTime startDate, 
        DateTime endDate)
    {
        return await _context.Reservations
            .Where(r => r.BusinessId == businessId &&
                       r.ReservationDate >= startDate.Date &&
                       r.ReservationDate <= endDate.Date)
            .OrderBy(r => r.ReservationDate)
            .ThenBy(r => r.ReservationTime)
            .ToListAsync();
    }

    public async Task<IEnumerable<Reservation>> GetByPhoneNumberAsync(string phoneNumber)
    {
        return await _context.Reservations
            .Where(r => r.PhoneNumber == phoneNumber)
            .OrderByDescending(r => r.ReservationDate)
            .ThenByDescending(r => r.ReservationTime)
            .ToListAsync();
    }

    public Task<Reservation> CreateAsync(Reservation reservation)
    {
        _context.Reservations.Add(reservation);
        return Task.FromResult(reservation);
    }

    public Task<Reservation> UpdateAsync(Reservation reservation)
    {
        _context.Reservations.Update(reservation);
        return Task.FromResult(reservation);
    }

    public async Task<bool> ExistsOverlappingReservationAsync(
        Guid businessId, 
        DateTime reservationDate, 
        TimeSpan reservationTime, 
        int durationMinutes, 
        Guid? excludeReservationId = null)
    {
        // Traer todas las reservas del día y validar solapamiento en memoria
        // Dos intervalos se solapan si: start1 < end2 && end1 > start2
        
        // Calcular DateTime de inicio y fin para la nueva reserva
        var newStartDateTime = reservationDate.Date.Add(reservationTime);
        var newEndDateTime = newStartDateTime.AddMinutes(durationMinutes);
        
        // Traer todas las reservas del día desde la base de datos
        var reservations = await _context.Reservations
            .Where(r => r.BusinessId == businessId &&
                       r.Status != Domain.Enums.ReservationStatus.Cancelled &&
                       r.ReservationDate == reservationDate.Date)
            .Select(r => new
            {
                r.ReservationId,
                r.ReservationDate,
                r.ReservationTime,
                r.DurationMinutes
            })
            .ToListAsync();

        // Validar solapamiento en memoria usando LINQ
        // Dos intervalos se solapan si: start1 < end2 && end1 > start2
        return reservations
            .Where(r => !excludeReservationId.HasValue || r.ReservationId != excludeReservationId.Value)
            .Any(r =>
            {
                var reservationStartDateTime = r.ReservationDate.Date.Add(r.ReservationTime);
                var reservationEndDateTime = reservationStartDateTime.AddMinutes(r.DurationMinutes);
                return reservationStartDateTime < newEndDateTime && reservationEndDateTime > newStartDateTime;
            });
    }
}
