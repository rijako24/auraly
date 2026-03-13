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
                  exacto del catálogo (servicio, servicio extra, horario, etc.). Aplica a CUALQUIER campo.
                  Ej: Bot ofrece "Plan Marineritos" → usuario: "sí, ese plan" → Service: "Plan Marineritos"
                  Ej: Bot muestra "Decoración Sencilla" y "Bouquet" → usuario: "la sencilla" → Attribute:SelectedAddOns: "Decoración Sencilla"
                  Ej: Bot muestra horarios 09:00, 11:00 → usuario: "a las 9" → DesiredTime: "09:00"
               c) Resolución contra catálogo: el usuario menciona un servicio por nombre parcial, abreviación o variación
                  ("post vacuna", "marineritos", "el plan de vacunas"). Resolver al nombre exacto del catálogo en la tabla
                  de campos disponibles. Confidence: 0.90 si el match es inequívoco, 0.70 si hay múltiples candidatos.
                  IMPORTANTE — Service y Attribute:SelectedAddOns: Extraer cuando el usuario use lenguaje de SELECCIÓN explícita
                  ("quiero X", "mejor el Y", "prefiero X", "la primera", "ese", "esa", "sí ese", "no el otro").
                  Si is_information_query=true, NO extraer Service NI SelectedAddOns, salvo que el usuario use lenguaje de selección explícita
                  o consulte disponibilidad. Preguntas informativas ("qué incluye...", "cuánto cuesta...", "háblame del bouquet")
                  sin selección → is_information_query=true y NO extraer campos de selección.
                  Afirmación vaga sin nombrar add-on ("sí", "ok", "está bien") cuando el bot mostró varios → NO extraer SelectedAddOns.
               d) Confidence: 0.92 si el referente es inequívoco en el historial. 0.70 si hay múltiples candidatos.
               e) Si el usuario claramente NO responde (cambia de tema) → no forzar extracción.
            5. Fechas temporales (mapeo obligatorio):
               - "hoy" → {now:yyyy-MM-dd} | "mañana" → {tomorrow:yyyy-MM-dd} | "pasado mañana" → {now.AddDays(2):yyyy-MM-dd}
               - Días de semana → próxima ocurrencia futura.
               - Número de día solo ("el 29", "para el 15") → próxima ocurrencia futura (mes actual si el día aún no pasó, mes siguiente si ya pasó).
               - Si el usuario pide disponibilidad/horarios mencionando una fecha (incluso "¿y para mañana?")
                 → extraer DesiredDate + marcar user_requested_availability=true.
            6. SEPARACIÓN ESTRICTA campos / intenciones: Los nombres ({intentionNames})
               pertenecen EXCLUSIVAMENTE al bloque "intentions". NUNCA los incluyas en "extracted_fields".
            7. ALCANCE: Extraer SOLO del último mensaje del usuario (delimitado con ---MENSAJE A ANALIZAR---).
                El historial es CONTEXTO para resolver referencias, NO fuente de datos a re-extraer.
                Si el usuario MENCIONA un campo en su mensaje (servicio, fecha, hora, nombre, etc.) → SIEMPRE extraerlo,
                incluso si ese campo ya tiene valor en "Estado actual" (sea igual o distinto). El backend maneja la idempotencia.
                Si el usuario NO menciona un campo → NO extraerlo del estado ni del historial.
                Ej: Estado Service="Plan Marineritos" + usuario "y para Plan Aventuras Marinas" → Service: "Plan Aventuras Marinas"
                Ej: Estado Service="Plan Marineritos" + usuario "ok a las 9" → NO extraer Service (no lo mencionó)
                EXCEPCIÓN CSV — Atributos multi-valor: Si el estado ya tiene valor y el usuario MODIFICA (agrega, quita o reemplaza),
                extraer el resultado COMPLETO (CSV actualizado), no solo el fragmento mencionado.
                Ej: Estado SelectedAddOns="Decoración Sencilla" + usuario "también el bouquet" → "Decoración Sencilla, Bouquet"
                Ej: Estado SelectedAddOns="Decoración Sencilla, Bouquet" + usuario "quita el bouquet" → "Decoración Sencilla"
                REEMPLAZAR: "mejor el bouquet", "prefiero la decoración sencilla", "no, el otro", "cambiemos a X"
                  → extraer el valor NUEVO completo (reemplaza el anterior). Ej: "mejor el bouquet" → SelectedAddOns: "Bouquet"
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
            - user_wants_human_assistance: "quiero hablar con una persona", "necesito un humano", "pásame con un agente",
              "quiero hablar con alguien real", "necesito un asesor" — solicitud explícita de redirección a humano.
            - user_wants_to_reschedule: "quiero cambiar el horario", "mejor otro día", "cambiar la cita", "reagendar",
              "para otra hora", "otra fecha por favor", "mejor a las 3" — solo cuando Stage=BookingCompleted (reserva ya creada).
            - user_wants_to_hold: "no puede asistir", "avisa ella", "mejor que me avise", "dejamos pendiente",
              "no voy a poder ir", "que me avisen cuando pueda" — reserva creada pero cliente no puede asistir ahora.

            ## Ambigüedades (tipos):
            - temporal: "pronto", "luego", "otro día" — sin fecha concreta.
            - referential: referencia que NO se puede resolver NI desde el historial NI desde el catálogo de servicios.
              Si el historial o el catálogo provee un referente claro (ej. "post vacuna" → "Plan Post Vacunas")
              → resolver en extracted_fields, NO ambigüedad.
            - multiple_values: "Mateo o Lucas" — más de una opción.
            - incomplete: "a la tarde" — hora sin especificar.
            """;
    }
}
