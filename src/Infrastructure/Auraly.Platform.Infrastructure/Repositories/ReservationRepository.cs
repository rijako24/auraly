using Microsoft.EntityFrameworkCore;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Repositories;
using Auraly.Platform.Infrastructure.Data;
using Auraly.Platform.Infrastructure.Extensions;

namespace Auraly.Platform.Infrastructure.Repositories;

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
            .Include(r => r.Service)
                .ThenInclude(s => s!.ResourceUsages)
                    .ThenInclude(ru => ru.BusinessResource)
            .Include(r => r.Employee)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId);
    }

    public async Task<IEnumerable<Reservation>> GetByBusinessIdAsync(Guid businessId)
    {
        return await _context.Reservations
            .Include(r => r.Service)
            .Include(r => r.Employee)
            .Where(r => r.BusinessId == businessId)
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();
    }

    public async Task<(IReadOnlyList<Reservation> Items, int TotalCount)> GetPagedByBusinessIdAsync(
        Guid businessId, int page, int pageSize, string? search,
        DateTime? startDate, DateTime? endDate, CancellationToken ct)
    {
        var query = _context.Reservations
            .Include(r => r.Service)
            .Include(r => r.Employee)
            .Where(r => r.BusinessId == businessId);

        if (startDate.HasValue)
            query = query.Where(r => r.ReservationDateTime >= startDate.Value);
        if (endDate.HasValue)
            query = query.Where(r => r.ReservationDateTime <= endDate.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(r =>
                (r.Service != null && r.Service.ServiceName.ToLower().Contains(s)) ||
                (r.Employee != null && r.Employee.Name.ToLower().Contains(s)));
        }

        return await query.OrderByDescending(r => r.ReservationDateTime).ToPagedListAsync(page, pageSize, ct);
    }

    public async Task<IReadOnlyList<Reservation>> GetRecentByBusinessIdAsync(
        Guid businessId, int limit, CancellationToken ct)
    {
        return await _context.Reservations
            .Include(r => r.Service)
            .Include(r => r.Employee)
            .Include(r => r.Conversation)
            .Where(r => r.BusinessId == businessId)
            .OrderByDescending(r => r.ReservationDateTime)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Reservation>> GetUpcomingConfirmedByBusinessIdAsync(
        Guid businessId,
        DateTime fromLocal,
        DateTime toLocal,
        CancellationToken ct = default)
    {
        return await _context.Reservations
            .Include(r => r.Service)
            .Include(r => r.Employee)
            .Include(r => r.AddOns)
                .ThenInclude(a => a.AddOnService)
            .Where(r => r.BusinessId == businessId
                && r.Status == Domain.Enums.ReservationStatus.Confirmed
                && r.ReservationDateTime.HasValue
                && r.ReservationDateTime.Value >= fromLocal
                && r.ReservationDateTime.Value <= toLocal)
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<(Guid ServiceId, string ServiceName, int TotalReservations, decimal Revenue)>> GetTopServicesByBusinessIdAsync(
        Guid businessId, int limit, DateTime? from, DateTime? to, CancellationToken ct)
    {
        var query = _context.Reservations
            .Where(r => r.BusinessId == businessId && r.Status != Domain.Enums.ReservationStatus.Cancelled);

        if (from.HasValue)
            query = query.Where(r => r.ReservationDateTime >= from.Value);
        if (to.HasValue)
            query = query.Where(r => r.ReservationDateTime <= to.Value);

        var grouped = await query
            .Join(_context.Services, r => r.ServiceId, s => s.ServiceId, (r, s) => new { Reservation = r, Service = s })
            .GroupBy(x => new { x.Service.ServiceId, x.Service.ServiceName })
            .Select(g => new
            {
                g.Key.ServiceId,
                g.Key.ServiceName,
                TotalReservations = g.Count(),
                Revenue = g.Sum(x => x.Service.Price)
            })
            .OrderByDescending(x => x.TotalReservations)
            .Take(limit)
            .ToListAsync(ct);

        return grouped.Select(x => (x.ServiceId, x.ServiceName, x.TotalReservations, x.Revenue)).ToList();
    }

    public async Task<IReadOnlyList<Reservation>> GetLatestCompletedCustomerReservationsWithoutFutureAsync(
        Guid businessId,
        DateTime completedBeforeUtc,
        DateTime futureFromUtc,
        int limit,
        CancellationToken ct = default)
    {
        var futurePhones = await _context.Reservations
            .Where(r => r.BusinessId == businessId
                && r.CustomerPhoneSnapshot != null
                && r.ReservationDateTime.HasValue
                && r.ReservationDateTime.Value >= futureFromUtc
                && (r.Status == Domain.Enums.ReservationStatus.Confirmed
                    || r.Status == Domain.Enums.ReservationStatus.OnHold
                    || r.Status == Domain.Enums.ReservationStatus.PendingCalendar))
            .Select(r => r.CustomerPhoneSnapshot!.Trim())
            .Distinct()
            .ToListAsync(ct);

        var futurePhoneSet = futurePhones.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var completed = await _context.Reservations
            .Include(r => r.Service)
            .Where(r => r.BusinessId == businessId
                && r.CustomerPhoneSnapshot != null
                && r.ReservationDateTime.HasValue
                && r.ReservationDateTime.Value <= completedBeforeUtc
                && r.Status == Domain.Enums.ReservationStatus.Completed)
            .OrderByDescending(r => r.ReservationDateTime)
            .Take(Math.Max(limit * 5, limit))
            .ToListAsync(ct);

        return completed
            .Where(r => !futurePhoneSet.Contains(r.CustomerPhoneSnapshot!.Trim()))
            .GroupBy(r => r.CustomerPhoneSnapshot!.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(r => r.ReservationDateTime).First())
            .Take(limit)
            .ToList();
    }

    public async Task<IEnumerable<Reservation>> GetByBusinessIdAndDateRangeAsync(
        Guid businessId,
        DateTime startDate,
        DateTime endDate)
    {
        return await _context.Reservations
            .Include(r => r.Service)
                .ThenInclude(s => s!.ResourceUsages)
                    .ThenInclude(ru => ru.BusinessResource)
            .Include(r => r.Employee)
            .Where(r => r.BusinessId == businessId &&
                       r.ReservationDateTime >= startDate &&
                       r.ReservationDateTime <= endDate)
            .OrderBy(r => r.ReservationDateTime)
            .ToListAsync();
    }

    public async Task<Reservation?> GetActiveByConversationIdAsync(Guid conversationId, CancellationToken ct = default)
    {
        return await _context.Reservations
            .Include(r => r.Service)
            .Include(r => r.Employee)
            .Include(r => r.AddOns)
                .ThenInclude(a => a.AddOnService)
            .Where(r => r.ConversationId == conversationId
                && r.Status == Domain.Enums.ReservationStatus.Confirmed)
            .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<Reservation>> GetManageableByConversationIdAsync(
        Guid conversationId,
        DateOnly businessToday,
        CancellationToken ct = default)
    {
        return await _context.Reservations
            .Include(r => r.Service)
            .Include(r => r.Employee)
            .Include(r => r.AddOns)
                .ThenInclude(a => a.AddOnService)
            .Where(r => r.ConversationId == conversationId
                && (r.Status == Domain.Enums.ReservationStatus.Confirmed
                    || r.Status == Domain.Enums.ReservationStatus.OnHold)
                && (!r.ReservationDateTime.HasValue
                    || DateOnly.FromDateTime(r.ReservationDateTime.Value) >= businessToday))
            .OrderBy(r => r.ReservationDateTime)
            .ThenByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .ToListAsync(ct);
    }
    public async Task<IReadOnlyList<Reservation>> GetManageableByCustomerPhoneAsync(
        Guid businessId,
        string customerPhone,
        DateOnly businessToday,
        CancellationToken ct = default)
    {
        var phone = customerPhone.Trim();

        return await _context.Reservations
            .Include(r => r.Service)
            .Include(r => r.Employee)
            .Include(r => r.AddOns)
                .ThenInclude(a => a.AddOnService)
            .Where(r => r.BusinessId == businessId
                && r.CustomerPhoneSnapshot != null
                && r.CustomerPhoneSnapshot.Trim() == phone
                && (r.Status == Domain.Enums.ReservationStatus.Confirmed
                    || r.Status == Domain.Enums.ReservationStatus.OnHold)
                && (!r.ReservationDateTime.HasValue
                    || DateOnly.FromDateTime(r.ReservationDateTime.Value) >= businessToday))
            .OrderBy(r => r.ReservationDateTime)
            .ThenByDescending(r => r.UpdatedAt ?? r.CreatedAt)
            .ToListAsync(ct);
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
        // Traer todas las reservas del dÃ­a y validar solapamiento en memoria
        // Dos intervalos se solapan si: start1 < end2 && end1 > start2

        // Calcular DateTime de inicio y fin para la nueva reserva
        var newStartDateTime = reservationDate.Date.Add(reservationTime);
        var newEndDateTime = newStartDateTime.AddMinutes(durationMinutes);

        // Traer todas las reservas del dÃ­a desde la base de datos
        var reservations = await _context.Reservations
            .Where(r => r.BusinessId == businessId &&
                       r.ReservationDateTime.HasValue &&
                       r.ReservationDateTime.Value.Date == reservationDate.Date &&
                       (r.Status == Domain.Enums.ReservationStatus.Confirmed ||
                        r.Status == Domain.Enums.ReservationStatus.Completed ||
                        r.Status == Domain.Enums.ReservationStatus.OnHold ||
                        r.Status == Domain.Enums.ReservationStatus.PendingCalendar))
            .Select(r => new
            {
                r.ReservationId,
                r.ReservationDateTime,
                r.DurationMinutes
            })
            .ToListAsync();

        // Validar solapamiento en memoria usando LINQ
        // Dos intervalos se solapan si: start1 < end2 && end1 > start2
        return reservations
            .Where(r => r.ReservationDateTime.HasValue && r.DurationMinutes.HasValue)
            .Where(r => !excludeReservationId.HasValue || r.ReservationId != excludeReservationId.Value)
            .Any(r =>
            {
                var start = r.ReservationDateTime!.Value;
                var reservationEndDateTime = start.AddMinutes(r.DurationMinutes!.Value);
                return start < newEndDateTime && reservationEndDateTime > newStartDateTime;
            });
    }
}
