using MimosBabySpa.IntegrationTests.Infrastructure;
using MimosBabySpa.IntegrationTests.Scenarios;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

public class CheckoutAfterSummaryAddOnChangeScenario : TestScenario
{
    public override string Id => "checkout_after_summary_addon_change";
    public override string Description => "Despues de mostrar checkout base, el cliente agrega un complemento y el nuevo checkout debe incluirlo.";
    public override CalendarMode CalendarMode => CalendarMode.Available;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "PrepareCheckoutCalledAtLeastTwice",
        "LastCheckoutIncludesMasajeExtra"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage: "Quiero Plan Deluxe el 2026-09-15 a las 10am. Soy Cliente Test y mi telefono es +573001234567.",
            LlmScript: FakeLlmScript.ManyToolsThenToolThenText(
                BaseFacts(addOns: "ninguno"),
                "check_availability",
                """{"service":"Plan Deluxe","date":"2026-09-15","time":"10:00"}""",
                "Hay disponibilidad para Plan Deluxe el 15 a las 10."),
            ExpectedBotResponseContains: "disponibilidad"),

        new(
            UserMessage: "Enviame el resumen para pagar.",
            LlmScript: FakeLlmScript.ToolOnly(
                "prepare_checkout",
                "{}"),
            ExpectedBotResponseContains: "resumen"),

        new(
            UserMessage: "Quiero agregar Masaje Extra 15m.",
            LlmScript: FakeLlmScript.ToolThenText(
                "set_fact",
                """{"key":"add_ons","value":"Masaje Extra 15m"}""",
                "Listo, agrego Masaje Extra 15m."),
            ExpectedBotResponseContains: "agrego"),

        new(
            UserMessage: "Actualiza el resumen.",
            LlmScript: FakeLlmScript.ToolOnly(
                "prepare_checkout",
                "{}"),
            ExpectedBotResponseContains: "Resumen")
    ];

    private static IReadOnlyList<(string ToolName, string ArgsJson)> BaseFacts(string addOns) =>
    [
        ("resolve_service_selection", """{"text":"Plan Deluxe"}"""),
        ("set_fact", """{"key":"reservation_date","value":"2026-09-15"}"""),
        ("set_fact", """{"key":"reservation_time","value":"10:00"}"""),
        ("set_fact", """{"key":"customer_name","value":"Cliente Test"}"""),
        ("set_fact", """{"key":"customer_phone","value":"+573001234567"}"""),
        ("set_fact", $$"""{"key":"add_ons","value":"{{addOns}}"}""")
    ];
}

public class CheckoutAfterSummaryRemoveAddOnScenario : TestScenario
{
    public override string Id => "checkout_after_summary_remove_addon";
    public override string Description => "Despues de mostrar checkout con complemento, el cliente lo quita y el nuevo checkout debe salir sin complemento.";
    public override CalendarMode CalendarMode => CalendarMode.Available;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "PrepareCheckoutCalledAtLeastTwice",
        "LastCheckoutRemovesAddOns"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage: "Quiero Plan Deluxe con Masaje Extra 15m el 2026-09-15 a las 10am. Soy Cliente Test y mi telefono es +573001234567.",
            LlmScript: FakeLlmScript.ManyToolsThenToolThenText(
                BaseFacts(addOns: "Masaje Extra 15m"),
                "check_availability",
                """{"service":"Plan Deluxe","date":"2026-09-15","time":"10:00"}""",
                "Hay disponibilidad para Plan Deluxe con Masaje Extra 15m."),
            ExpectedBotResponseContains: "disponibilidad"),

        new(
            UserMessage: "Enviame el resumen para pagar.",
            LlmScript: FakeLlmScript.ToolOnly(
                "prepare_checkout",
                "{}"),
            ExpectedBotResponseContains: "resumen"),

        new(
            UserMessage: "Mejor quita el complemento.",
            LlmScript: FakeLlmScript.ToolThenText(
                "set_fact",
                """{"key":"add_ons","value":"ninguno"}""",
                "Listo, quite los complementos."),
            ExpectedBotResponseContains: "quite"),

        new(
            UserMessage: "Actualiza el resumen.",
            LlmScript: FakeLlmScript.ToolOnly(
                "prepare_checkout",
                "{}"),
            ExpectedBotResponseContains: "Resumen")
    ];

    private static IReadOnlyList<(string ToolName, string ArgsJson)> BaseFacts(string addOns) =>
    [
        ("resolve_service_selection", """{"text":"Plan Deluxe"}"""),
        ("set_fact", """{"key":"reservation_date","value":"2026-09-15"}"""),
        ("set_fact", """{"key":"reservation_time","value":"10:00"}"""),
        ("set_fact", """{"key":"customer_name","value":"Cliente Test"}"""),
        ("set_fact", """{"key":"customer_phone","value":"+573001234567"}"""),
        ("set_fact", $$"""{"key":"add_ons","value":"{{addOns}}"}""")
    ];
}

public class CheckoutAfterSummaryBookingInputChangeScenario : TestScenario
{
    public override string Id => "checkout_after_summary_booking_input_change";
    public override string Description => "Despues de mostrar checkout, el cliente cambia servicio, fecha y hora; el nuevo checkout debe usar esos datos.";
    public override CalendarMode CalendarMode => CalendarMode.Available;
    public override bool ExpectAvailabilityChecked => true;

    public override IReadOnlyList<string> RulesToValidate =>
    [
        "PrepareCheckoutCalledAtLeastTwice",
        "LastCheckoutUsesChangedBookingInputs"
    ];

    public override IReadOnlyList<ConversationStep> Steps =>
    [
        new(
            UserMessage: "Quiero Plan Deluxe el 2026-09-15 a las 10am. Soy Cliente Test y mi telefono es +573001234567.",
            LlmScript: FakeLlmScript.ManyToolsThenToolThenText(
                [
                    ("resolve_service_selection", """{"text":"Plan Deluxe"}"""),
                    ("set_fact", """{"key":"reservation_date","value":"2026-09-15"}"""),
                    ("set_fact", """{"key":"reservation_time","value":"10:00"}"""),
                    ("set_fact", """{"key":"customer_name","value":"Cliente Test"}"""),
                    ("set_fact", """{"key":"customer_phone","value":"+573001234567"}"""),
                    ("set_fact", """{"key":"add_ons","value":"ninguno"}""")
                ],
                "check_availability",
                """{"service":"Plan Deluxe","date":"2026-09-15","time":"10:00"}""",
                "Hay disponibilidad para Plan Deluxe el 15 a las 10."),
            ExpectedBotResponseContains: "disponibilidad"),

        new(
            UserMessage: "Enviame el resumen para pagar.",
            LlmScript: FakeLlmScript.ToolOnly(
                "prepare_checkout",
                "{}"),
            ExpectedBotResponseContains: "resumen"),

        new(
            UserMessage: "Cambialo a Plan Marineritos el 2026-09-16 a las 11am.",
            LlmScript: FakeLlmScript.ManyToolsThenToolThenText(
                [
                    ("resolve_service_selection", """{"text":"Plan Marineritos"}"""),
                    ("set_fact", """{"key":"reservation_date","value":"2026-09-16"}"""),
                    ("set_fact", """{"key":"reservation_time","value":"11:00"}"""),
                    ("set_fact", """{"key":"add_ons","value":"ninguno"}""")
                ],
                "check_availability",
                """{"service":"Plan Marineritos","date":"2026-09-16","time":"11:00"}""",
                "Tambien hay disponibilidad para Plan Marineritos el 16 a las 11."),
            ExpectedBotResponseContains: "disponibilidad"),

        new(
            UserMessage: "Actualiza el resumen.",
            LlmScript: FakeLlmScript.ToolOnly(
                "prepare_checkout",
                "{}"),
            ExpectedBotResponseContains: "Resumen")
    ];
}

