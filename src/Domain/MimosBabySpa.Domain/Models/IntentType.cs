namespace MimosBabySpa.Domain.Models;

/// <summary>
/// Tipos de intención detectados por el sistema de detección de intención.
/// </summary>
public enum IntentType
{
    /// <summary>
    /// Intención desconocida o no clasificada.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// El usuario solicita información general sobre servicios, precios, horarios, etc.
    /// </summary>
    Information = 1,

    /// <summary>
    /// El usuario quiere explorar disponibilidad para una fecha/hora específica.
    /// </summary>
    ExploreAvailability = 2,

    /// <summary>
    /// El usuario está proporcionando datos personales (nombre, teléfono, edad del bebé, etc.).
    /// </summary>
    ProvideData = 3,

    /// <summary>
    /// El usuario confirma explícitamente que quiere hacer una reserva.
    /// </summary>
    ReservationConfirmation = 4,

    /// <summary>
    /// El usuario expresa dudas, objeciones o preocupaciones.
    /// </summary>
    Objection = 5,

    /// <summary>
    /// Conversación casual, saludos, agradecimientos, etc.
    /// </summary>
    SmallTalk = 6
}
