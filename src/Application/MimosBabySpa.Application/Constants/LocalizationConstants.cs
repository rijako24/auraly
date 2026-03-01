namespace MimosBabySpa.Application.Constants;

/// <summary>
/// Constantes de localización centralizadas.
/// TODO: Migrar a sistema i18n completo (ILocalizationService) en el futuro.
/// </summary>
public static class LocalizationConstants
{
    /// <summary>
    /// Nombres de días de la semana en diferentes idiomas.
    /// </summary>
    public static class DayNames
    {
        /// <summary>
        /// Nombres de días en español.
        /// </summary>
        public static readonly Dictionary<string, string> Spanish = new()
        {
            ["monday"] = "Lunes",
            ["tuesday"] = "Martes",
            ["wednesday"] = "Miércoles",
            ["thursday"] = "Jueves",
            ["friday"] = "Viernes",
            ["saturday"] = "Sábado",
            ["sunday"] = "Domingo"
        };

        /// <summary>
        /// Nombres de días en inglés.
        /// </summary>
        public static readonly Dictionary<string, string> English = new()
        {
            ["monday"] = "Monday",
            ["tuesday"] = "Tuesday",
            ["wednesday"] = "Wednesday",
            ["thursday"] = "Thursday",
            ["friday"] = "Friday",
            ["saturday"] = "Saturday",
            ["sunday"] = "Sunday"
        };

        /// <summary>
        /// Obtiene el nombre del día en el idioma especificado.
        /// </summary>
        /// <param name="dayKey">Clave del día (ej: "monday")</param>
        /// <param name="language">Idioma ("es" o "en")</param>
        /// <returns>Nombre del día traducido, o la clave si no se encuentra</returns>
        public static string Get(string dayKey, string language = "es")
        {
            var dict = language.ToLower() switch
            {
                "es" => Spanish,
                "en" => English,
                _ => Spanish // Por defecto español
            };

            return dict.TryGetValue(dayKey.ToLower(), out var dayName) ? dayName : dayKey;
        }
    }

    /// <summary>
    /// Mensajes de error estándar del sistema.
    /// </summary>
    public static class ErrorMessages
    {
        /// <summary>
        /// Mensaje cuando hay dificultades técnicas en el procesamiento.
        /// </summary>
        public const string TechnicalDifficulty =
            "Disculpa, estoy teniendo dificultades técnicas. ¿Podrías repetir tu mensaje de forma más clara?";

        /// <summary>
        /// Mensaje cuando la extracción de información falla.
        /// </summary>
        public const string ExtractionFailed =
            "No pude entender completamente tu mensaje. ¿Podrías darme más detalles?";

        /// <summary>
        /// Mensaje cuando el servicio de IA no responde.
        /// </summary>
        public const string AIServiceUnavailable =
            "Lo siento, estoy experimentando problemas temporales. Por favor, intenta de nuevo en un momento.";

        /// <summary>
        /// Mensaje cuando hay un error de validación.
        /// </summary>
        public const string ValidationError =
            "Parece que hubo un problema con la información proporcionada. ¿Podrías verificarla?";
    }

    /// <summary>
    /// Mensajes de éxito del sistema.
    /// </summary>
    public static class SuccessMessages
    {
        public const string ReservationCreated = "¡Reserva creada exitosamente! 🎉";
        public const string InformationSaved = "Perfecto, he guardado tu información.";
        public const string AvailabilityChecked = "He verificado la disponibilidad.";
    }

    /// <summary>
    /// Mensajes de escalado a humano.
    /// </summary>
    public static class EscalationMessages
    {
        public const string PleaseRepeat =
            "Disculpa, estoy teniendo un inconveniente procesando tu mensaje. ¿Podrías repetirlo?";

        public const string TechnicalIssues =
            "Disculpa, estamos teniendo inconvenientes técnicos. Un asesor se comunicará contigo pronto para ayudarte.";

        public const string Redirect =
            "Gracias por contactarnos. Estamos redirigiendo tu conversación a un asesor que te atenderá personalmente muy pronto.";

        public const string ErrorRetry =
            "Disculpa, ha ocurrido un error. Por favor intenta nuevamente.";
    }

    /// <summary>
    /// Frases comunes del sistema.
    /// </summary>
    public static class CommonPhrases
    {
        public const string Greeting = "¡Hola! 😊";
        public const string ThankYou = "¡Gracias!";
        public const string YoureWelcome = "¡Con gusto!";
        public const string Goodbye = "¡Hasta pronto! 👋";
        public const string PleaseWait = "Un momento por favor...";
    }
}
