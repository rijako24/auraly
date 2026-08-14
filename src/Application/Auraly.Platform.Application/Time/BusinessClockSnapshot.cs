namespace Auraly.Platform.Application.Time;

/// <summary>
/// Instantánea del reloj del negocio en su zona horaria.
/// Fuente única de "hoy" para prompts, tools y disponibilidad.
/// </summary>
public sealed record BusinessClockSnapshot(
    Guid BusinessId,
    DateTimeOffset Now,
    DateOnly Today,
    TimeZoneInfo TimeZone);
