namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para detectar si un mensaje tiene intención real de reservar o consultar disponibilidad.
/// Evita llamadas innecesarias a disponibilidad cuando solo se menciona una fecha sin intención.
/// </summary>
public interface IReservationIntentDetector
{
    /// <summary>
    /// Detecta si el mensaje tiene intención de reservar o consultar disponibilidad.
    /// Retorna true si el mensaje contiene señales claras de intención de reserva.
    /// </summary>
    /// <param name="message">Mensaje del usuario a analizar</param>
    /// <returns>true si hay intención de reserva, false en caso contrario</returns>
    bool HasReservationIntent(string message);
}
