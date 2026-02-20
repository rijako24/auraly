using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.IntegrationTests.Infrastructure;

public enum ReservationMode
{
    AlwaysSucceed,
    AlwaysFail,
    TrackDuplicates
}

public class FakeReservationService : IReservationService
{
    private readonly ReservationMode _mode;
    private readonly List<ReservationDto> _reservationsCreated = [];

    public FakeReservationService(ReservationMode mode = ReservationMode.AlwaysSucceed)
    {
        _mode = mode;
    }

    public IReadOnlyList<ReservationDto> ReservationsCreated => _reservationsCreated.AsReadOnly();

    public Task<ReservationDto> CreateReservationAsync(
        Reservation reservation,
        Dictionary<string, string>? metadata = null,
        IEnumerable<Guid>? addOnServiceIds = null,
        CancellationToken cancellationToken = default)
    {
        if (_mode == ReservationMode.AlwaysFail)
            throw new Exception("Fallo al crear reserva en el backend");

        if (_mode == ReservationMode.TrackDuplicates)
        {
            var date = DateOnly.FromDateTime(reservation.ReservationDateTime);
            var time = TimeOnly.FromDateTime(reservation.ReservationDateTime);

            var duplicate = _reservationsCreated.FirstOrDefault(r =>
                DateOnly.FromDateTime(r.ReservationDateTime) == date &&
                TimeOnly.FromDateTime(r.ReservationDateTime) == time);

            if (duplicate != null)
                throw new Exception($"Horario duplicado: ya existe reserva para {date:yyyy-MM-dd} {time:HH:mm}");
        }

        var dto = new ReservationDto
        {
            ReservationId       = Guid.NewGuid(),
            BusinessId          = reservation.BusinessId,
            ServiceId           = reservation.ServiceId,
            EmployeeId          = reservation.EmployeeId,
            ReservationDateTime = reservation.ReservationDateTime,
            DurationMinutes     = reservation.DurationMinutes,
            Status              = ReservationStatus.Pending,
            CreatedAt           = DateTime.UtcNow
        };

        _reservationsCreated.Add(dto);
        return Task.FromResult(dto);
    }

    public Task<ReservationDto?> GetReservationByIdAsync(Guid reservationId) =>
        Task.FromResult(_reservationsCreated.FirstOrDefault(r => r.ReservationId == reservationId));

    public Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_reservationsCreated.Where(r => r.BusinessId == businessId));

    public Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAndDateRangeAsync(
        Guid businessId, DateTime startDate, DateTime endDate) =>
        Task.FromResult(_reservationsCreated.Where(r =>
            r.BusinessId == businessId &&
            r.ReservationDateTime >= startDate &&
            r.ReservationDateTime <= endDate));
}
