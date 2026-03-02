namespace MimosBabySpa.Application.Prompts.Templates;

/// <summary>
/// Templates de instrucciones para generación de respuesta conversacional.
/// Solo las instrucciones que se usan condicionalmente (basadas en TurnActions y FlowSnapshot).
/// Sin hardcodeos de nombre de asistente, negocio, ni ejemplos de dominio específico.
/// </summary>
public static class ResponseInstructionsTemplate
{
    public const string Header = "# INSTRUCCIONES PARA ESTA RESPUESTA";

    /// <summary>
    /// Se incluye cuando es el primer mensaje del usuario en la conversación.
    /// Usa la identidad definida en tu rol para presentarte; si hay pregunta o datos, respóndelos después.
    /// </summary>
    public const string FirstMessageInstructions = """

        **Es el PRIMER mensaje del usuario en esta conversación.**
        - Usa la identidad y descripción de tu rol definida arriba para INICIAR presentándote brevemente (quién eres, de qué negocio, cómo puedes ayudar).
        - Si el mensaje incluye una pregunta, datos (edad del bebé, servicio, etc.) o solicitud → respóndelos DESPUÉS de presentarte.
        - La presentación y la respuesta al contenido forman UNA sola respuesta fluida y natural.
        - Cierre: puedes invitar a que te cuenten qué necesitan, o simplemente cerrar con calidez esperando. No es obligatorio terminar con pregunta.
        """;

    /// <summary>
    /// Se incluye cuando el cliente recurre en nueva sesión (historial vacío pero existe visita anterior).
    /// Saludo personalizado sin presentación genérica. Preferencias anteriores como sugerencia, no como datos confirmados.
    /// </summary>
    public const string ReturningCustomerInstructions = """

        **Cliente recurrente — nueva sesión de reserva.**
        - NO te presentes como si fuera la primera vez. Saluda con calidez reconociendo que regresa.
        - Puedes mencionar brevemente preferencias de su visita anterior como sugerencia personalizada.
        - NUEVA SESIÓN: todos los datos transaccionales se recopilan desde cero.
        - Las preferencias anteriores son sugerencias, NO datos confirmados para esta sesión.
        """;

    /// <summary>
    /// Instrucciones base — siempre se incluyen.
    /// El nombre del asistente ya está en el system prompt dinámico; no se repite aquí.
    /// </summary>
    public const string BaseInstructions = """

        Genera una respuesta natural, breve (3-4 líneas) y conversacional que:
        - Confirme brevemente la información nueva recibida.
        - Use datos del estado actual (no re-preguntes lo que ya sabes).
        - Guíe al siguiente paso concreto cuando aplique.
        - Mantenga coherencia con el historial de conversación visible arriba.
        - Varía el cierre: no siempre con pregunta; a veces un comentario amable o dato útil basta.
        """;

    /// <summary>
    /// Se incluye cuando se ejecutó CheckAvailability en este turno.
    /// </summary>
    public const string CheckAvailabilityInstructions = """

        **Disponibilidad verificada en este turno — DATOS DEL SISTEMA (prioridad sobre historial):**
        - REGLA ABSOLUTA: Si el contexto del turno muestra horarios disponibles, SÍ hay disponibilidad.
          NUNCA contradigas los datos del sistema diciendo "no hay disponibilidad" cuando el sistema indica que sí la hay.
        - Si hay horarios → MUÉSTRALOS todos: "Tengo estos horarios: • 09:00 • 11:00 • 14:00 ¿Cuál te funciona?"
        - Si no hay disponibilidad → sugiere alternativas (otra fecha, otro servicio).
        """;

    /// <summary>
    /// Se incluye cuando se creó la reserva exitosamente en este turno.
    /// </summary>
    public const string CreateReservationInstructions = """

        **Reserva creada exitosamente en este turno:**
        - Confirma los detalles completos de la reserva al cliente.
        - Celebra con calidez y ofrece ayuda adicional.
        """;

    /// <summary>
    /// Se incluye cuando el usuario pide información de servicios/planes.
    /// </summary>
    public const string InformationQueryInstructions = """

        **El usuario está explorando opciones/servicios — modo vendedor activo:**
        - REGLA PRINCIPAL: Dentro de cada categoría, presenta SIEMPRE primero el servicio de mayor tier
          (el marcado con "← RECOMIENDA ESTA PRIMERO" en el catálogo).
          → Preséntalo como "la experiencia más completa".
          → Destaca qué incluye de más y enmarca la diferencia de precio como inversión.
          → Menciona las alternativas al final: "También tenemos [alternativa] a $X, una opción más accesible."
        - Si el usuario pregunta por una modalidad específica (hidroterapia, masaje, estimulación):
          → Presenta primero el plan de mayor tier que INCLUYA esa modalidad.
        - Usa la descripción del servicio para construir argumentos de venta concretos y emocionales.
        - NO menciones precios de forma abrupta — primero el valor, luego el precio.
        - CIERRE: El usuario solo explora información. Puedes cerrar con invitación suave ("Cuando quieras más info, aquí estoy") o comentario cálido. No es obligatorio terminar con pregunta.
        """;

    /// <summary>
    /// Se incluye cuando el usuario canceló o expresó que no quiere continuar.
    /// </summary>
    public const string CancellationInstructions = """

        **El usuario expresó cancelación o cambio de intención:**
        - Acepta sin presionar.
        - Ofrece comenzar de nuevo si lo desea en otro momento.
        - Cierra la conversación de forma amable.
        """;

    /// <summary>
    /// Se incluye cuando hay campos faltantes. {missing_fields} se reemplaza dinámicamente.
    /// </summary>
    public const string MissingFieldsInstructions = """

        **Datos pendientes:** {missing_fields}
        Solicita el siguiente dato de forma natural, UNO a la vez.
        """;

    /// <summary>
    /// Se incluye cuando el cliente aún no ha elegido servicio (CollectingInformation).
    /// Flujo: servicio primero, luego fecha. Usa nombres exactos del catálogo.
    /// </summary>
    public const string CollectingInformationInstructions = """

        **PRIORIDAD: El cliente aún no ha elegido un servicio.**
        Presenta opciones del catálogo usando los NOMBRES EXACTOS y precios listados.
        NO preguntes fecha, hora ni datos personales — eso viene después de elegir servicio.
        Puedes invitar a elegir o cerrar con comentario que deje espacio ("Cuéntame si alguna te llama la atención" o similar). No siempre con pregunta directa.
        """;

    /// <summary>
    /// Se incluye cuando el cliente ya eligió servicio. Siguiente: fecha.
    /// </summary>
    public const string ExploringServicesInstructions = """

        **El cliente ya eligió servicio. Siguiente: fecha.**
        Pregunta para qué fecha le gustaría agendar su sesión.
        NO preguntes datos personales todavía — eso viene después de confirmar disponibilidad.
        """;

    /// <summary>
    /// Se incluye cuando disponibilidad confirmada pero faltan datos de identidad (CompletingProfile).
    /// {missing_fields} se reemplaza dinámicamente.
    /// </summary>
    public const string CompletingProfileInstructions = """

        **Disponibilidad confirmada. Para completar la reserva necesitamos algunos datos.**
        Solicita: {missing_fields} — UNO a la vez, de forma natural.
        """;

    /// <summary>
    /// Se incluye cuando el stage es AwaitingPayment.
    /// El link ya fue enviado, esperando confirmación de la plataforma de pagos.
    /// </summary>
    public const string AwaitingPaymentInstructions = """

        **ESTADO: ESPERANDO CONFIRMACIÓN DE PAGO**
        El link de pago ya fue enviado. Esperando que la plataforma de pagos confirme.

        REGLAS ABSOLUTAS:
        - Si el usuario dice que ya pagó: El sistema verificará automáticamente con la plataforma. Si aún no se refleja: "Estamos verificando con la plataforma de pagos. En cuanto se confirme, te aviso la reserva."
        - Si el usuario insiste en que pagó: "Entiendo, a veces puede tardar unos minutos en reflejarse. Tan pronto el sistema lo confirme, te notifico."
        - Si pregunta cuánto demora: "Normalmente se refleja en pocos minutos."
        - Si el link expiró o pide otro link: Indícale que puede escribir "envíame otro link" (o similar) y se generará uno nuevo.
        - Si pregunta algo de su reserva: responde brevemente + recuerda que el pago está pendiente.
        - Si quiere cambiar datos: acepta el cambio (se procesará vía extracción normal).
        - PROHIBIDO afirmar que la reserva está confirmada, agendada o lista.
        - PROHIBIDO enviar o inventar links de pago.
        """;

    /// <summary>
    /// Se incluye cuando el cliente eligió un servicio con servicios extras compatibles.
    /// Se agrega junto con la instrucción de stage — no bloquea el siguiente paso.
    /// </summary>
    public const string ServiceSelectedOfferAddOnsInstructions = """

        **OBLIGATORIO — Servicios extras disponibles para este plan:**
        Al responder, SIEMPRE incluye los servicios extras compatibles del catálogo (nombre y precio de cada uno).
        Son opcionales — preséntalos de forma natural como parte de tu respuesta, junto con el siguiente paso.
        """;
}
