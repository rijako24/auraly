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

    // Verbos narrativos que indican que una fecha no es para reserva
    private static readonly string[] NarrativeVerbs = new[]
    {
        "viajo", "viajar", "viajamos", "viajé", "viajare",
        "salgo", "salir", "salimos", "salí", "saldré",
        "trabajo", "trabajar", "trabajamos", "trabajé",
        "estudio", "estudiar", "estudiamos", "estudié",
        "tengo", "tenemos", "tendré", "tendremos",
        "voy", "ir", "vamos", "fui", "iré", "iremos"
    };

    // Patrones para detectar fechas narrativas
    private static readonly Regex[] NarrativeDatePatterns = new[]
    {
        new Regex(@"\b(el|la|los|las)\s+(lunes|martes|miércoles|miercoles|jueves|viernes|sábado|sabado|domingo|mañana|pasado mañana)\s+(viajo|viajar|salgo|salir|trabajo|trabajar|estudio|estudiar|tengo|voy|ir)", RegexOptions.IgnoreCase),
        new Regex(@"\b(viajo|viajar|salgo|salir|trabajo|trabajar|estudio|estudiar|tengo|voy|ir)\s+(el|la|los|las)?\s*(lunes|martes|miércoles|miercoles|jueves|viernes|sábado|sabado|domingo|mañana|pasado mañana)", RegexOptions.IgnoreCase),
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

        // Si el estado es null, crear uno vacío
        if (state == null)
        {
            _logger.LogWarning("Estado de conversación es null, creando estado vacío");
            state = new ConversationState();
        }

        var normalizedMessage = userMessage.ToLowerInvariant().Trim();
        var result = new IntentDetectionResult();

        // TODO: Toda la información debe venir del estado (contexto), no del mensaje
        // El mensaje solo se usa para detectar información nueva que pueda actualizar el estado

        // PASO 1: Usar fecha y hora del estado (contexto)
        result.HasDate = state.DesiredDate.HasValue;
        result.HasTime = state.DesiredTime.HasValue;
        result.DetectedDateRaw = state.DesiredDate?.ToString("yyyy-MM-dd");
        result.DetectedTimeRaw = state.DesiredTime?.ToString(@"hh\:mm");

        // PASO 2: Detectar confirmación explícita SOLO del mensaje (el estado se actualiza después)
        result.IsExplicitConfirmation = DetectExplicitConfirmation(normalizedMessage);

        // PASO 3: Clasificar intención basándose en el estado y el mensaje
        result.Intent = ClassifyIntent(normalizedMessage, result, state);

        // PASO 5: Determinar si se debe verificar disponibilidad (basado en estado y validación de fecha narrativa)
        result.ShouldCheckAvailability = DetermineShouldCheckAvailability(result, state, normalizedMessage);

        // PASO 6: Determinar si se debe permitir reserva (basado en estado)
        result.ShouldAllowReservation = DetermineShouldAllowReservation(result, state);

        _logger.LogInformation(
            "Intención detectada: {Intent}, HasDate={HasDate}, " +
            "IsExplicitConfirmation={IsExplicit}, ShouldCheckAvailability={ShouldCheck}, ShouldAllowReservation={ShouldAllow}",
            result.Intent, result.HasDate, result.IsExplicitConfirmation,
            result.ShouldCheckAvailability, result.ShouldAllowReservation);

        return result;
    }

    /// <summary>
    /// Evalúa si se permite crear una reserva basándose únicamente en el estado actual.
    /// Útil cuando se recalcula la intención después de actualizar el contexto.
    /// </summary>
    public IntentDetectionResult EvaluateFromState(ConversationState state)
    {
        var result = new IntentDetectionResult();

        // Usar información del estado directamente
        result.HasDate = state.DesiredDate.HasValue;
        result.HasTime = state.DesiredTime.HasValue;
        result.DetectedDateRaw = state.DesiredDate?.ToString("yyyy-MM-dd");
        result.DetectedTimeRaw = state.DesiredTime?.ToString(@"hh\:mm");
        result.IsExplicitConfirmation = false; // No hay mensaje, solo estado

        // Clasificar intención basándose en el estado (sin confirmación porque no hay mensaje)
        if (result.HasDate)
        {
            result.Intent = IntentType.ExploreAvailability;
        }
        else
        {
            result.Intent = state.CurrentIntent ?? IntentType.Unknown;
        }

        // Determinar si se debe verificar disponibilidad (sin mensaje, no hay validación narrativa)
        result.ShouldCheckAvailability = DetermineShouldCheckAvailability(result, state, null);

        // Determinar si se debe permitir reserva (regla crítica)
        result.ShouldAllowReservation = DetermineShouldAllowReservation(result, state);

        _logger.LogInformation(
            "Evaluación desde estado: {Intent}, HasDate={HasDate}, " +
            "IsExplicitConfirmation={IsExplicit}, ShouldCheckAvailability={ShouldCheck}, ShouldAllowReservation={ShouldAllow}",
            result.Intent, result.HasDate, result.IsExplicitConfirmation,
            result.ShouldCheckAvailability, result.ShouldAllowReservation);

        return result;
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
    /// Clasifica la intención del usuario usando reglas determinísticas primero, luego fallback a IA si es necesario.
    /// </summary>
    private IntentType ClassifyIntent(string normalizedMessage, IntentDetectionResult partialResult, ConversationState state)
    {
        // Si hay confirmación explícita, es ReservationConfirmation
        if (partialResult.IsExplicitConfirmation)
        {
            return IntentType.ReservationConfirmation;
        }

        // Si hay fecha válida, probablemente es ExploreAvailability
        if (partialResult.HasDate)
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
    /// Usa solo el estado (contexto) como fuente de verdad.
    /// Valida fechas narrativas solo para disponibilidad.
    /// </summary>
    private bool DetermineShouldCheckAvailability(IntentDetectionResult result, ConversationState state, string? normalizedMessage = null)
    {
        // NO verificar disponibilidad si:
        // 1. No hay fecha válida en el estado
        if (!state.DesiredDate.HasValue)
        {
            return false;
        }

        // 2. El servicio se obtiene de las tools (check_availability o create_reservation), no del estado
        // Por lo tanto, siempre se puede verificar disponibilidad si hay fecha

        // 3. Validar si hay fecha narrativa en el mensaje (solo para disponibilidad)
        // Solo validar si hay fecha en el estado Y hay fecha en el mensaje
        if (!string.IsNullOrWhiteSpace(normalizedMessage) && result.HasDate)
        {
            var extractedDate = _dateTimeExtractor.ExtractDate(normalizedMessage);
            // Solo validar fecha narrativa si realmente hay una fecha en el mensaje
            if (extractedDate.HasValue && DetectNarrativeDate(normalizedMessage, extractedDate.HasValue))
            {
                _logger.LogDebug("Fecha narrativa detectada en mensaje, no se verificará disponibilidad");
                return false;
            }
        }

        // 4. Ya se verificó disponibilidad para esta fecha/hora específica
        if (state.LastAvailabilityResult.HasValue && 
            state.LastAvailabilityCheckAt.HasValue &&
            state.DesiredDate.Value.ToString("yyyy-MM-dd") == state.LastAvailabilityCheckAt.Value.ToString("yyyy-MM-dd"))
        {
            // Si también hay hora y coincide, no verificar de nuevo
            if (state.DesiredTime.HasValue)
            {
                var stateTime = state.DesiredTime.Value.ToTimeSpan();
                var lastCheckTime = state.LastAvailabilityCheckAt.Value.TimeOfDay;
                if (Math.Abs((stateTime - lastCheckTime).TotalMinutes) < 5)
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
    /// La confirmación explícita viene del mensaje (result.IsExplicitConfirmation), no del contexto.
    /// </summary>
    private bool DetermineShouldAllowReservation(IntentDetectionResult result, ConversationState state)
    {
        // Verificar todas las condiciones necesarias
        // Nota: El servicio se obtiene de las tools (check_availability o create_reservation), no del estado
        bool hasAllData =
            result.IsExplicitConfirmation && // Confirmación explícita del MENSAJE actual
            state.DesiredDate.HasValue && // Fecha del contexto
            state.DesiredTime.HasValue && // Hora del contexto
            state.LastAvailabilityResult == true;

        if (!hasAllData)
        {
            _logger.LogDebug(
                "No se permite reserva: IsExplicitConfirmation={Confirmed}, Date={Date}, Time={Time}, Availability={Availability}",
                result.IsExplicitConfirmation, 
                state.DesiredDate.HasValue, state.DesiredTime.HasValue, 
                state.LastAvailabilityResult == true);
            return false;
        }

        _logger.LogInformation("Todas las condiciones cumplidas, se permite crear reserva");
        return true;
    }
}
