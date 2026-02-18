using MimosBabySpa.Application.Configuration;
using System.Text;

namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Genera la tabla compacta de campos disponibles para el prompt de extracción.
///
/// Multitenant: los servicios y atributos vienen del LoadedBusinessContext, nunca hardcodeados.
/// Formato tabla → menos tokens que prosa, más fácil de procesar por el LLM.
/// </summary>
public class FieldDefinitionsBuilder
{
    public string Build(LoadedBusinessContext context)
    {
        var now = DateTime.Now;
        var tomorrow = now.AddDays(1);
        var sb = new StringBuilder();

        sb.AppendLine("## Campos disponibles:");
        sb.AppendLine();
        sb.AppendLine("### Campos core:");
        sb.AppendLine("| Campo | Tipo | Formato / Valores válidos |");
        sb.AppendLine("|-------|------|--------------------------|");
        sb.AppendLine("| CustomerName | Text | Nombre de quien reserva (no del bebé/mascota/etc.) |");
        sb.AppendLine("| Phone | Phone | Solo lectura — provisto por el canal |");
        sb.AppendLine("| Email | Email | usuario@dominio.com |");

        // Servicios — dinámicos por tenant
        var serviceNames = string.Join(", ", context.Services.Select(s => $"\"{s.Name}\""));
        sb.AppendLine($"| Service | Service | Exacto: {serviceNames} |");

        sb.AppendLine($"| DesiredDate | Date | YYYY-MM-DD · hoy={now:yyyy-MM-dd} · mañana={tomorrow:yyyy-MM-dd} |");
        sb.AppendLine("| DesiredTime | Time | HH:MM (24h) · ejemplos: 09:00, 14:30 |");

        // Atributos de negocio — dinámicos por tenant
        if (context.Attributes.Any())
        {
            sb.AppendLine();
            sb.AppendLine("### Atributos del negocio (prefijo \"Attribute:\"):");
            sb.AppendLine("| Campo | Tipo | Requerido | Descripción |");
            sb.AppendLine("|-------|------|-----------|-------------|");

            foreach (var (key, def) in context.Attributes)
            {
                var required = def.IsRequired ? "Sí" : "No";
                var desc = def.Description ?? key;
                var pattern = !string.IsNullOrEmpty(def.ValidationPattern)
                    ? $" · patrón: `{def.ValidationPattern}`"
                    : string.Empty;
                sb.AppendLine($"| Attribute:{key} | {def.Type} | {required} | {desc}{pattern} |");
            }
        }

        return sb.ToString().TrimEnd();
    }
}
