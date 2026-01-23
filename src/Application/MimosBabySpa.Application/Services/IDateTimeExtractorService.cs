namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para extraer fecha y hora de mensajes del usuario.
/// Permite detección manual desde backend en lugar de dejar que el modelo decida.
/// </summary>
public interface IDateTimeExtractorService
{
    /// <summary>
    /// Intenta extraer una fecha del mensaje del usuario.
    /// Retorna null si no se encuentra una fecha válida.
    /// </summary>
    DateTime? ExtractDate(string message);
    
    /// <summary>
    /// Intenta extraer una hora del mensaje del usuario.
    /// Retorna null si no se encuentra una hora válida.
    /// </summary>
    TimeSpan? ExtractTime(string message);
    
    /// <summary>
    /// Detecta si el mensaje menciona una fecha u hora.
    /// </summary>
    bool ContainsDateTime(string message);
}
