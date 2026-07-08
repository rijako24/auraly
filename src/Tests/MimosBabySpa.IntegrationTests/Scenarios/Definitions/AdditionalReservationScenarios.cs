using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Scenarios;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

public sealed class AdditionalReservationScenario : TestScenario
{
    private readonly string _id;
    private readonly string _description;
    private readonly IReadOnlyList<ConversationStep> _steps;
    private readonly bool _expectReservationCreated;
    private readonly bool _expectAvailabilityChecked;
    private readonly CalendarMode _calendarMode;

    public AdditionalReservationScenario(
        string id,
        string description,
        bool expectReservationCreated,
        bool expectAvailabilityChecked,
        CalendarMode calendarMode,
        IReadOnlyList<ConversationStep> steps)
    {
        _id = id;
        _description = description;
        _expectReservationCreated = expectReservationCreated;
        _expectAvailabilityChecked = expectAvailabilityChecked;
        _calendarMode = calendarMode;
        _steps = steps;
    }

    public override string Id => _id;
    public override string Description => _description;
    public override bool ExpectReservationCreated => _expectReservationCreated;
    public override bool ExpectAvailabilityChecked => _expectAvailabilityChecked;
    public override CalendarMode CalendarMode => _calendarMode;
    public override IReadOnlyList<string> RulesToValidate =>
        _expectReservationCreated
            ? ["ReservationMustCallCreateReservation", "CheckAvailabilityBeforeCreateReservation", "NoDuplicateReservation"]
            : ["NoHallucinatedAvailability", "NoDuplicateReservation"];
    public override IReadOnlyList<ConversationStep> Steps => _steps;

    public static IReadOnlyList<TestScenario> BuildAll() =>
    [
        new AdditionalReservationScenario(
            "additional_01_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Marineritos el 2026-08-11 a las 10:00. Soy Cliente Extra 1, extra1@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Marineritos","date":"2026-08-11","time":"10:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Marineritos","date":"2026-08-11","time":"10:00","customer_name":"Cliente Extra 1","customer_phone":"+5491100000000","customer_email":"extra1@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_02_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Post Vacunas el 2026-08-12 a las 11:00. Soy Cliente Extra 2, extra2@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Post Vacunas","date":"2026-08-12","time":"11:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Post Vacunas","date":"2026-08-12","time":"11:00","customer_name":"Cliente Extra 2","customer_phone":"+5491100000000","customer_email":"extra2@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_03_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Aventuras Marinas el 2026-08-13 a las 12:30. Soy Cliente Extra 3, extra3@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-13","time":"12:30"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-13","time":"12:30","customer_name":"Cliente Extra 3","customer_phone":"+5491100000000","customer_email":"extra3@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_04_date_change",
            "Edge: cambia fecha antes de confirmar y se revalida.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Quiero Plan Deluxe 2026-08-14 a las 13:00",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-14","time":"13:00"}""",
                        "Hay disponibilidad. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Mejor el 2026-08-15 a las 13:30, soy Cliente Extra 4",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-15","time":"13:30"}""",
                        "Tambien hay disponibilidad con la nueva fecha. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Deluxe","date":"2026-08-15","time":"13:30","customer_name":"Cliente Extra 4","customer_phone":"+5491100000000","customer_confirmed":true}""",
                        "Reserva creada con la nueva fecha."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_05_no_availability",
            "Edge: horario no disponible no crea reserva.",
            false,
            true,
            CalendarMode.NoSlots,
            [
                new ConversationStep(
                    "Quiero Plan Marineritos 2026-08-15 a las 14:00, soy Cliente Extra 5",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Marineritos","date":"2026-08-15","time":"14:00"}""",
                        "No hay disponibilidad para ese horario. Te muestro otras opciones."),
                    "No hay disponibilidad")
            ]),
        new AdditionalReservationScenario(
            "additional_06_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Post Vacunas el 2026-08-16 a las 15:30. Soy Cliente Extra 6, extra6@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"15:30"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Post Vacunas","date":"2026-08-16","time":"15:30","customer_name":"Cliente Extra 6","customer_phone":"+5491100000000","customer_email":"extra6@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_07_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Aventuras Marinas el 2026-08-17 a las 9:00. Soy Cliente Extra 7, extra7@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-17","time":"09:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-17","time":"09:00","customer_name":"Cliente Extra 7","customer_phone":"+5491100000000","customer_email":"extra7@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_08_date_change",
            "Edge: cambia fecha antes de confirmar y se revalida.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Quiero Plan Deluxe 2026-08-18 a las 10:00",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-18","time":"10:00"}""",
                        "Hay disponibilidad. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Mejor el 2026-08-19 a las 10:30, soy Cliente Extra 8",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-19","time":"10:30"}""",
                        "Tambien hay disponibilidad con la nueva fecha. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Deluxe","date":"2026-08-19","time":"10:30","customer_name":"Cliente Extra 8","customer_phone":"+5491100000000","customer_confirmed":true}""",
                        "Reserva creada con la nueva fecha."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_09_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Marineritos el 2026-08-19 a las 11:30. Soy Cliente Extra 9, extra9@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Marineritos","date":"2026-08-19","time":"11:30"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Marineritos","date":"2026-08-19","time":"11:30","customer_name":"Cliente Extra 9","customer_phone":"+5491100000000","customer_email":"extra9@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_10_no_availability",
            "Edge: horario no disponible no crea reserva.",
            false,
            true,
            CalendarMode.NoSlots,
            [
                new ConversationStep(
                    "Quiero Plan Post Vacunas 2026-08-20 a las 12:00, soy Cliente Extra 10",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Post Vacunas","date":"2026-08-20","time":"12:00"}""",
                        "No hay disponibilidad para ese horario. Te muestro otras opciones."),
                    "No hay disponibilidad")
            ]),
        new AdditionalReservationScenario(
            "additional_11_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Aventuras Marinas el 2026-08-21 a las 13:00. Soy Cliente Extra 11, extra11@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-21","time":"13:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-21","time":"13:00","customer_name":"Cliente Extra 11","customer_phone":"+5491100000000","customer_email":"extra11@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_12_date_change",
            "Edge: cambia fecha antes de confirmar y se revalida.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Quiero Plan Deluxe 2026-08-22 a las 14:00",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-22","time":"14:00"}""",
                        "Hay disponibilidad. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Mejor el 2026-08-23 a las 14:30, soy Cliente Extra 12",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-23","time":"14:30"}""",
                        "Tambien hay disponibilidad con la nueva fecha. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Deluxe","date":"2026-08-23","time":"14:30","customer_name":"Cliente Extra 12","customer_phone":"+5491100000000","customer_confirmed":true}""",
                        "Reserva creada con la nueva fecha."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_13_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Marineritos el 2026-08-23 a las 15:00. Soy Cliente Extra 13, extra13@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Marineritos","date":"2026-08-23","time":"15:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Marineritos","date":"2026-08-23","time":"15:00","customer_name":"Cliente Extra 13","customer_phone":"+5491100000000","customer_email":"extra13@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_14_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Post Vacunas el 2026-08-24 a las 9:00. Soy Cliente Extra 14, extra14@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Post Vacunas","date":"2026-08-24","time":"09:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Post Vacunas","date":"2026-08-24","time":"09:00","customer_name":"Cliente Extra 14","customer_phone":"+5491100000000","customer_email":"extra14@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_15_no_availability",
            "Edge: horario no disponible no crea reserva.",
            false,
            true,
            CalendarMode.NoSlots,
            [
                new ConversationStep(
                    "Quiero Plan Aventuras Marinas 2026-08-25 a las 10:00, soy Cliente Extra 15",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-25","time":"10:00"}""",
                        "No hay disponibilidad para ese horario. Te muestro otras opciones."),
                    "No hay disponibilidad")
            ]),
        new AdditionalReservationScenario(
            "additional_16_date_change",
            "Edge: cambia fecha antes de confirmar y se revalida.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Quiero Plan Deluxe 2026-08-26 a las 11:00",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-26","time":"11:00"}""",
                        "Hay disponibilidad. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Mejor el 2026-08-27 a las 11:30, soy Cliente Extra 16",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-27","time":"11:30"}""",
                        "Tambien hay disponibilidad con la nueva fecha. Confirmamos?"),
                    "disponib"),
                new ConversationStep(
                    "Confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Deluxe","date":"2026-08-27","time":"11:30","customer_name":"Cliente Extra 16","customer_phone":"+5491100000000","customer_confirmed":true}""",
                        "Reserva creada con la nueva fecha."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_17_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Marineritos el 2026-08-27 a las 12:00. Soy Cliente Extra 17, extra17@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Marineritos","date":"2026-08-27","time":"12:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Marineritos","date":"2026-08-27","time":"12:00","customer_name":"Cliente Extra 17","customer_phone":"+5491100000000","customer_email":"extra17@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_18_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Post Vacunas el 2026-08-28 a las 13:30. Soy Cliente Extra 18, extra18@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Post Vacunas","date":"2026-08-28","time":"13:30"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Post Vacunas","date":"2026-08-28","time":"13:30","customer_name":"Cliente Extra 18","customer_phone":"+5491100000000","customer_email":"extra18@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_19_happy",
            "Happy path adicional con datos completos.",
            true,
            true,
            CalendarMode.Available,
            [
                new ConversationStep(
                    "Hola, quiero Plan Aventuras Marinas el 2026-08-29 a las 14:00. Soy Cliente Extra 19, extra19@mail.com",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-29","time":"14:00"}""",
                        "Hay disponibilidad. Confirmamos la reserva?"),
                    "disponib"),
                new ConversationStep(
                    "Si confirmo",
                    FakeLlmScript.ToolThenText(
                        "create_reservation",
                        """{"service":"Plan Aventuras Marinas","date":"2026-08-29","time":"14:00","customer_name":"Cliente Extra 19","customer_phone":"+5491100000000","customer_email":"extra19@mail.com","customer_confirmed":true}""",
                        "Reserva creada correctamente."),
                    "Reserva")
            ]),
        new AdditionalReservationScenario(
            "additional_20_no_availability",
            "Edge: horario no disponible no crea reserva.",
            false,
            true,
            CalendarMode.NoSlots,
            [
                new ConversationStep(
                    "Quiero Plan Deluxe 2026-08-30 a las 15:00, soy Cliente Extra 20",
                    FakeLlmScript.ToolThenText(
                        "check_availability",
                        """{"service":"Plan Deluxe","date":"2026-08-30","time":"15:00"}""",
                        "No hay disponibilidad para ese horario. Te muestro otras opciones."),
                    "No hay disponibilidad")
            ])
    ];
}

