using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.LLM.Extraction;

namespace MimosBabySpa.Application.Prompts.Extraction;

/// <summary>
/// Genera la cabecera del prompt de extracción: identidad, fechas de referencia y reglas unificadas.
///
/// El mensaje del usuario NO se incluye aquí — se pasa como rol "user" en el request LLM,
/// evitando duplicación de tokens.
/// </summary>
public class CoreInstructionsBuilder
{
    public string Build(LoadedBusinessContext context)
    {
        var now = DateTime.Now;
        var tomorrow = now.AddDays(1);
        var minConf = ExtractionConstants.MinConfidence;

        return $"""
            # EXTRACCIÓN DE INFORMACIÓN — JSON MODE
            Negocio: {context.Info.Name} | Hoy: {now:yyyy-MM-dd} | Mañana: {tomorrow:yyyy-MM-dd}
            Responde SOLO con JSON válido. Sin texto, markdown ni explicaciones fuera del JSON.

            ## Reglas de extracción:
            1. Extraer solo si confidence >= {minConf}. Si confidence < {minConf} → agregar a `ambiguities`, NO a `extracted_fields`.
            2. Valores estructurados únicamente: "Ana" ✓ · "me llamo Ana" ✗ · fechas YYYY-MM-DD · horas HH:MM.
            3. Respuestas negativas ("ninguna", "no", "no tiene") → valor "N/A", confidence 0.95.
            4. Inferencia contextual (cuando el bot hizo una pregunta y el usuario responde con un valor simple):
               - Analiza semánticamente de qué campo preguntó el bot (compara con descripciones de campos disponibles).
               - Si el usuario responde 1-4 palabras sin verbo conjugado → extraer ese campo, confidence 0.85.
               - Si el bot mencionó un servicio y el usuario dice "ese", "ese mismo", "sí", "ok" → extraer Service, confidence 0.9.
               - Si hay ambigüedad sin contexto previo claro → `ambiguities` tipo "referential".
            5. Service: usar nombre EXACTO de la lista. NO inventar nombres.
            6. DesiredDate con referencias: "hoy"={now:yyyy-MM-dd}, "mañana"={tomorrow:yyyy-MM-dd}, "pasado mañana"={now.AddDays(2):yyyy-MM-dd}. Días de semana → próxima ocurrencia.
            7. Si el usuario pide disponibilidad/horarios Y menciona una fecha → extraer DesiredDate + marcar user_requested_availability=true.

            ## Intenciones (detectar del texto, no inventar):
            - user_requested_availability: pregunta explícita por horarios/disponibilidad/cupo.
            - user_confirmed_booking: confirmación explícita de reserva ("sí", "confirmo", "adelante", "ok") en contexto de reserva propuesta.
            - is_information_query: pregunta por servicios/planes/precios/información general.
            - user_wants_to_cancel: "cancelar", "no quiero", "cambié de opinión", "mejor no".

            ## Ambigüedades (tipos):
            - temporal: "pronto", "luego", "otro día" — sin fecha concreta.
            - referential: "ese plan", "esa fecha" — sin contexto previo claro.
            - multiple_values: "Mateo o Lucas" — más de una opción.
            - incomplete: "el lunes" — incompleto sin saber cuál.
            """;
    }
}
