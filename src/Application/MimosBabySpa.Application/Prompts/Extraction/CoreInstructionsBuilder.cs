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
            4. Resolución contextual (el historial es contexto para interpretar el mensaje actual):
               a) Valor directo: el usuario proporciona el dato explícitamente.
                  Ej: "se llama Thomas" → CustomerName: "Thomas" | "son 2 bebés" → cantidad: "2"
               b) Aceptación por referencia: el asistente propuso o mostró opciones y el usuario la acepta
                  ("sí", "ese", "esa", "está bien", "la primera", "la sencilla", etc.).
                  Resolver la referencia al valor concreto usando el historial. El valor debe ser el nombre
                  exacto del catálogo (servicio, add-on, horario, etc.). Aplica a CUALQUIER campo.
                  Ej: Bot ofrece "Plan Marineritos" → usuario: "sí, ese plan" → Service: "Plan Marineritos"
                  Ej: Bot muestra "Decoración Sencilla" y "Bouquet" → usuario: "la sencilla" → Attribute:SelectedAddOns: "Decoración Sencilla"
                  Ej: Bot muestra horarios 09:00, 11:00 → usuario: "a las 9" → DesiredTime: "09:00"
               c) Confidence: 0.92 si el referente es inequívoco en el historial. 0.70 si hay múltiples candidatos.
               d) Si el usuario claramente NO responde (cambia de tema) → no forzar extracción.
            5. Fechas temporales (mapeo obligatorio):
               - "hoy" → {now:yyyy-MM-dd} | "mañana" → {tomorrow:yyyy-MM-dd} | "pasado mañana" → {now.AddDays(2):yyyy-MM-dd}
               - Días de semana → próxima ocurrencia futura.
               - Si el usuario pide disponibilidad/horarios mencionando una fecha (incluso "¿y para mañana?")
                 → extraer DesiredDate + marcar user_requested_availability=true.
            6. SEPARACIÓN ESTRICTA campos / intenciones: Los nombres ({intentionNames})
               pertenecen EXCLUSIVAMENTE al bloque "intentions". NUNCA los incluyas en "extracted_fields".
            7. ALCANCE: Extraer SOLO del último mensaje del usuario (delimitado con ---MENSAJE A ANALIZAR---).
                El historial es CONTEXTO para entender referencias. NO re-extraer datos que ya figuran en "Estado actual".
                NO inventar campos fuera de la tabla de campos disponibles (ej. TotalPrice).

            ## Intenciones (detectar del texto, no inventar):
            - user_requested_availability: pregunta explícita por horarios/disponibilidad/cupo. También aplica en preguntas de seguimiento de fecha ("¿y para pasado mañana?").
            - user_confirmed_booking: true SOLO cuando Stage=ConfirmingBooking en el estado actual. En cualquier otro stage,
              respuestas afirmativas ("sí", "está bien", "ok", "dale") son aceptación del dato que se discute, NO confirmación de reserva.
            - is_information_query: pregunta por servicios/planes/precios/información general.
            - user_wants_to_cancel: "cancelar", "no quiero", "cambié de opinión", "mejor no".
            - user_requests_new_payment_link: "envíame otro link", "el link expiró", "mandame el link de nuevo",
              "pásame otro link", "necesito otro link de pago", "no me funciona el link" — solo cuando Stage=AwaitingPayment.
              En otros stages NO aplicar.
            - user_says_already_paid: "ya pagué", "ya hice el pago", "ya transferí", "listo ya pagué", "acabo de pagar",
              "el pago ya está hecho" — solo cuando Stage=AwaitingPayment. Indica que verificaremos con la plataforma.

            ## Ambigüedades (tipos):
            - temporal: "pronto", "luego", "otro día" — sin fecha concreta.
            - referential: referencia que NO se puede resolver desde el historial (ej. "ese plan" sin que el asistente
              haya mencionado ningún servicio). Si el historial provee un referente claro → resolver en extracted_fields, NO ambigüedad.
            - multiple_values: "Mateo o Lucas" — más de una opción.
            - incomplete: "el lunes" — incompleto sin saber cuál.
            """;
    }
}
