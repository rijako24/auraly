using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Scenarios;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

// -----------------------------------------------------------------------------
// Escenario 1: Reserva Exitosa
// -----------------------------------------------------------------------------

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
            UserMessage: "Hola, quiero reservar un Plan Marineritos para el 2026-08-15 a las 10am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"10:00"}""",
                "Hay disponibilidad el 15 de agosto a las 10:00. Confirmamos la reserva?"),
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage: "Si, confirmo el horario de las 10am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"10:00","customer_name":"Lucia","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Tu reserva ha sido creada exitosamente! Te esperamos el 15 de agosto."),
            ExpectedBotResponseContains: "reserva")
    ];
}

// -----------------------------------------------------------------------------
// Escenario 2: Sin Disponibilidad
// -----------------------------------------------------------------------------

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
            UserMessage: "Quiero un Plan Post Vacunas el 2026-08-20 a las 9am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Post Vacunas","date":"2026-08-20","time":"09:00"}""",
                "Lo siento, no hay disponibilidad para esa fecha y hora. Te gustaria elegir otra fecha?"),
            ExpectedBotResponseContains: "")
    ];
}

// -----------------------------------------------------------------------------
// Escenario 3: Confirmacion sin verificar disponibilidad
// El bot intenta confirmar sin haber verificado disponibilidad primero.
// -----------------------------------------------------------------------------

public class ConfirmationWithoutAvailabilityScenario : TestScenario
{
    public override string Id          => "test_confirmacion_sin_llamada";
    public override string Description => "Se valida que el bot no crea reserva sin verificar disponibilidad primero.";
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
            UserMessage: "Confirmo la reserva para el Plan Marineritos el lunes.",
            LlmScript: FakeLlmScript.TextOnly("Para confirmar una reserva primero necesito verificar disponibilidad. Que fecha y hora prefieres?"),
            ExpectedBotResponseContains: "")
    ];
}

// -----------------------------------------------------------------------------
// Escenario 4: Reserva Doble
// -----------------------------------------------------------------------------

public class DoubleBookingScenario : TestScenario
{
    public override string Id          => "test_doble_reserva";
    public override string Description => "Se intenta crear dos reservas en el mismo horario. El sistema debe rechazar la segunda.";
    public override CalendarMode CalendarMode     => CalendarMode.Available;
    public override ReservationMode ReservationMode => ReservationMode.TrackDuplicates;
    public override bool ExpectReservationCreated  => true;
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
            UserMessage: "Quiero Plan Marineritos el 2026-08-15 a las 10am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"10:00"}""",
                "Hay disponibilidad. Confirmamos la reserva?"),
            ExpectedBotResponseContains: ""),

        new(
            UserMessage: "Confirmo.",
            LlmScript: FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"10:00","customer_name":"Cliente Test","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada exitosamente!"),
            ExpectedBotResponseContains: "")
    ];
}

// -----------------------------------------------------------------------------
// Escenario 5: Error del Backend de Calendario
// -----------------------------------------------------------------------------

public class BackendCalendarErrorScenario : TestScenario
{
    public override string Id          => "test_error_backend_calendar";
    public override string Description => "El servicio de disponibilidad lanza una excepcion. El agente debe manejarla con gracia.";
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
            UserMessage: "Quiero reservar Plan Aventuras Marinas para el 2026-09-01 a las 11am.",
            LlmScript:
            [
                // LLM intenta llamar check_availability (fallara con error del backend)
                .. FakeLlmScript.ToolThenText(
                    "check_availability",
                    """{"service":"Plan Aventuras Marinas","date":"2026-09-01","time":"11:00"}""",
                    "Lo siento, hubo un problema al verificar disponibilidad. Por favor intenta mas tarde.")
            ],
            ExpectedBotResponseContains: "")
    ];
}

// -----------------------------------------------------------------------------
// Escenario 6: Usuario Cambia de Fecha
// -----------------------------------------------------------------------------

public class UserChangesDateScenario : TestScenario
{
    public override string Id          => "test_usuario_cambia_fecha";
    public override string Description => "El usuario cambia la fecha de reserva. El sistema re-verifica disponibilidad.";
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
            UserMessage: "Quiero Plan Marineritos el 2026-08-15 a las 9am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-15","time":"09:00"}""",
                "Hay disponibilidad el 15 de agosto a las 9:00. Confirmamos?"),
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage: "En realidad prefiero el 2026-08-22 a las 11am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-22","time":"11:00"}""",
                "Tambien hay disponibilidad el 22 de agosto a las 11:00. Confirmamos con la nueva fecha?"),
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage: "Perfecto, confirmo el 22 de agosto. Soy Cliente Test.",
            LlmScript: FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-22","time":"11:00","customer_name":"Cliente Test","customer_phone":"+5491100000000","customer_confirmed":true}""",
                "Reserva creada para el 22 de agosto a las 11:00!"),
            ExpectedBotResponseContains: "reserva")
    ];
}

public class RepeatReservationAfterCompletionScenario : TestScenario
{
    public override string Id => "test_repetir_reserva_despues_de_completar";
    public override string Description => "El cliente completa una reserva, vuelve a saludar, pide servicios y puede completar una segunda reserva en la misma conversacion.";
    public override CalendarMode CalendarMode => CalendarMode.Available;
    public override ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public override bool ExpectReservationCreated => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "MultipleReservationCycles"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage: "Hola, quiero reservar un Plan Marineritos para el 2026-08-25 a las 10am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-08-25","time":"10:00"}""",
                "Hay disponibilidad el 25 de agosto a las 10:00. Confirmamos la reserva?"),
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage: "Si, confirmo. Soy Richard y mi telefono es +573012926660.",
            LlmScript: FakeLlmScript.ManyToolsThenToolThenText(
                [
                    ("resolve_service_selection", """{"text":"Plan Marineritos"}"""),
                    ("set_fact", """{"key":"desired_date","value":"2026-08-25"}"""),
                    ("set_fact", """{"key":"desired_time","value":"10:00"}""")
                ],
                "create_reservation",
                """{"service":"Plan Marineritos","date":"2026-08-25","time":"10:00","customer_name":"Richard","customer_phone":"+573012926660","customer_confirmed":true}""",
                "Tu reserva quedo creada para el 25 de agosto a las 10:00."),
            ExpectedBotResponseContains: "reserva"),

        new(
            UserMessage: "Hola de nuevo, quiero ver los servicios.",
            LlmScript: FakeLlmScript.ToolThenText(
                "get_service_catalog",
                """{"view":"categories"}""",
                "Hola de nuevo. Tenemos categorias y servicios disponibles para reservar."),
            ExpectedBotResponseContains: "servicios"),

        new(
            UserMessage: "Quiero Plan Suaves Mimos - Post Vacunas para el 2026-08-26 a las 11am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Suaves Mimos - Post Vacunas","date":"2026-08-26","time":"11:00"}""",
                "Tambien hay disponibilidad el 26 de agosto a las 11:00. Confirmamos?"),
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage: "Confirmo esa tambien.",
            LlmScript: FakeLlmScript.ManyToolsThenToolThenText(
                [
                    ("resolve_service_selection", """{"text":"Plan Suaves Mimos - Post Vacunas"}"""),
                    ("set_fact", """{"key":"desired_date","value":"2026-08-26"}"""),
                    ("set_fact", """{"key":"desired_time","value":"11:00"}""")
                ],
                "create_reservation",
                """{"service":"Plan Suaves Mimos - Post Vacunas","date":"2026-08-26","time":"11:00","customer_name":"Richard","customer_phone":"+573012926660","customer_confirmed":true}""",
                "Listo, tu segunda reserva quedo creada para el 26 de agosto a las 11:00."),
            ExpectedBotResponseContains: "segunda reserva")
    ];
}
