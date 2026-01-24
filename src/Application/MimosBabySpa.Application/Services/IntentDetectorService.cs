using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Models;
using MimosBabySpa.Domain.Models;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación del detector de intención con reglas determinísticas, heurísticas y fallback controlado a IA.
/// </summary>
public class IntentDetectorService : IIntentDetectorService
{
    private readonly IDateTimeExtractorService _dateTimeExtractor;
    private readonly IReservationIntentDetector _reservationIntentDetector;
    private readonly ILogger<IntentDetectorService> _logger;

    // Verbos narrativos que indican que una fecha es narrativa, no para reserva
    private static readonly string[] NarrativeVerbs = new[]
    {
        "viajar", "viajo", "viajaré", "viajare", "viajando",
        "salir", "salgo", "saldré", "saldre", "saliendo",
        "llegar", "llegó", "llegare", "llegaré", "llegando",
        "volver", "vuelvo", "volveré", "volvere", "volviendo",
        "estar", "estoy", "estaré", "estare", "estando",
        "pasar", "paso", "pasaré", "pasare", "pasando",
        "ir", "voy", "iré", "ire", "yendo"
    };

    // Palabras fuertes de confirmación explícita de reserva
    private static readonly string[] ExplicitConfirmationKeywords = new[]
    {
        "reservar", "reserva", "reservación", "reservacion",
        "agendar", "agenda", "agendamos",
        "confirmar", "confirmo", "confirmación", "confirmacion",
        "hacer cita", "hacer una cita", "hacer la cita",
        "apartar", "apartamos", "apartar la",
        "quiero reservar", "quiero agendar", "quiero hacer",
        "me gustaría reservar", "me gustaria reservar",
        "quisiera reservar", "quisiera agendar"
    };

    // Patrones para detectar confirmación explícita
    private static readonly Regex[] ExplicitConfirmationPatterns = new[]
    {
        new Regex(@"\b(quiero|quisiera|me gustaría|me gustaria)\s+(reservar|agendar|hacer una cita)", RegexOptions.IgnoreCase),
        new Regex(@"\b(reservar|agendar|hacer una cita)\s+(para|el|la|un|una)", RegexOptions.IgnoreCase),
        new Regex(@"\b(confirmo|confirmar)\s+(la\s+)?(reserva|reservación|cita)", RegexOptions.IgnoreCase),
        new Regex(@"\b(sí|si)\s*,?\s*(quiero|confirma|reserva|agenda)", RegexOptions.IgnoreCase),
        new Regex(@"\b(perfecto|ok|okay|de acuerdo)\s*,?\s*(reserva|agenda|confirma)", RegexOptions.IgnoreCase),
    };

    // Patrones para detectar fechas narrativas
    private static readonly Regex[] NarrativeDatePatterns = new[]
    {
        new Regex(@"\b(viajo|viajar|viaje|viajaré|viajare)\s+(el|para el|el día|día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(llegar|llegando|llegó|llegare|llegaré)\s+(el|para el|el día|día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(salir|saliendo|salgo|saldré|saldre)\s+(el|para el|el día|día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(ir|iré|ire|voy)\s+(el|para el|el día|día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(volver|volveré|volvere|vuelvo)\s+(el|para el|el día|día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(estar|estaré|estare|estoy)\s+(el|para el|el día|día)", RegexOptions.IgnoreCase),
        new Regex(@"\b(pasar|pasaré|pasare|paso)\s+(el|para el|el día|día)", RegexOptions.IgnoreCase),
    };

    public IntentDetectorService(
        IDateTimeExtractorService dateTimeExtractor,
        IReservationIntentDetector reservationIntentDetector,
        ILogger<IntentDetectorService> logger)
    {
        _dateTimeExtractor = dateTimeExtractor;
        _reservationIntentDetector = reservationIntentDetector;
        _logger = logger;
    }

    public IntentDetectionResult Detect(string userMessage, ConversationState state)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            _logger.LogDebug("Mensaje vacío, retornando Unknown");
            return new IntentDetectionResult { Intent = IntentType.Unknown };
        }

        var normalizedMessage = userMessage.ToLowerInvariant().Trim();
        var result = new IntentDetectionResult();

        // PASO 1: Detectar fecha y hora
        var extractedDate = _dateTimeExtractor.ExtractDate(userMessage);
        var extractedTime = _dateTimeExtractor.ExtractTime(userMessage);

        result.HasDate = extractedDate.HasValue;
        result.HasTime = extractedTime.HasValue;
        result.DetectedDateRaw = extractedDate?.ToString("yyyy-MM-dd");
        result.DetectedTimeRaw = extractedTime?.ToString(@"hh\:mm");

        // PASO 2: Detectar si la fecha es narrativa (regla determinística)
        result.IsNarrativeDate = DetectNarrativeDate(normalizedMessage, extractedDate.HasValue);
        
        // Si es narrativa, invalidar la fecha para reservas
        if (result.IsNarrativeDate)
        {
            result.HasDate = false;
            _logger.LogDebug("Fecha narrativa detectada, invalidando fecha para reservas");
        }

        // PASO 3: Detectar confirmación explícita (regla determinística)
        result.IsExplicitConfirmation = DetectExplicitConfirmation(normalizedMessage);

        // PASO 4: Verificar estado de conversación (heurísticas)
        result.HasService = !string.IsNullOrWhiteSpace(state.PrimaryEntity);

        // PASO 5: Clasificar intención (reglas determinísticas primero, luego fallback a IA si es necesario)
        result.Intent = ClassifyIntent(normalizedMessage, result, state);

        // PASO 6: Determinar si se debe verificar disponibilidad
        result.ShouldCheckAvailability = DetermineShouldCheckAvailability(result, state);

        // PASO 7: Determinar si se debe permitir reserva (regla crítica)
        result.ShouldAllowReservation = DetermineShouldAllowReservation(result, state);

        _logger.LogInformation(
            "Intención detectada: {Intent}, HasDate={HasDate}, IsNarrative={IsNarrative}, " +
            "IsExplicitConfirmation={IsExplicit}, ShouldCheckAvailability={ShouldCheck}, ShouldAllowReservation={ShouldAllow}",
            result.Intent, result.HasDate, result.IsNarrativeDate, result.IsExplicitConfirmation,
            result.ShouldCheckAvailability, result.ShouldAllowReservation);

        return result;
    }

    /// <summary>
    /// Detecta si una fecha es narrativa (ej: "el sábado viajo") y no debe usarse para reservas.
    /// </summary>
    private bool DetectNarrativeDate(string normalizedMessage, bool hasDate)
    {
        if (!hasDate)
            return false;

        // Verificar patrones de fecha narrativa
        foreach (var pattern in NarrativeDatePatterns)
        {
            if (pattern.IsMatch(normalizedMessage))
            {
                _logger.LogDebug("Patrón narrativo detectado: {Pattern}", pattern);
                return true;
            }
        }

        // Verificar si hay verbos narrativos cerca de palabras de fecha
        var dateKeywords = new[] { "lunes", "martes", "miércoles", "miercoles", "jueves", "viernes", "sábado", "sabado", "domingo", "mañana", "pasado mañana" };
        
        foreach (var dateKeyword in dateKeywords)
        {
            if (normalizedMessage.Contains(dateKeyword))
            {
                // Buscar verbos narrativos en un rango de 50 caracteres alrededor de la fecha
                var dateIndex = normalizedMessage.IndexOf(dateKeyword);
                var startIndex = Math.Max(0, dateIndex - 50);
                var endIndex = Math.Min(normalizedMessage.Length, dateIndex + dateKeyword.Length + 50);
                var contextAroundDate = normalizedMessage.Substring(startIndex, endIndex - startIndex);

                foreach (var verb in NarrativeVerbs)
                {
                    if (contextAroundDate.Contains(verb))
                    {
                        _logger.LogDebug("Verbo narrativo '{Verb}' detectado cerca de fecha '{Date}'", verb, dateKeyword);
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Detecta si el usuario hizo una confirmación explícita de reserva.
    /// </summary>
    private bool DetectExplicitConfirmation(string normalizedMessage)
    {
        // Verificar patrones de confirmación explícita
        foreach (var pattern in ExplicitConfirmationPatterns)
        {
            if (pattern.IsMatch(normalizedMessage))
            {
                _logger.LogDebug("Patrón de confirmación explícita detectado: {Pattern}", pattern);
                return true;
            }
        }

        // Verificar palabras clave de confirmación
        foreach (var keyword in ExplicitConfirmationKeywords)
        {
            if (normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Palabra clave de confirmación detectada: {Keyword}", keyword);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Clasifica la intención del usuario usando reglas determinísticas primero, luego fallback a IA si es necesario.
    /// </summary>
    private IntentType ClassifyIntent(string normalizedMessage, IntentDetectionResult partialResult, ConversationState state)
    {
        // Si hay confirmación explícita, es ReservationConfirmation
        if (partialResult.IsExplicitConfirmation)
        {
            return IntentType.ReservationConfirmation;
        }

        // Si hay fecha válida (no narrativa) y servicio, probablemente es ExploreAvailability
        if (partialResult.HasDate && !partialResult.IsNarrativeDate && partialResult.HasService)
        {
            return IntentType.ExploreAvailability;
        }

        // Si hay fecha válida pero no servicio, podría ser Information o ExploreAvailability
        if (partialResult.HasDate && !partialResult.IsNarrativeDate)
        {
            // Usar el detector de intención de reserva existente como heurística
            if (_reservationIntentDetector.HasReservationIntent(normalizedMessage))
            {
                return IntentType.ExploreAvailability;
            }
        }

        // Detectar si está proporcionando datos
        if (DetectProvideDataIntent(normalizedMessage))
        {
            return IntentType.ProvideData;
        }

        // Detectar si es información
        if (DetectInformationIntent(normalizedMessage))
        {
            return IntentType.Information;
        }

        // Detectar si es objeción
        if (DetectObjectionIntent(normalizedMessage))
        {
            return IntentType.Objection;
        }

        // Detectar si es small talk
        if (DetectSmallTalkIntent(normalizedMessage))
        {
            return IntentType.SmallTalk;
        }

        // Si no se puede clasificar con reglas determinísticas, retornar Unknown
        // El fallback a IA se puede hacer después si es necesario, pero por ahora retornamos Unknown
        return IntentType.Unknown;
    }

    private bool DetectProvideDataIntent(string normalizedMessage)
    {
        var provideDataKeywords = new[] { "me llamo", "mi nombre", "soy", "tengo", "mi bebé tiene", "tiene", "meses", "año", "años", "teléfono", "telefono", "celular" };
        return provideDataKeywords.Any(keyword => normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private bool DetectInformationIntent(string normalizedMessage)
    {
        var informationKeywords = new[] { "qué", "que", "cómo", "como", "cuánto", "cuanto", "precio", "precios", "cuesta", "vale", "información", "info", "horarios", "horario", "servicios", "servicio" };
        return informationKeywords.Any(keyword => normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private bool DetectObjectionIntent(string normalizedMessage)
    {
        var objectionKeywords = new[] { "caro", "cara", "caros", "caras", "miedo", "miedos", "preocupación", "preocupacion", "duda", "dudas", "no estoy seguro", "no estoy segura", "no sé", "no se" };
        return objectionKeywords.Any(keyword => normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    private bool DetectSmallTalkIntent(string normalizedMessage)
    {
        var smallTalkKeywords = new[] { "hola", "buenos días", "buenos dias", "buenas tardes", "buenas noches", "gracias", "muchas gracias", "ok", "okay", "perfecto", "genial", "excelente" };
        return smallTalkKeywords.Any(keyword => normalizedMessage.Contains(keyword, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Determina si se debe verificar disponibilidad en el backend.
    /// </summary>
    private bool DetermineShouldCheckAvailability(IntentDetectionResult result, ConversationState state)
    {
        // NO verificar disponibilidad si:
        // 1. La fecha es narrativa
        if (result.IsNarrativeDate)
        {
            return false;
        }

        // 2. No hay fecha válida
        if (!result.HasDate)
        {
            return false;
        }

        // 3. No hay servicio definido
        if (!result.HasService)
        {
            return false;
        }

        // 4. Ya se verificó disponibilidad para esta fecha/hora específica
        if (state.LastAvailabilityResult.HasValue && 
            state.LastAvailabilityCheckAt.HasValue &&
            result.DetectedDateRaw == state.LastAvailabilityCheckAt.Value.ToString("yyyy-MM-dd"))
        {
            // Si también hay hora y coincide, no verificar de nuevo
            if (result.HasTime && state.DesiredTime.HasValue)
            {
                var detectedTime = TimeSpan.Parse(result.DetectedTimeRaw ?? "00:00");
                var stateTime = state.DesiredTime.Value.ToTimeSpan();
                if (Math.Abs((detectedTime - stateTime).TotalMinutes) < 5)
                {
                    _logger.LogDebug("Disponibilidad ya verificada para esta fecha/hora, no se verificará de nuevo");
                    return false;
                }
            }
        }

        // Si llegamos aquí, se debe verificar disponibilidad
        return true;
    }

    /// <summary>
    /// Determina si se debe permitir crear una reserva.
    /// REGLA CRÍTICA: Solo permite reserva si se cumplen TODAS las condiciones.
    /// </summary>
    private bool DetermineShouldAllowReservation(IntentDetectionResult result, ConversationState state)
    {
        // CONDICIÓN 1: Debe haber confirmación explícita
        if (!result.IsExplicitConfirmation)
        {
            _logger.LogDebug("No hay confirmación explícita, no se permite reserva");
            return false;
        }

        // CONDICIÓN 2: Debe haber servicio en estado
        if (!result.HasService || string.IsNullOrWhiteSpace(state.PrimaryEntity))
        {
            _logger.LogDebug("No hay servicio en estado, no se permite reserva");
            return false;
        }

        // CONDICIÓN 3: Debe haber fecha válida (no narrativa)
        if (!result.HasDate || result.IsNarrativeDate)
        {
            _logger.LogDebug("No hay fecha válida o es narrativa, no se permite reserva");
            return false;
        }

        // CONDICIÓN 4: Debe haber hora
        if (!result.HasTime && !state.DesiredTime.HasValue)
        {
            _logger.LogDebug("No hay hora, no se permite reserva");
            return false;
        }

        // CONDICIÓN 5: Debe haber disponibilidad positiva previa
        if (!state.LastAvailabilityResult.HasValue || state.LastAvailabilityResult.Value != true)
        {
            _logger.LogDebug("No hay disponibilidad positiva previa, no se permite reserva");
            return false;
        }

        // Si llegamos aquí, se permite reserva
        _logger.LogInformation("Todas las condiciones cumplidas, se permite crear reserva");
        return true;
    }
}
