namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resultado determinístico de verificación de disponibilidad.
/// El modelo NO debe inferir esto, solo usar estos valores.
/// </summary>
public class AvailabilityResult
{
    public bool IsAvailable { get; set; }
    public int CurrentReservations { get; set; }
    public List<BookedSlot> BookedSlots { get; set; } = new();
    public List<BookedSlot> OverlappingSlots { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}
