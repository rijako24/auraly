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
                r.ReservationDateTime.HasValue &&
                DateOnly.FromDateTime(r.ReservationDateTime.Value) == request.Date &&
                TimeOnly.FromDateTime(r.ReservationDateTime.Value) == request.Time);

            if (duplicate != null)
                throw new Exception($"Horario duplicado: ya existe reserva para {request.Date:yyyy-MM-dd} {request.Time:HH:mm}");
        }

        var reservationId = Guid.NewGuid();
        var addOnNames = ParseAddOnNames(
            ReservationBusinessAttributeKeys.GetSelectedAddOnsCsv(request.BusinessAttributes));

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

    public Task<CreateReservationResponse> CreateFromIntentSnapshotAsync(
        Guid businessId,
        Guid conversationId,
        ReservationIntentSnapshot snapshot,
        DateTime reservationDateTime,
        CancellationToken cancellationToken = default) =>
        CreateReservationAsync(
            new CreateReservationRequest(
                businessId,
                conversationId,
                snapshot.ServiceName,
                DateOnly.FromDateTime(reservationDateTime),
                TimeOnly.FromDateTime(reservationDateTime),
                snapshot.CustomerName,
                snapshot.CustomerEmail,
                snapshot.CustomerPhone,
                new Dictionary<string, string>(),
                snapshot.CustomAttributesJson),
            cancellationToken);

    public Task<ReservationDto?> GetReservationByIdAsync(Guid reservationId) =>
        Task.FromResult(_reservationsCreated.FirstOrDefault(r => r.ReservationId == reservationId));

    public Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAsync(Guid businessId) =>
        Task.FromResult(_reservationsCreated.Where(r => r.BusinessId == businessId));

    public Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAndDateRangeAsync(
        Guid businessId, DateTime startDate, DateTime endDate) =>
        Task.FromResult(_reservationsCreated.Where(r =>
            r.BusinessId == businessId &&
            r.ReservationDateTime.HasValue &&
            r.ReservationDateTime.Value >= startDate &&
            r.ReservationDateTime.Value <= endDate));

    public Task<bool> SuspendAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        var r = _reservationsCreated.FirstOrDefault(x => x.ReservationId == reservationId);
        if (r != null) r.Status = ReservationStatus.OnHold;
        return Task.FromResult(r != null);
    }

    public Task<UpdateReservationChangeResult> UpdateReservationAsync(
        UpdateReservationChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var r = _reservationsCreated.FirstOrDefault(x => x.ReservationId == request.ReservationId);
        if (r is null)
        {
            return Task.FromResult(UpdateReservationChangeResult.Fail(
                "reservation_not_found",
                "Reservation was not found.",
                null,
                request.ReservationId));
        }

        var dateTime = r.ReservationDateTime;
        if (request.Date.HasValue || request.Time.HasValue)
        {
            var date = request.Date ?? DateOnly.FromDateTime(dateTime ?? DateTime.UtcNow);
            var time = request.Time ?? TimeOnly.FromDateTime(dateTime ?? DateTime.UtcNow);
            dateTime = date.ToDateTime(time);
        }

        var addOns = ParseAddOnNames(request.AddOnsCsv);
        if (request.Apply)
        {
            r.ServiceName = request.ServiceName ?? r.ServiceName;
            r.ReservationDateTime = dateTime;
            r.UpdatedAt = DateTime.UtcNow;
        }

        return Task.FromResult(new UpdateReservationChangeResult(
            true,
            null,
            null,
            null,
            request.ReservationId,
            request.ServiceName ?? r.ServiceName,
            dateTime.HasValue ? DateOnly.FromDateTime(dateTime.Value) : null,
            dateTime.HasValue ? TimeOnly.FromDateTime(dateTime.Value) : null,
            r.EmployeeName,
            r.DurationMinutes,
            addOns,
            0m,
            "No additional online payment is required for reservation changes after payment; any remaining balance is handled at the venue.",
            request.Apply));
    }

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
