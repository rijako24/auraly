namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Template para presentar horarios disponibles al LLM.
/// 
/// IMPORTANTE: 
/// - Estos son horarios SUGERIDOS generados por el sistema
/// - NO significa que TODOS estén 100% disponibles
/// - El LLM debe verificar disponibilidad específica cuando el cliente elija un horario
/// - El backend solo popula los placeholders con datos dinámicos
/// </summary>
public static class AvailableTimeSlotsTemplate
{
    /// <summary>
    /// Encabezado de la sección de horarios disponibles
    /// </summary>
    public const string Header = @"
## ⏰ HORARIOS SUGERIDOS PARA ESTE DÍA";

    /// <summary>
    /// Instrucciones sobre qué son estos horarios
    /// </summary>
    public const string Explanation = @"
**IMPORTANTE**: Estos son horarios SUGERIDOS basados en el horario de operación del negocio.
Algunos pueden estar ocupados. Cuando el cliente elija un horario específico, se verificará
la disponibilidad exacta automáticamente.";

    /// <summary>
    /// Instrucciones sobre cuándo mostrar estos horarios
    /// </summary>
    public const string WhenToShow = @"
**Cuándo mostrar estos horarios:**
- Cliente pregunta: ""¿qué horarios tienes?"", ""¿cuáles están disponibles?"", ""¿a qué horas puedo?"", etc.
- Cliente no ha especificado hora exacta aún";

    /// <summary>
    /// Formato de la lista de horarios
    /// Placeholders: {time_slots_list}
    /// </summary>
    public const string TimeSlotsList = @"
**Horarios sugeridos:**
{time_slots_list}";

    /// <summary>
    /// Formato de respuesta sugerido para el LLM
    /// Placeholders: {customer_name}, {time_slots_bullets}
    /// </summary>
    public const string ResponseFormat = @"
**Formato de respuesta sugerido:**
""Perfecto{customer_name_greeting}! Para ese día tengo estos horarios:
{time_slots_bullets}
¿Cuál te funciona mejor?""

**IMPORTANTE**: 
- NO digas ""todos están disponibles"" (algunos pueden estar ocupados)
- Cuando el cliente elija, se verificará disponibilidad automáticamente
- Si el horario elegido está ocupado, sugerirás otro de la lista";

    /// <summary>
    /// Construye la sección completa de horarios disponibles
    /// </summary>
    public static string Build(
        string customerName,
        string[] timeSlots)
    {
        if (timeSlots == null || timeSlots.Length == 0)
        {
            return string.Empty;
        }

        var sb = new System.Text.StringBuilder();
        
        sb.AppendLine(Header);
        sb.AppendLine(Explanation);
        sb.AppendLine(WhenToShow);
        sb.AppendLine();
        
        // Lista de horarios
        sb.AppendLine("**Horarios sugeridos:**");
        foreach (var slot in timeSlots)
        {
            sb.AppendLine($"• {slot}");
        }
        sb.AppendLine();
        
        // Formato de respuesta con placeholders poblados
        var greeting = !string.IsNullOrEmpty(customerName) ? $" {customerName}" : "";
        var bullets = string.Join("\n", timeSlots.Select(s => $"• {s}"));
        
        sb.AppendLine("**Formato de respuesta sugerido:**");
        sb.AppendLine($"\"Perfecto{greeting}! Para ese día tengo estos horarios:");
        foreach (var slot in timeSlots)
        {
            sb.AppendLine($"• {slot}");
        }
        sb.AppendLine("¿Cuál te funciona mejor?\"");
        sb.AppendLine();
        
        sb.AppendLine("**IMPORTANTE**:");
        sb.AppendLine("- NO digas \"todos están disponibles\" (algunos pueden estar ocupados)");
        sb.AppendLine("- Cuando el cliente elija, se verificará disponibilidad automáticamente");
        sb.AppendLine("- Si el horario elegido está ocupado, sugerirás otro de la lista");
        
        return sb.ToString();
    }
}
