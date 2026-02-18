using MimosBabySpa.Domain.Models;
using System.Text;

namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Genera un snapshot compacto del estado de la conversación para el prompt de extracción.
///
/// SOLO datos, cero reglas. Las reglas de inferencia van en el prompt de extracción.
/// El LLM necesita saber qué ya tiene (para no re-extraer) y el último mensaje del bot
/// (para inferir a qué campo responde un valor simple del usuario).
/// </summary>
public class StateContextBuilder
{
    /// <summary>
    /// Snapshot de una línea por grupo de datos + último mensaje del bot si existe.
    /// Formato diseñado para ser compacto y legible por el LLM en pocos tokens.
    /// </summary>
    public string Build(ConversationState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Estado actual:");

        // Datos ya recolectados en una sola línea compacta
        var dataLine = BuildDataLine(state);
        sb.AppendLine(dataLine);

        // Atributos de negocio (dinámicos por tenant)
        if (state.Attributes.Any())
        {
            var attrs = string.Join(" | ", state.Attributes.Select(a => $"{a.Key}={a.Value}"));
            sb.AppendLine($"Atributos: {attrs}");
        }

        // Último mensaje del bot — clave para inferencia contextual
        if (!string.IsNullOrWhiteSpace(state.LastBotMessage))
            sb.AppendLine($"Último mensaje del asistente: \"{state.LastBotMessage}\"");

        return sb.ToString().TrimEnd();
    }

    private static string BuildDataLine(ConversationState state)
    {
        var parts = new List<string>
        {
            $"CustomerName={state.CustomerName ?? "—"}",
            $"Phone={state.Phone ?? "—"}",
            $"Service={state.Service ?? "—"}",
            $"Date={state.DesiredDate?.ToString("yyyy-MM-dd") ?? "—"}",
            $"Time={state.DesiredTime?.ToString("HH:mm") ?? "—"}",
            $"Email={state.Email ?? "—"}"
        };

        return string.Join(" | ", parts);
    }
}
