using MimosBabySpa.Domain.Models;
using System.Text;

namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Genera un snapshot compacto del estado de la conversación para el prompt de extracción.
///
/// SOLO datos, cero reglas. Las reglas de inferencia van en el prompt de extracción.
/// El historial conversacional se pasa como mensajes user/assistant — no se embebe aquí.
/// </summary>
public class StateContextBuilder
{
    /// <summary>
    /// Snapshot de una línea por grupo de datos. Formato compacto para pocos tokens.
    /// </summary>
    public string Build(ConversationState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Estado actual:");

        var dataLine = BuildDataLine(state);
        sb.AppendLine(dataLine);
        sb.AppendLine($"Stage={state.CurrentStage} | AvailabilityConfirmed={state.AvailabilityConfirmed} | ReservationConfirmed={state.ReservationConfirmed}");

        if (state.Attributes.Any())
        {
            var attrs = string.Join(" | ", state.Attributes.Select(a => $"{a.Key}={a.Value}"));
            sb.AppendLine($"Atributos: {attrs}");
        }

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
