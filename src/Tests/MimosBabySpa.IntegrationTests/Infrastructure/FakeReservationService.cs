using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;

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

    public Task<CreateReservationResponse> CreateReservationAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_mode == ReservationMode.AlwaysFail)
            throw new Exception("Fallo al crear reserva en el backend");

        var reservationDateTime = request.Date.ToDateTime(request.Time);

        if (_mode == ReservationMode.TrackDuplicates)
        {
            var duplicate = _reservationsCreated.FirstOrDefault(r =>
                DateOnly.FromDateTime(r.ReservationDateTime) == request.Date &&
                TimeOnly.FromDateTime(r.ReservationDateTime) == request.Time);

            if (duplicate != null)
                throw new Exception($"Horario duplicado: ya existe reserva para {request.Date:yyyy-MM-dd} {request.Time:HH:mm}");
        }

        var reservationId = Guid.NewGuid();
        var addOnNames = ParseAddOnNames(request.SelectedAddOnsCsv);

        var dto = new ReservationDto
        {
            ReservationId = reservationId,
            BusinessId = request.BusinessId,
            ServiceId = Guid.Empty,
            EmployeeId = Guid.Empty,
            ServiceName = request.ServiceName,
            EmployeeName = "Fake Employee",
            ReservationDateTime = reservationDateTime,
            DurationMinutes = 60,
            Status = ReservationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _reservationsCreated.Add(dto);

        var response = new CreateReservationResponse(
            reservationId,
            request.ServiceName,
            "Fake Employee",
            request.Date,
            request.Time,
            60,
            addOnNames);

        return Task.FromResult(response);
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

    private static IReadOnlyList<string> ParseAddOnNames(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            return [];

        return csv
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();
    }
}
