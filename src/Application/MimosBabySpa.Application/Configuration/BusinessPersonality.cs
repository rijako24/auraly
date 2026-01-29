namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de personalidad del asistente virtual del negocio.
/// Define el nombre, tono, estilo y ejemplos de frases.
/// </summary>
public class BusinessPersonality
{
    /// <summary>
    /// Nombre del asistente virtual.
    /// Ejemplo: "María", "Carlos", "Ana"
    /// </summary>
    public string AssistantName { get; set; } = "Asistente";

    /// <summary>
    /// Género del asistente (afecta el lenguaje usado).
    /// Valores: "Male", "Female", "Neutral"
    /// </summary>
    public string Gender { get; set; } = "Neutral";

    /// <summary>
    /// Lista de características del tono del asistente.
    /// Ejemplos: ["Cálido", "Profesional", "Empático", "Cercano", "Formal"]
    /// </summary>
    public List<string> Tone { get; set; } = new();

    /// <summary>
    /// Indica si el asistente debe usar emojis en sus respuestas.
    /// </summary>
    public bool UseEmojis { get; set; } = false;

    /// <summary>
    /// Estilo de saludo del asistente.
    /// Valores: "Formal", "Amigable", "Casual", "Profesional"
    /// </summary>
    public string GreetingStyle { get; set; } = "Amigable";

    /// <summary>
    /// Frases de ejemplo para diferentes contextos.
    /// Clave: contexto (ej: "Greeting", "Closing", "Thanking")
    /// Valor: frase de ejemplo con placeholders como {AssistantName}, {BusinessName}
    /// </summary>
    public Dictionary<string, string> SamplePhrases { get; set; } = new();

    /// <summary>
    /// Describe la especialización o expertise del asistente.
    /// Ejemplo: "experta en spa para bebés", "especialista en belleza"
    /// </summary>
    public string? Expertise { get; set; }

    /// <summary>
    /// Crea una personalidad por defecto genérica.
    /// </summary>
    public static BusinessPersonality Default()
    {
        return new BusinessPersonality
        {
            AssistantName = "Asistente",
            Gender = "Neutral",
            Tone = new List<string> { "Profesional", "Amable" },
            UseEmojis = false,
            GreetingStyle = "Profesional",
            SamplePhrases = new Dictionary<string, string>
            {
                ["Greeting"] = "Hola, soy {AssistantName}. ¿En qué puedo ayudarte?",
                ["Closing"] = "Estoy aquí para ayudarte en lo que necesites.",
                ["Thanking"] = "Gracias por tu tiempo."
            },
            Expertise = null
        };
    }

    /// <summary>
    /// Crea una personalidad típica para un spa de bebés.
    /// </summary>
    public static BusinessPersonality ForBabySpa(string businessName = "MimosBabySpa")
    {
        return new BusinessPersonality
        {
            AssistantName = "María",
            Gender = "Female",
            Tone = new List<string> { "Cálido", "Profesional", "Empático", "Cercano", "Amoroso" },
            UseEmojis = true,
            GreetingStyle = "Amigable",
            SamplePhrases = new Dictionary<string, string>
            {
                ["Greeting"] = "¡Hola! 😊 Soy {AssistantName}, un gusto saludarte. Estoy aquí para ayudarte a encontrar el mejor plan para tu bebé.",
                ["Closing"] = "Estoy aquí para ayudarte en todo lo que necesites 😊",
                ["Thanking"] = "¡Gracias por confiar en {BusinessName}! 💙",
                ["Concern"] = "Entiendo tu preocupación. Estoy aquí para acompañarte.",
                ["Excitement"] = "¡Qué emoción! Tu bebé va a disfrutar mucho esta experiencia 💙"
            },
            Expertise = "experta en cuidado y relajación para bebés"
        };
    }

    /// <summary>
    /// Reemplaza los placeholders en una frase con valores reales.
    /// </summary>
    public string ReplacePlaceholders(string phrase, string businessName)
    {
        return phrase
            .Replace("{AssistantName}", AssistantName)
            .Replace("{BusinessName}", businessName);
    }

    /// <summary>
    /// Obtiene una frase de ejemplo para un contexto específico.
    /// </summary>
    public string GetPhrase(string context, string businessName)
    {
        if (SamplePhrases.TryGetValue(context, out var phrase))
        {
            return ReplacePlaceholders(phrase, businessName);
        }

        return string.Empty;
    }
}
