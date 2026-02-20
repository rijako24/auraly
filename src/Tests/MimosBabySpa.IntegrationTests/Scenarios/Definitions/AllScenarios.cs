using MimosBabySpa.IntegrationTests.Infrastructure;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

// ─────────────────────────────────────────────────────────────────────────────
// ESCENARIO 1: Reserva Exitosa
// El cliente proporciona todos los datos, hay disponibilidad, y confirma.
// ─────────────────────────────────────────────────────────────────────────────

public class SuccessfulReservationScenario : TestScenario
{
    public override string Id          => "test_reserva_exitosa";
    public override string Description => "El cliente proporciona todos los datos, hay disponibilidad y confirma la reserva exitosamente.";
    public override CalendarMode CalendarMode     => CalendarMode.Available;
    public override ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public override bool ExpectReservationCreated  => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "ReservationMustCallCreateReservation",
        "CheckAvailabilityBeforeCreateReservation",
        "BotMustNotInventTimeSlots",
        "NoDuplicateReservation"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage:   "Hola, quiero reservar un Plan Marineritos para el 2025-08-15 a las 10am para mi bebé Lucía.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "Service",     "value": "Plan Marineritos",   "confidence": 0.98},
                {"field": "DesiredDate", "value": "2025-08-15",         "confidence": 0.99},
                {"field": "DesiredTime", "value": "10:00",              "confidence": 0.97},
                {"field": "CustomerName","value": "Lucía (mamá)",       "confidence": 0.90}
              ],
              "intentions": {
                "user_requested_availability": true,
                "user_confirmed_booking":      false,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage:   "Sí, confirmo el horario de las 10am.",
            ExtractionJson: """
            {
              "extracted_fields": [],
              "intentions": {
                "user_requested_availability": false,
                "user_confirmed_booking":      true,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "reserva")
    ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ESCENARIO 2: Sin Disponibilidad
// El cliente pide una fecha sin horarios disponibles.
// ─────────────────────────────────────────────────────────────────────────────

public class NoAvailabilityScenario : TestScenario
{
    public override string Id          => "test_sin_disponibilidad";
    public override string Description => "El cliente solicita una fecha sin disponibilidad. El bot debe informar y NO crear reserva.";
    public override CalendarMode CalendarMode     => CalendarMode.NoSlots;
    public override ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public override bool ExpectReservationCreated  => false;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "CheckAvailabilityBeforeCreateReservation",
        "BotMustNotInventTimeSlots",
        "NoDuplicateReservation"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage:   "Quiero un Plan Post Vacunas el 2025-08-20 a las 9am.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "Service",     "value": "Plan Post Vacunas", "confidence": 0.97},
                {"field": "DesiredDate", "value": "2025-08-20",        "confidence": 0.99},
                {"field": "DesiredTime", "value": "09:00",             "confidence": 0.95}
              ],
              "intentions": {
                "user_requested_availability": true,
                "user_confirmed_booking":      false,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "disponible")
    ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ESCENARIO 3: Confirmación sin verificación de disponibilidad
// El bot intenta crear la reserva sin haber verificado disponibilidad antes.
// ─────────────────────────────────────────────────────────────────────────────

public class ConfirmationWithoutAvailabilityScenario : TestScenario
{
    public override string Id          => "test_confirmacion_sin_llamada";
    public override string Description => "Se valida que el bot no puede crear reserva si no verificó disponibilidad primero.";
    public override CalendarMode CalendarMode     => CalendarMode.NoSlots;
    public override ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public override bool ExpectReservationCreated  => false;
    public override bool ExpectAvailabilityChecked => false;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "NoConfirmationWithoutAvailabilityCheck",
        "BotMustNotInventTimeSlots"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage:   "Confirmo la reserva para el Plan Marineritos el lunes.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "Service",     "value": "Plan Marineritos", "confidence": 0.90}
              ],
              "intentions": {
                "user_requested_availability": false,
                "user_confirmed_booking":      true,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "fecha")
    ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ESCENARIO 4: Reserva Doble
// El sistema detecta duplicados y no crea la segunda reserva.
// ─────────────────────────────────────────────────────────────────────────────

public class DoubleBookingScenario : TestScenario
{
    public override string Id          => "test_doble_reserva";
    public override string Description => "Se intenta crear dos reservas en el mismo horario. El sistema debe rechazar la segunda.";
    public override CalendarMode CalendarMode     => CalendarMode.Available;
    public override ReservationMode ReservationMode => ReservationMode.TrackDuplicates;
    public override bool ExpectReservationCreated  => true;  // first one succeeds
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "ReservationMustCallCreateReservation",
        "CheckAvailabilityBeforeCreateReservation",
        "NoDuplicateReservation"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage:   "Quiero Plan Marineritos el 2025-08-15 a las 10am.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "Service",      "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate",  "value": "2025-08-15",       "confidence": 0.99},
                {"field": "DesiredTime",  "value": "10:00",            "confidence": 0.97},
                {"field": "CustomerName", "value": "Cliente Test",     "confidence": 0.90}
              ],
              "intentions": {
                "user_requested_availability": true,
                "user_confirmed_booking":      false,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """),
        new(
            UserMessage:   "Confirmo.",
            ExtractionJson: """
            {
              "extracted_fields": [],
              "intentions": {
                "user_requested_availability": false,
                "user_confirmed_booking":      true,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """)
    ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ESCENARIO 5: Error del Backend de Calendario
// El servicio de disponibilidad lanza una excepción.
// ─────────────────────────────────────────────────────────────────────────────

public class BackendCalendarErrorScenario : TestScenario
{
    public override string Id          => "test_error_backend_calendar";
    public override string Description => "El servicio de disponibilidad lanza una excepción. El orquestador debe manejarla con gracia.";
    public override CalendarMode CalendarMode     => CalendarMode.ThrowError;
    public override ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public override bool ExpectReservationCreated  => false;
    public override bool ExpectAvailabilityChecked => false;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "BotMustNotInventTimeSlots",
        "NoDuplicateReservation"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage:   "Quiero reservar Plan Aventuras Marinas para el 2025-09-01 a las 11am.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "Service",     "value": "Plan Aventuras Marinas", "confidence": 0.97},
                {"field": "DesiredDate", "value": "2025-09-01",             "confidence": 0.99},
                {"field": "DesiredTime", "value": "11:00",                  "confidence": 0.95}
              ],
              "intentions": {
                "user_requested_availability": true,
                "user_confirmed_booking":      false,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "")
    ];
}

// ─────────────────────────────────────────────────────────────────────────────
// ESCENARIO 6: Usuario Cambia de Fecha
// El usuario especifica una fecha, el sistema verifica, luego el usuario cambia.
// ─────────────────────────────────────────────────────────────────────────────

public class UserChangesDateScenario : TestScenario
{
    public override string Id          => "test_usuario_cambia_fecha";
    public override string Description => "El usuario cambia la fecha de reserva. El sistema debe re-verificar disponibilidad.";
    public override CalendarMode CalendarMode     => CalendarMode.Available;
    public override ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public override bool ExpectReservationCreated  => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "ReservationMustCallCreateReservation",
        "CheckAvailabilityBeforeCreateReservation",
        "BotMustNotInventTimeSlots",
        "NoDuplicateReservation"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage:   "Quiero Plan Marineritos el 2025-08-15 a las 9am.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "Service",      "value": "Plan Marineritos", "confidence": 0.98},
                {"field": "DesiredDate",  "value": "2025-08-15",       "confidence": 0.99},
                {"field": "DesiredTime",  "value": "09:00",            "confidence": 0.95},
                {"field": "CustomerName", "value": "Cliente Test",     "confidence": 0.90}
              ],
              "intentions": {
                "user_requested_availability": true,
                "user_confirmed_booking":      false,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage:   "En realidad prefiero el 2025-08-22 a las 11am.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "DesiredDate", "value": "2025-08-22", "confidence": 0.99},
                {"field": "DesiredTime", "value": "11:00",      "confidence": 0.97}
              ],
              "intentions": {
                "user_requested_availability": true,
                "user_confirmed_booking":      false,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage:   "Perfecto, confirmo el 22 de agosto.",
            ExtractionJson: """
            {
              "extracted_fields": [],
              "intentions": {
                "user_requested_availability": false,
                "user_confirmed_booking":      true,
                "is_information_query":        false,
                "user_wants_to_cancel":        false
              },
              "ambiguities": []
            }
            """,
            ExpectedBotResponseContains: "reserva")
    ];
}
