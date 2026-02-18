namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de personalidad del asistente virtual del negocio.
/// Se carga desde BusinessConfiguration key=Personality (0).
/// Si el valor almacenado es texto libre, se usa SystemIdentityText directamente en el system prompt.
/// Si el valor es JSON estructurado, se deserializa en los campos tipados.
/// </summary>
public class BusinessPersonality
{
    /// <summary>
    /// Texto libre de identidad del asistente tal como fue escrito para el tenant.
    /// Cuando tiene valor, se inyecta directamente en el system prompt como sección de rol,
    /// reemplazando al template genérico. Ejemplo: "Eres María, una asesora experta en spa para bebés..."
    /// </summary>
    public string? SystemIdentityText { get; set; }

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
            Expertise = null
        };
    }

}
