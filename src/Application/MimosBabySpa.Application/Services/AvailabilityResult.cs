namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resultado deterministico de verificacion de disponibilidad.
/// Expone ventanas libres y opciones reservables con inicio/fin; no expone slots sueltos.
/// </summary>
public class AvailabilityResult
{
    public bool IsAvailable { get; set; }
    public int CurrentReservations { get; set; }

    /// <summary>Ventanas libres reales donde podria caber el servicio segun politica y agenda.</summary>
    public List<AvailabilityWindow> AvailableWindows { get; set; } = new();

    /// <summary>Opciones concretas de reserva. Cada opcion ya considera duracion del servicio y buffer.</summary>
    public List<AvailabilityOption> AvailableOptions { get; set; } = new();

    /// <summary>Opcion validada cuando la consulta especifica hora y esta disponible.</summary>
    public AvailabilityOption? Option { get; set; }

    /// <summary>Intervalo solicitado cuando la consulta especifica hora.</summary>
    public AvailabilityOption? RequestedOption { get; set; }

    /// <summary>Resumen de estado para tools.</summary>
    public string ResponseMessage { get; set; } = string.Empty;

    /// <summary>Nombre del servicio consultado.</summary>
    public string RequestServiceName { get; set; } = string.Empty;

    /// <summary>Fecha consultada en formato YYYY-MM-DD.</summary>
    public string RequestDateString { get; set; } = string.Empty;

    /// <summary>Hora consultada (HH:mm) o null si no se indico.</summary>
    public string? RequestTimeString { get; set; }
}

public sealed record AvailabilityWindow(string Start, string End);

public sealed record AvailabilityOption(string Start, string End);

internal readonly record struct AvailabilityWindowRange(TimeSpan Start, TimeSpan End);

internal readonly record struct AvailabilityOptionRange(TimeSpan Start, TimeSpan End);