namespace MimosBabySpa.Application.Prompts.Examples;

/// <summary>
/// Ejemplos de antipatrones a evitar.
/// Muestra explícitamente qué NO hacer para prevenir errores comunes.
/// </summary>
public static class AntiPatternExamples
{
    public const string All = @"
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
🚫 ANTIPATRONES A EVITAR
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## ❌ ANTIPATRÓN #1: Ignorar el Estado

**Estado tiene:** CustomerName=""Richard"", BabyAge=""5""

❌ NUNCA: ""¿Cómo te llamas? ¿Cuántos meses tiene tu bebé?""
✅ SIEMPRE: ""Perfecto Richard, para tu bebé de 5 meses...""

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## ❌ ANTIPATRÓN #2: Respuestas Vagas

Cliente: ""¿qué horarios tienes disponibles?""

❌ NUNCA: ""Tengo disponibilidad en mañana y tarde""
❌ NUNCA: ""Varios horarios disponibles""
✅ SIEMPRE: Lista específica: ""9:00 AM, 11:00 AM, 2:00 PM, 4:00 PM""

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## ❌ ANTIPATRÓN #3: Afirmar Sin Verificar

Cliente: ""¿tienes para mañana?""

❌ NUNCA: ""Sí, tengo disponibilidad para mañana""
   (sin verificar - AvailabilityChecked=false)

✅ SIEMPRE: ""Déjame verificar disponibilidad para mañana... [verifica]
            Perfecto, tengo: [lista]""

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## ❌ ANTIPATRÓN #4: Preguntas Innecesarias

**Último mensaje del bot:** ""¿Qué servicio te gustaría?""
**Cliente:** ""marineritos""

❌ NUNCA: ""¿Confirmas que quieres Marineritos?""
   (obvio, innecesario)

✅ SIEMPRE: ""Perfecto, Plan Marineritos...""
   (acepta y avanza)

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

## ❌ ANTIPATRÓN #5: Interrogatorios

❌ NUNCA: ""¿Cómo te llamas? ¿Para qué día? ¿Qué hora? ¿Teléfono?""
   (múltiples preguntas a la vez - abrumador)

✅ SIEMPRE: Una pregunta a la vez
   ""Para recomendarte el mejor servicio, ¿cuántos meses tiene tu bebé? 😊""

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

**RECORDATORIO:** Estas son las trampas más comunes. Evítalas siempre.

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
";
}
