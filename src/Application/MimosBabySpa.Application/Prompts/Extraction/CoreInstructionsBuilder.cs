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

        // Generado desde ExtractionIntentions.JsonPropertyNames — fuente única,
        // sin duplicar strings. Si se agrega una intención nueva, aparece aquí automáticamente.
        var intentionNames = string.Join(", ", ExtractionIntentions.JsonPropertyNames);

        return $"""
            # EXTRACCIÓN DE INFORMACIÓN — JSON MODE
            Negocio: {context.Info.Name} | Hoy: {now:yyyy-MM-dd} | Mañana: {tomorrow:yyyy-MM-dd} | Pasado mañana: {now.AddDays(2):yyyy-MM-dd}
            Responde SOLO con JSON válido. Sin texto, markdown ni explicaciones fuera del JSON.

            ## Reglas de extracción:
            1. Extraer solo si confidence >= {minConf}. Si confidence < {minConf} → agregar a `ambiguities`, NO a `extracted_fields`.
            2. Valores estructurados únicamente: "Ana" ✓ · "me llamo Ana" ✗ · fechas YYYY-MM-DD · horas HH:MM.
            3. Respuestas negativas ("ninguna", "no", "no tiene") → valor "N/A", confidence 0.95.
            4. Inferencia contextual (el historial muestra la pregunta del asistente y la respuesta del usuario):
               - Si el asistente preguntó por un dato y el usuario responde, extraer el VALOR LIMPIO de ese campo.
               - Ejemplos: "se llama Thomas" → campo nombre: "Thomas" | "son 2 bebés" → campo cantidad: "2" | "tiene 6 meses" → campo edad: "6"
               - Confidence: 0.90 si el valor es coherente con el tipo del campo.
               - Si el usuario claramente NO responde a la pregunta (cambia de tema, pregunta otra cosa) → no forzar extracción.
            5. Service: usar nombre EXACTO de la lista. NO inventar nombres.
            6. DesiredDate con referencias temporales (mapeo obligatorio):
               - "hoy" → {now:yyyy-MM-dd}
               - "mañana" → {tomorrow:yyyy-MM-dd}
               - "pasado mañana" → {now.AddDays(2):yyyy-MM-dd}
               - Días de semana → próxima ocurrencia futura.
            7. Si el usuario pide disponibilidad/horarios Y menciona una fecha → SIEMPRE extraer DesiredDate + marcar user_requested_availability=true.
            8. CAMBIO DE FECHA EN PREGUNTA DE SEGUIMIENTO: Frases como "¿y para [fecha]?", "¿y [fecha]?",
               "¿qué hay [fecha]?", "¿para pasado mañana?" expresan una NUEVA fecha de consulta.
               Aunque el estado ya tenga una fecha anterior, extraer DesiredDate con la nueva fecha (confidence 0.95)
               Y marcar user_requested_availability=true. La nueva fecha reemplaza a la anterior.
            9. SEPARACIÓN ESTRICTA campos / intenciones: Los nombres ({intentionNames})
               pertenecen EXCLUSIVAMENTE al bloque "intentions". NUNCA los incluyas en "extracted_fields".
            10. ALCANCE: Extraer SOLO del último mensaje del usuario (delimitado con ---MENSAJE A ANALIZAR---).
                El historial es CONTEXTO para entender referencias. NO re-extraer datos que ya figuran en "Estado actual".
                NO inventar campos fuera de la tabla de campos disponibles (ej. TotalPrice).

            ## Intenciones (detectar del texto, no inventar):
            - user_requested_availability: pregunta explícita por horarios/disponibilidad/cupo. También aplica en preguntas de seguimiento de fecha ("¿y para pasado mañana?").
            - user_confirmed_booking: true SOLO cuando Stage=ConfirmingBooking en el estado actual. En cualquier otro stage,
              respuestas afirmativas ("sí", "está bien", "ok", "dale") son aceptación del dato que se discute, NO confirmación de reserva.
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
