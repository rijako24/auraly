using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Scenarios;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

// ─────────────────────────────────────────────────────────────────────────────
// Escenario: Plan Deluxe con add-on. El bot verifica disponibilidad y el usuario
// confirma con un servicio extra incluido.
// ─────────────────────────────────────────────────────────────────────────────

public class AddOnOfferingScenario : TestScenario
{
    public override string Id          => "test_oferta_addons";
    public override string Description => "Plan Deluxe: bot muestra disponibilidad y servicios extras juntos; usuario confirma con extra.";
    public override CalendarMode CalendarMode     => CalendarMode.Available;
    public override ReservationMode ReservationMode => ReservationMode.AlwaysSucceed;
    public override bool ExpectReservationCreated  => true;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "ReservationMustCallCreateReservation",
        "CheckAvailabilityBeforeCreateReservation",
        "BotMustNotInventTimeSlots",
        "NoDuplicateReservation",
        "ReservationMustIncludeAddOns"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage: "Quiero reservar Plan Deluxe el 2026-08-15 a las 10am.",
            LlmScript: FakeLlmScript.ToolThenText(
                "check_availability",
                """{"service":"Plan Deluxe","date":"2026-08-15","time":"10:00"}""",
                "Hay disponibilidad el 15 de agosto a las 10:00. El Plan Deluxe incluye la opción de agregar Masaje Extra 15m. ¿Lo incluimos?"),
            ExpectedBotResponseContains: "disponib"),

        new(
            UserMessage: "Sí, agrega el Masaje Extra 15m y confirma.",
            LlmScript: FakeLlmScript.ToolThenText(
                "create_reservation",
                """{"service":"Plan Deluxe","date":"2026-08-15","time":"10:00","customer_name":"Cliente Test","customer_phone":"+5491100000000","add_ons":"Masaje Extra 15m","customer_confirmed":true}""",
                "¡Reserva creada con Masaje Extra 15m! Te esperamos el 15 de agosto."),
            ExpectedBotResponseContains: "reserva")
    ];
}
