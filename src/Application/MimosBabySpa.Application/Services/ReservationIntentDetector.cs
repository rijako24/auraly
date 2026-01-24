using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación de detección de intención de reserva usando NLP ligero.
/// Utiliza palabras clave, patrones y contexto para determinar si el usuario realmente quiere reservar.
/// </summary>
public class ReservationIntentDetector : IReservationIntentDetector
{
    private readonly ILogger<ReservationIntentDetector> _logger;

    // Palabras clave que indican intención de reserva (positivas)
    private static readonly string[] ReservationKeywords = new[]
    {
        "reservar", "reserva", "reservación", "reservacion",
        "agendar", "agenda", "cita", "citas",
        "disponible", "disponibilidad", "disponibles",
        "quiero", "me gustaría", "me interesa", "quisiera",
        "confirmar", "confirmo", "confirmación",
        "puedo", "podría", "podrías",
        "tengo", "tendría",
        "necesito", "necesitaría",
        "plan", "planes", "servicio", "servicios",
        "clase", "clases",
        "horario", "horarios", "hora", "hora",
        "fecha", "fechas",
        "mañana", "pasado mañana", "lunes", "martes", "miércoles", "miercoles",
        "jueves", "viernes", "sábado", "sabado", "domingo"
    };

    // Palabras clave que indican NO intención de reserva (negativas)
    private static readonly string[] NonReservationKeywords = new[]
    {
        "viajo", "viajar", "viaje", "viajando",
        "llegar", "llegando", "llegada",
        "salir", "saliendo", "salida",
        "ir", "iré", "ire", "voy",
        "estoy", "estar",
        "cuando", "cuándo",
        "información", "info", "información sobre",
        "precio", "precios", "cuesta", "cuestan", "vale", "valen",
        "qué", "que", "cómo", "como"
    };

    // Patrones que indican intención de reserva
    private static readonly Regex[] ReservationPatterns = new[]
    {
        new Regex(@"\b(quiero|quisiera|me gustaría|me interesa)\s+(reservar|agendar|hacer una cita)", RegexOptions.IgnoreCase),
        new Regex(@"\b(reservar|agendar|hacer una cita)\s+(para|el|la|un|una)", RegexOptions.IgnoreCase),
        new Regex(@"\b(está|están|hay)\s+(disponible|disponibles)", RegexOptions.IgnoreCase),
        new Regex(@"\b(puedo|podría|podrías)\s+(reservar|agendar|hacer una cita)", RegexOptions.IgnoreCase),
        new Regex(@"\b(confirmo|confirmar)\s+(la\s+)?(reserva|reservación|cita)", RegexOptions.IgnoreCase),
        new Regex(@"\b(necesito|necesitaría)\s+(reservar|agendar|una cita)", RegexOptions.IgnoreCase),
        new Regex(@"\b(tengo|tendría)\s+(disponible|tiempo)", RegexOptions.IgnoreCase),
        new Regex(@"\b(para|el|la)\s+(lunes|martes|miércoles|miercoles|jueves|viernes|sábado|sabado|domingo|mañana)", RegexOptions.IgnoreCase),
    };

    // Patrones que indican NO intención (solo menciona fecha sin contexto de reserva)
    private static readonly Regex[] NonReservationPatterns = new[]
    {
        new Regex(@"\b(viajo|viajar|viaje)\s+(el|para el|el día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(llegar|llegando)\s+(el|para el|el día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(salir|saliendo)\s+(el|para el|el día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(ir|iré|voy)\s+(el|para el|el día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(cuando|cuándo)\s+(viajo|llegar|salir|ir)", RegexOptions.IgnoreCase),
        new Regex(@"\b(información|info|precio|precios)\s+(sobre|de|del)", RegexOptions.IgnoreCase),
    };

    public ReservationIntentDetector(ILogger<ReservationIntentDetector> logger)
    {
        _logger = logger;
    }

    public bool HasReservationIntent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _logger.LogDebug("Mensaje vacío, no hay intención de reserva");
            return false;
        }

        var normalizedMessage = message.ToLowerInvariant().Trim();

        // 1. Verificar patrones negativos primero (más específicos)
        foreach (var pattern in NonReservationPatterns)
        {
            if (pattern.IsMatch(normalizedMessage))
            {
                _logger.LogDebug("Patrón negativo detectado: {Pattern}. No hay intención de reserva", pattern);
                return false;
            }
        }

        // 2. Verificar patrones positivos (intención explícita)
        foreach (var pattern in ReservationPatterns)
        {
            if (pattern.IsMatch(normalizedMessage))
            {
                _logger.LogDebug("Patrón positivo detectado: {Pattern}. Hay intención de reserva", pattern);
                return true;
            }
        }

        // 3. Verificar palabras clave negativas (contexto de viaje/información)
        var hasNonReservationContext = NonReservationKeywords.Any(keyword => 
            normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        if (hasNonReservationContext)
        {
            // Si tiene contexto negativo pero también tiene palabras de reserva, verificar más
            var hasReservationKeywords = ReservationKeywords.Any(keyword =>
                normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (!hasReservationKeywords)
            {
                _logger.LogDebug("Contexto negativo sin palabras de reserva. No hay intención");
                return false;
            }
        }

        // 4. Verificar palabras clave positivas (intención de reserva)
        var reservationKeywordCount = ReservationKeywords.Count(keyword =>
            normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        // Si tiene múltiples palabras clave de reserva, es más probable que haya intención
        if (reservationKeywordCount >= 2)
        {
            _logger.LogDebug("Múltiples palabras clave de reserva detectadas ({Count}). Hay intención", reservationKeywordCount);
            return true;
        }

        // 5. Verificar si tiene al menos una palabra clave de reserva Y no tiene contexto negativo
        if (reservationKeywordCount >= 1 && !hasNonReservationContext)
        {
            _logger.LogDebug("Palabra clave de reserva sin contexto negativo. Hay intención");
            return true;
        }

        // 6. Si solo tiene una palabra clave pero también tiene contexto negativo, no hay intención
        if (reservationKeywordCount >= 1 && hasNonReservationContext)
        {
            _logger.LogDebug("Palabra clave de reserva pero con contexto negativo. No hay intención");
            return false;
        }

        _logger.LogDebug("No se detectó intención clara de reserva");
        return false;
    }
}
