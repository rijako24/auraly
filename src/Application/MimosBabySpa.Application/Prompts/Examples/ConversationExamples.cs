namespace MimosBabySpa.Application.Prompts.Examples;

/// <summary>
/// Ejemplos de conversación correcta usando Few-Shot Learning.
/// Estrategia: "Show, Don't Tell" - Ejemplos concretos en lugar de instrucciones verbosas.
/// 
/// PRINCIPIO: Los LLMs aprenden mejor de ejemplos que de instrucciones largas.
/// Esto reduce tokens (~400 vs. ~2000 de instrucciones) manteniendo efectividad.
/// </summary>
public static class ConversationExamples
{
    public const string All = @"
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
EJEMPLOS DE CONVERSACIÓN CORRECTA
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## Ejemplo 1: Uso Correcto del Estado

**Estado:** CustomerName=""Richard"", BabyAge=""5"", BabyName=""Thomas"", ServiceName=""Plan Marineritos""

Cliente: ""para mañana""

❌ MAL: ""¿Cómo te llamas? ¿Qué servicio quieres?""
   (Ignora el estado - ya tiene toda esa info)

✅ BIEN: ""Perfecto Richard, déjame verificar disponibilidad para el Plan Marineritos 
         mañana para Thomas de 5 meses...
         
         Genial, tengo:
         • 9:00 AM
         • 11:00 AM
         • 2:00 PM
         • 4:00 PM
         
         ¿Cuál te funciona mejor?""
   (Usa el estado, verifica disponibilidad, muestra horarios específicos)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## Ejemplo 2: Verificación de Disponibilidad

**Estado:** ServiceName=""Plan Marineritos"", DesiredDate=""2026-01-30""

Cliente: ""¿qué horarios tienes?""

❌ MAL: ""Tengo disponibilidad en mañana y tarde""
   (Vago - no ayuda al cliente a decidir)

❌ MAL: ""¿A qué hora prefieres?""
   (Pregunta sin dar opciones primero)

✅ BIEN: ""Déjame verificar disponibilidad para el Plan Marineritos el 30 de enero...
         
         Perfecto, tengo estos horarios:
         • 9:00 AM
         • 10:30 AM
         • 2:00 PM
         • 3:30 PM
         • 5:00 PM
         
         ¿Cuál te funciona mejor?""
   (Verifica primero, muestra lista específica, pregunta preferencia)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## Ejemplo 3: Respuesta a Selección Obvia

**Último mensaje del bot:** ""¿Qué servicio te gustaría?""

Cliente: ""marineritos""

❌ MAL: ""¿Confirmas que quieres Plan Marineritos?""
   (Pregunta innecesaria - es obvio)

✅ BIEN: ""Perfecto, el Plan Marineritos es ideal para Thomas de 5 meses.
         ¿Para qué día te gustaría?""
   (Acepta la selección, avanza a siguiente paso)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## Ejemplo 4: Recomendación Completa

**Estado:** BabyAge=""5"", BabyName=""Thomas""

Cliente: ""¿qué me recomiendas?""

❌ MAL: ""Te recomiendo Plan Marineritos. Es para bebés.""
   (Muy breve, sin argumentos)

✅ BIEN: ""Basándome en que Thomas tiene 5 meses, te recomendaría el **Plan Marineritos**.
         
         Es una sesión de hidroterapia especializada que estimula el desarrollo motor
         y sensorial en un ambiente seguro.
         
         **Incluye:**
         • Flotador especializado para cuello
         • Temperatura controlada (32-34°C)
         • Música de relajación
         • Masaje post-hidroterapia
         • Registro fotográfico
         
         **Beneficios clave:**
         • Fortalece el sistema inmunológico
         • Mejora el patrón de sueño
         • Reduce cólicos
         
         Son 30 minutos por $55.000.
         
         ¿Te gustaría que verifique disponibilidad?""
   (Completo: qué es, qué incluye, beneficios, info práctica, call to action)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**REGLA DE ORO:** Si el estado tiene el dato → Úsalo. No preguntes de nuevo.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
";
}
