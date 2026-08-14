namespace Auraly.Platform.Application.DTOs;

public sealed record UpdateReservationChangeRequest(
    Guid ReservationId,
    string? ServiceName,
    DateOnly? Date,
    TimeOnly? Time,
    string? AddOnsCsv,
    string? AddOnsMode,
    bool Apply);

public sealed record UpdateReservationChangeResult(
    bool Success,
    string? ErrorCode,
    string? ErrorMessage,
    string? Remediation,
    Guid ReservationId,
    string ServiceName,
    DateOnly? Date,
    TimeOnly? Time,
    string? EmployeeName,
    int? DurationMinutes,
    IReadOnlyList<string> AddOns,
    decimal NewTotal,
    string PaymentPolicy,
    bool Applied)
{
    public static UpdateReservationChangeResult Fail(
        string code,
        string message,
        string? remediation,
        Guid reservationId) =>
        new(false, code, message, remediation, reservationId, string.Empty, null, null, null, null, [], 0m, string.Empty, false);
}
