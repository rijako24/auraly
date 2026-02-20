using MimosBabySpa.IntegrationTests.Infrastructure;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

// ─────────────────────────────────────────────────────────────────────────────
// 20 CONVERSACIONES COMPLETAS: desde inicio hasta reserva.
// Estilos variados para detectar bugs en: datos requeridos, add-ons, formulario
// de confirmación y creación de reserva.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// 1. Estilo formal — datos completos en mensaje estructurado
/// </summary>
public class FullReservationStyle1FormalScenario : TestScenario
{
    public override string Id => "full_1_formal";
    public override string Description => "Estilo formal: datos completos, lenguaje cortés.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Buenos días, me gustaría reservar un Plan Marineritos para el 2025-08-15 a las 10:00. Mi nombre es María González.",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-15", "confidence": 0.99},
                {"field": "DesiredTime", "value": "10:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "María González", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new(
            "Perfecto, confirmo la reserva con esos datos.",
            """
            {
              "extracted_fields": [],
              "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "reserva")
    ];
}

/// <summary>
/// 2. Estilo coloquial — mensajes cortos, lenguaje casual
/// </summary>
public class FullReservationStyle2ColloquialScenario : TestScenario
{
    public override string Id => "full_2_colloquial";
    public override string Description => "Estilo coloquial: mensajes breves, lenguaje casual.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Oye quiero el Plan Post Vacunas mañana a las 3pm, soy Ana",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Post Vacunas", "confidence": 0.97},
                {"field": "DesiredDate", "value": "2025-08-16", "confidence": 0.95},
                {"field": "DesiredTime", "value": "15:00", "confidence": 0.96},
                {"field": "CustomerName", "value": "Ana", "confidence": 0.90}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Dale confirma", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 3. Todo en un solo mensaje inicial
/// </summary>
public class FullReservationStyle3AllInOneScenario : TestScenario
{
    public override string Id => "full_3_all_in_one";
    public override string Description => "Usuario proporciona todo en un solo mensaje.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Hola quiero Plan Aventuras Marinas 2025-08-20 14:00 soy Carlos Ruiz",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Aventuras Marinas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-20", "confidence": 0.99},
                {"field": "DesiredTime", "value": "14:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Carlos Ruiz", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirma", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 4. Flujo conversacional con múltiples intercambios
/// </summary>
public class FullReservationStyle4ConversationalScenario : TestScenario
{
    public override string Id => "full_4_conversational";
    public override string Description => "Flujo conversacional con varios intercambios.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Hola qué servicios tienen?", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": false, "is_information_query": true, "user_wants_to_cancel": false}, "ambiguities": []}""", ""),
        new(
            "Quiero Plan Marineritos para el 2025-08-18 a las 11am, soy Patricia",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-18", "confidence": 0.99},
                {"field": "DesiredTime", "value": "11:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Patricia", "confidence": 0.92}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Sí confirmo la reserva", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 5. Con correcciones de fecha
/// </summary>
public class FullReservationStyle5DateCorrectionsScenario : TestScenario
{
    public override string Id => "full_5_date_corrections";
    public override string Description => "Usuario corrige fecha durante la conversación.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Marineritos para hoy a las 9am",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-14", "confidence": 0.90},
                {"field": "DesiredTime", "value": "09:00", "confidence": 0.97}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            ""),
        new(
            "Mejor mañana 2025-08-15 a las 11, soy Diego",
            """
            {
              "extracted_fields": [
                {"field": "DesiredDate", "value": "2025-08-15", "confidence": 0.99},
                {"field": "DesiredTime", "value": "11:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Diego", "confidence": 0.92}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirmo", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 6. Plan Deluxe CON add-on — valida oferta y selección de add-on
/// </summary>
public class FullReservationStyle6WithAddOnScenario : TestScenario
{
    public override string Id => "full_6_with_addon";
    public override string Description => "Plan Deluxe con add-on: valida oferta y selección.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation", "ReservationMustIncludeAddOns"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Quiero Plan Deluxe 2025-08-15 a las 10am, soy Laura",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Deluxe", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-15", "confidence": 0.99},
                {"field": "DesiredTime", "value": "10:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Laura", "confidence": 0.93}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new(
            "Sí agrega el Masaje Extra 15m y confirma",
            """
            {
              "extracted_fields": [{"field": "SelectedAddOns", "value": "Masaje Extra 15m", "confidence": 0.95}],
              "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "reserva")
    ];
}

/// <summary>
/// 7. Sin add-on explícito — Plan Deluxe, usuario rechaza add-ons
/// </summary>
public class FullReservationStyle7NoAddOnScenario : TestScenario
{
    public override string Id => "full_7_no_addon";
    public override string Description => "Plan Deluxe, usuario rechaza add-ons explícitamente.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Deluxe 2025-08-17 14:00, no quiero add-ons, soy Ricardo",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Deluxe", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-17", "confidence": 0.99},
                {"field": "DesiredTime", "value": "14:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Ricardo", "confidence": 0.92}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirmo", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 8. Respuestas de una palabra
/// </summary>
public class FullReservationStyle8OneWordScenario : TestScenario
{
    public override string Id => "full_8_one_word";
    public override string Description => "Usuario responde con una palabra a la vez.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Post Vacunas 2025-08-19 10am María López",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Post Vacunas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-19", "confidence": 0.99},
                {"field": "DesiredTime", "value": "10:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "María López", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Sí", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 9. Con email opcional
/// </summary>
public class FullReservationStyle9WithEmailScenario : TestScenario
{
    public override string Id => "full_9_with_email";
    public override string Description => "Usuario incluye email en los datos.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Hola soy Carmen, carmen@email.com. Quiero Plan Marineritos 2025-08-21 11am",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-21", "confidence": 0.99},
                {"field": "DesiredTime", "value": "11:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Carmen", "confidence": 0.93},
                {"field": "Email", "value": "carmen@email.com", "confidence": 0.90}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirmo la reserva", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 10. Fecha lejana
/// </summary>
public class FullReservationStyle10FutureDateScenario : TestScenario
{
    public override string Id => "full_10_future_date";
    public override string Description => "Reserva para fecha en el futuro lejano.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Quiero reservar Plan Aventuras Marinas para el 2025-09-10 a las 2pm, soy Pablo Martín",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Aventuras Marinas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-09-10", "confidence": 0.99},
                {"field": "DesiredTime", "value": "14:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Pablo Martín", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Perfecto confirmo", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 11. Mensaje largo y detallado
/// </summary>
public class FullReservationStyle11LongMessageScenario : TestScenario
{
    public override string Id => "full_11_long_message";
    public override string Description => "Usuario envía mensaje largo con todos los detalles.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Buenos días, les escribo porque he visto su página y me interesa mucho. Tengo una bebé de 7 meses y quiero reservar el Plan Marineritos para el próximo viernes 2025-08-22 a las 10 de la mañana. Me llamo Claudia Vega.",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-22", "confidence": 0.99},
                {"field": "DesiredTime", "value": "10:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Claudia Vega", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirmo la reserva con esos datos por favor", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 12. Usuario impaciente
/// </summary>
public class FullReservationStyle12ImpatientScenario : TestScenario
{
    public override string Id => "full_12_impatient";
    public override string Description => "Usuario impaciente, mensajes urgentes.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Reserva ya Plan Post Vacunas mañana 4pm Pedro Díaz",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Post Vacunas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-16", "confidence": 0.95},
                {"field": "DesiredTime", "value": "16:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Pedro Díaz", "confidence": 0.93}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirma ya", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 13. Cambio de servicio antes de confirmar
/// </summary>
public class FullReservationStyle13ServiceChangeScenario : TestScenario
{
    public override string Id => "full_13_service_change";
    public override string Description => "Usuario cambia de servicio durante la conversación.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Quiero Plan Aventuras Marinas 2025-08-18 9am",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Aventuras Marinas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-18", "confidence": 0.99},
                {"field": "DesiredTime", "value": "09:00", "confidence": 0.97}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new(
            "Mejor Plan Marineritos, soy Fernando Castro",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "CustomerName", "value": "Fernando Castro", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": false, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            ""),
        new("Sí confirmo", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 14. Hora con minutos (11:30)
/// </summary>
public class FullReservationStyle14TimeWithMinutesScenario : TestScenario
{
    public override string Id => "full_14_time_minutes";
    public override string Description => "Hora con minutos (11:30).";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Marineritos 2025-08-20 11:30 soy Roberto Sánchez",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-20", "confidence": 0.99},
                {"field": "DesiredTime", "value": "11:30", "confidence": 0.97},
                {"field": "CustomerName", "value": "Roberto Sánchez", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Adelante confirma", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 15. Pregunta por horarios antes de especificar
/// </summary>
public class FullReservationStyle15AskFirstScenario : TestScenario
{
    public override string Id => "full_15_ask_first";
    public override string Description => "Usuario pregunta por horarios antes de especificar.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Qué horarios tienen mañana?", """{"extracted_fields": [{"field": "DesiredDate", "value": "2025-08-16", "confidence": 0.85}], "intentions": {"user_requested_availability": false, "user_confirmed_booking": false, "is_information_query": true, "user_wants_to_cancel": false}, "ambiguities": []}""", ""),
        new(
            "A las 3pm para Plan Post Vacunas, soy Juan Pérez",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Post Vacunas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-16", "confidence": 0.99},
                {"field": "DesiredTime", "value": "15:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Juan Pérez", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirmo", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 16. Plan Deluxe con add-on en segundo mensaje
/// </summary>
public class FullReservationStyle16DeluxeAddOnTwoStepsScenario : TestScenario
{
    public override string Id => "full_16_deluxe_addon_2steps";
    public override string Description => "Plan Deluxe: add-on en segundo paso.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation", "ReservationMustIncludeAddOns"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Deluxe 2025-08-25 10am Sandra Torres",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Deluxe", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-25", "confidence": 0.99},
                {"field": "DesiredTime", "value": "10:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Sandra Torres", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new(
            "Sí quiero Masaje Extra 15m. Confirmo.",
            """
            {
              "extracted_fields": [{"field": "SelectedAddOns", "value": "Masaje Extra 15m", "confidence": 0.95}],
              "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "reserva")
    ];
}

/// <summary>
/// 17. Confirmación con sinónimos
/// </summary>
public class FullReservationStyle17ConfirmationSynonymsScenario : TestScenario
{
    public override string Id => "full_17_confirmation_synonyms";
    public override string Description => "Usuario confirma con varias formas (adelante, procede).";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Aventuras Marinas 2025-08-22 12pm soy Lucía Mendoza",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Aventuras Marinas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-22", "confidence": 0.99},
                {"field": "DesiredTime", "value": "12:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Lucía Mendoza", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Adelante hazlo", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 18. Nombre con apellido compuesto
/// </summary>
public class FullReservationStyle18CompoundNameScenario : TestScenario
{
    public override string Id => "full_18_compound_name";
    public override string Description => "Nombre con apellido compuesto.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Post Vacunas 2025-08-23 9am, María José García López",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Post Vacunas", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-23", "confidence": 0.99},
                {"field": "DesiredTime", "value": "09:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "María José García López", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Procede con la reserva", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 19. Flujo mínimo válido
/// </summary>
public class FullReservationStyle19MinimalScenario : TestScenario
{
    public override string Id => "full_19_minimal";
    public override string Description => "Flujo mínimo: datos esenciales, 2 mensajes.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            "Plan Marineritos 2025-08-24 10:00 Ana",
            """
            {
              "extracted_fields": [
                {"field": "Service", "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-24", "confidence": 0.99},
                {"field": "DesiredTime", "value": "10:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Ana", "confidence": 0.90}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Ok", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}

/// <summary>
/// 20. Flujo completo con 4 pasos
/// </summary>
public class FullReservationStyle20FourStepsScenario : TestScenario
{
    public override string Id => "full_20_four_steps";
    public override string Description => "Flujo en 4 pasos: info, reserva, disponibilidad, confirmar.";
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
        ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "BotMustNotInventTimeSlots", "NoDuplicateReservation"];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new("Hola", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": false, "is_information_query": true, "user_wants_to_cancel": false}, "ambiguities": []}""", ""),
        new(
            "Quiero Plan Marineritos",
            """
            {
              "extracted_fields": [{"field": "Service", "value": "Plan Marineritos", "confidence": 0.98}],
              "intentions": {"user_requested_availability": false, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            ""),
        new(
            "Para 2025-08-26 a las 11am, soy Elena Ruiz",
            """
            {
              "extracted_fields": [
                {"field": "DesiredDate", "value": "2025-08-26", "confidence": 0.99},
                {"field": "DesiredTime", "value": "11:00", "confidence": 0.97},
                {"field": "CustomerName", "value": "Elena Ruiz", "confidence": 0.95}
              ],
              "intentions": {"user_requested_availability": true, "user_confirmed_booking": false, "is_information_query": false, "user_wants_to_cancel": false},
              "ambiguities": []
            }
            """,
            "disponib"),
        new("Confirmo", """{"extracted_fields": [], "intentions": {"user_requested_availability": false, "user_confirmed_booking": true, "is_information_query": false, "user_wants_to_cancel": false}, "ambiguities": []}""", "reserva")
    ];
}
