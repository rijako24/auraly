using MimosBabySpa.Application.Configuration;
using System.Text;

namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Constructor de definiciones de campos disponibles para extracción.
/// Incluye campos core y atributos de negocio.
/// </summary>
public class FieldDefinitionsBuilder
{
    /// <summary>
    /// Construye la sección de campos disponibles.
    /// </summary>
    public string Build(LoadedBusinessContext context)
    {
        var now = DateTime.Now;
        var tomorrow = now.AddDays(1);

        var sb = new StringBuilder();
        sb.AppendLine("## CAMPOS DISPONIBLES:");
        sb.AppendLine();
        sb.AppendLine("### 1️⃣ CAMPOS CORE (siempre disponibles):");
        sb.AppendLine();
        sb.AppendLine("### 🚨 CAMPO CRÍTICO #1: CustomerName");
        sb.AppendLine("- **CustomerName**: Nombre completo del cliente (quien hace la reserva)");
        sb.AppendLine("  **PATRONES EXACTOS A DETECTAR:**");
        sb.AppendLine("  • 'Me llamo X' → CustomerName");
        sb.AppendLine("  • 'Mi nombre es X' → CustomerName");
        sb.AppendLine("  • 'Soy X' → CustomerName");
        sb.AppendLine("  ⚠️ NO confundir con 'Mi bebé se llama X' (eso es Attribute:BabyName)");
        sb.AppendLine("  ✅ **SIEMPRE extraer CustomerName cuando el usuario se identifica**");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("- **Phone**: Número de teléfono del cliente");
        sb.AppendLine("  Formatos: \"555-1234\", \"5551234567\", \"+52 555 123 4567\"");
        sb.AppendLine();
        sb.AppendLine("- **Email**: Correo electrónico del cliente");
        sb.AppendLine("  Formato: \"usuario@dominio.com\"");
        sb.AppendLine();
        sb.AppendLine($"- **Service**: Nombre EXACTO del servicio (debe coincidir con uno disponible)");
        sb.AppendLine($"  ⚠️ CRÍTICO: SOLO usa servicios de esta lista, NO inventes otros");
        sb.AppendLine($"  Válidos: {string.Join(", ", context.Services.Select(s => $"\"{s.Name}\""))}");
        sb.AppendLine();
        sb.AppendLine($"- **DesiredDate**: Fecha en formato YYYY-MM-DD");
        sb.AppendLine($"  Hoy: {now:yyyy-MM-dd} | Mañana: {tomorrow:yyyy-MM-dd}");
        sb.AppendLine($"  ⚠️ IMPORTANTE: Si el usuario pregunta por disponibilidad/horarios CON una fecha,");
        sb.AppendLine($"  EXTRAE la fecha incluso si está en la misma pregunta");
        sb.AppendLine($"  Ejemplos:");
        sb.AppendLine($"  • 'qué horarios tienes mañana' → DesiredDate = '{tomorrow:yyyy-MM-dd}'");
        sb.AppendLine($"  • 'hay cupo para hoy' → DesiredDate = '{now:yyyy-MM-dd}'");
        sb.AppendLine();
        sb.AppendLine("- **DesiredTime**: Hora en formato HH:MM (24h)");
        sb.AppendLine("  Ejemplos: \"10:00\", \"15:30\", \"09:00\"");
        sb.AppendLine();
        sb.AppendLine("### 2️⃣ ATRIBUTOS DEL NEGOCIO (configurados):");
        sb.AppendLine("**IMPORTANTE:** Para atributos de negocio, SIEMPRE usa el prefijo \"Attribute:\" en el field_name.");
        sb.AppendLine();
        sb.AppendLine(BuildAttributesSchema(context.Attributes));

        return sb.ToString();
    }

    private string BuildAttributesSchema(Dictionary<string, AttributeDefinition> attributes)
    {
        if (!attributes.Any())
            return "*(No hay atributos personalizados configurados para este negocio)*";

        var sb = new StringBuilder();
        foreach (var attr in attributes)
        {
            var required = attr.Value.IsRequired ? "**REQUERIDO**" : "Opcional";
            var fieldName = $"Attribute:{attr.Key}";
            sb.AppendLine($"- **{fieldName}** ({attr.Value.Type}) - {required}");
            sb.AppendLine($"  Nombre interno: {attr.Key}");
            sb.AppendLine($"  Descripción: {attr.Value.Description ?? "N/A"}");
            if (!string.IsNullOrEmpty(attr.Value.ValidationPattern))
                sb.AppendLine($"  Patrón: `{attr.Value.ValidationPattern}`");
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }
}
