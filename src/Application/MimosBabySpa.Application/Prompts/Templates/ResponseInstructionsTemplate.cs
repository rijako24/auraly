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
    /// Instrucciones base — siempre se incluyen.
    /// El nombre del asistente ya está en el system prompt dinámico; no se repite aquí.
    /// </summary>
    public const string BaseInstructions = """

        Genera una respuesta natural, breve (3-4 líneas) y conversacional que:
        - Confirme brevemente la información nueva recibida.
        - Use datos del estado actual (no re-preguntes lo que ya sabes).
        - Guíe al siguiente paso concreto.
        - Mantenga coherencia con el historial de conversación visible arriba.
        """;

    /// <summary>
    /// Se incluye cuando se ejecutó CheckAvailability en este turno.
    /// </summary>
    public const string CheckAvailabilityInstructions = """

        **Disponibilidad verificada en este turno:**
        - Si hay horarios disponibles → MUÉSTRALOS todos explícitamente (no solo "hay disponibilidad").
        - Formato recomendado: "Tengo estos horarios: • 09:00 • 11:00 • 14:00 • 16:00 ¿Cuál te funciona?"
        - Si no hay disponibilidad → sugiere alternativas (otra fecha, otro servicio similar).
        """;

    /// <summary>
    /// Se incluye cuando el usuario seleccionó un horario pero aún no confirmó la reserva.
    /// </summary>
    public const string TimeSelectedInstructions = """

        **El usuario seleccionó un horario:**
        - Confirma el horario elegido.
        - Pregunta EXPLÍCITAMENTE si desea confirmar la reserva.
        - NO digas que "vas a confirmar" hasta que el usuario diga "sí".
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

        **El usuario está explorando opciones/servicios:**
        - Muestra los servicios disponibles de forma atractiva y contextualizada.
        - NO presiones para reservar inmediatamente.
        - Al finalizar, pregunta si hay algo más en lo que puedas ayudar o si le gustaría reservar.
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
    /// Se incluye cuando hay ambigüedades detectadas.
    /// </summary>
    public const string AmbiguitiesInstructions = """

        **Hay información ambigua que necesita clarificación:**
        Haz UNA pregunta concreta para resolver la ambigüedad antes de continuar.
        """;
}
