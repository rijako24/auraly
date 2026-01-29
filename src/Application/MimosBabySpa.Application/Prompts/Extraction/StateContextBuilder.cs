using MimosBabySpa.Domain.Models;
using System.Text;

namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Constructor de contexto del estado actual de la conversación.
/// Muestra al LLM qué información ya se ha recolectado.
/// </summary>
public class StateContextBuilder
{
    /// <summary>
    /// Construye la representación del estado actual.
    /// ✅ INCLUYE contexto conversacional para inferencia de servicios.
    /// </summary>
    public string Build(ConversationState state)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## ESTADO ACTUAL:");
        sb.AppendLine();

        // ✅ NUEVO: Contexto conversacional para inferencia
        if (!string.IsNullOrEmpty(state.LastBotMessage))
        {
            sb.AppendLine("### 📝 Contexto conversacional:");
            sb.AppendLine($"**Último mensaje del bot:** \"{state.LastBotMessage}\"");
            sb.AppendLine();
            sb.AppendLine("⚠️ **REGLAS DE INFERENCIA CONTEXTUAL:**");
            sb.AppendLine();
            
            sb.AppendLine("**1️⃣ INFERENCIA DE SERVICIOS:**");
            sb.AppendLine("Si el usuario:");
            sb.AppendLine("- ✅ Confirma: 'sí', 'ok', 'perfecto', 'adelante', 'eso', 'ese'");
            sb.AppendLine("- ✅ Pide detalles: 'explícame más', 'cuéntame', 'cómo funciona', 'qué hace'");
            sb.AppendLine("- ✅ Pide disponibilidad: 'qué horarios', 'hay cupo', 'cuándo puedo', 'disponibilidad'");
            sb.AppendLine("- ✅ Usa pronombres demostrativos: 'ese plan', 'ese servicio', 'esa opción', 'este plan'");
            sb.AppendLine();
            sb.AppendLine("**Y el bot mencionó un servicio específico:**");
            sb.AppendLine("→ 🎯 **OBLIGATORIO: Extraer ese servicio como `Service`**");
            sb.AppendLine("→ Busca nombres de servicios en el mensaje del bot");
            sb.AppendLine("→ Confidence: 0.9 (inferencia contextual)");
            sb.AppendLine();
            
            sb.AppendLine("**2️⃣ INFERENCIA DE RESPUESTAS DIRECTAS (GENÉRICA):**");
            sb.AppendLine("Si el bot hizo una pregunta Y el usuario respondió con un valor simple:");
            sb.AppendLine();
            sb.AppendLine("**Proceso de inferencia:**");
            sb.AppendLine("1. Analiza semánticamente la pregunta del bot");
            sb.AppendLine("2. Compara con las descripciones de TODOS los campos disponibles (core + atributos)");
            sb.AppendLine("3. Identifica qué campo se está preguntando basándote en:");
            sb.AppendLine("   • Similitud semántica entre pregunta y descripción/displayName del campo");
            sb.AppendLine("   • Tipo de dato esperado (Text, Number, Date, Time, etc.)");
            sb.AppendLine("   • Contexto del negocio y keywords en las descripciones");
            sb.AppendLine("4. Extrae ese campo con la respuesta del usuario");
            sb.AppendLine();
            sb.AppendLine("**Criterios para 'valor simple':**");
            sb.AppendLine("- Respuesta de 1-4 palabras");
            sb.AppendLine("- No contiene verbos conjugados (excepto 'es', 'son')");
            sb.AppendLine("- Es un nombre propio, número, fecha, hora, o palabra clave");
            sb.AppendLine();
            sb.AppendLine("**Confidence:** 0.85-0.9 (inferencia contextual genérica)");
            sb.AppendLine();
            sb.AppendLine("**Ejemplos genéricos:**");
            sb.AppendLine("• Bot: \"¿Cómo se llama tu bebé?\" → Usuario: \"thomas\"");
            sb.AppendLine("  → Analiza: pregunta sobre nombre + contexto bebé");
            sb.AppendLine("  → Busca campo con descripción similar a \"nombre del bebé\"");
            sb.AppendLine("  → Extrae: Attribute:BabyName = \"thomas\"");
            sb.AppendLine();
            sb.AppendLine("• Bot: \"¿Para cuántas personas?\" → Usuario: \"4\"");
            sb.AppendLine("  → Analiza: pregunta sobre cantidad + respuesta numérica");
            sb.AppendLine("  → Busca campo tipo Number con descripción sobre personas/cantidad");
            sb.AppendLine("  → Extrae: Attribute:PartySize = \"4\" (si ese campo existe)");
            sb.AppendLine();
        }

        // Verificar si hay información recolectada
        var hasService = !string.IsNullOrEmpty(state.Service);
        var hasDate = state.DesiredDate.HasValue;
        var hasTime = state.DesiredTime.HasValue;
        var hasCustomerName = !string.IsNullOrEmpty(state.CustomerName);
        var hasPhone = !string.IsNullOrEmpty(state.Phone);
        var hasAttributes = state.Attributes.Any();

        // Mostrar campos ya recolectados
        if (hasService || hasDate || hasTime || hasCustomerName || hasPhone || hasAttributes)
        {
            sb.AppendLine("### 📊 Información ya recolectada:");
            sb.AppendLine();

            if (hasService)
                sb.AppendLine($"- **Servicio seleccionado:** {state.Service}");

            if (hasDate)
                sb.AppendLine($"- **Fecha solicitada:** {state.DesiredDate:yyyy-MM-dd}");

            if (hasTime)
                sb.AppendLine($"- **Hora solicitada:** {state.DesiredTime:HH:mm}");

            if (hasCustomerName)
                sb.AppendLine($"- **Nombre del cliente:** {state.CustomerName}");

            if (hasPhone)
                sb.AppendLine($"- **Teléfono:** {state.Phone}");

            if (hasAttributes)
            {
                foreach (var attr in state.Attributes)
                    sb.AppendLine($"- **{attr.Key}:** {attr.Value}");
            }
        }
        else
        {
            sb.AppendLine("*(Sin información recolectada aún)*");
        }

        return sb.ToString();
    }
}
