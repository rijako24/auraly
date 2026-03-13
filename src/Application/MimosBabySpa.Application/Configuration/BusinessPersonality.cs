using System.Text.RegularExpressions;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Configuración de personalidad del asistente virtual del negocio.
/// Se carga desde BusinessConfiguration key=Personality (0). Si no hay configuración,
/// se usa SystemConfiguration.ToneAndStyle como fallback.
/// Todo el tono (identidad + estilo + emoticonos) viene en un único texto libre.
/// </summary>
public class BusinessPersonality
{
    /// <summary>
    /// Texto completo de identidad, tono y estilo del asistente.
    /// Se inyecta tal cual en el system prompt. Contiene quién es, cómo habla, uso de emoticonos, etc.
    /// </summary>
    public string PersonalityText { get; set; } = string.Empty;

    /// <summary>
    /// Nombre del asistente, extraído del texto o "Asistente" por defecto.
    /// Se usa para referencia programática (tests, logs, etc.).
    /// </summary>
    public string AssistantName { get; set; } = "Asistente";

    /// <summary>
    /// Indica si hay personalidad configurada (desde negocio o fallback del sistema).
    /// </summary>
    public bool HasPersonality => !string.IsNullOrWhiteSpace(PersonalityText);

    /// <summary>
    /// Intenta extraer el nombre del asistente del texto.
    /// Patrones: "Eres Luna,", "Eres María,", "Soy X,", etc.
    /// </summary>
    public static string ExtractAssistantName(string personalityText)
    {
        if (string.IsNullOrWhiteSpace(personalityText))
            return "Asistente";

        var text = personalityText.Trim();
        var match = Regex.Match(text, @"(?:Eres|Soy)\s+(\w+)\s*[,.]", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : "Asistente";
    }
}
