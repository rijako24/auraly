using MimosBabySpa.Domain.Enums;
using MimosBabySpa.IntegrationTests.Infrastructure;

namespace MimosBabySpa.IntegrationTests.Scenarios.Definitions;

// ─────────────────────────────────────────────────────────────────────────────
// ESCENARIO 7: Servicios extras integrados con disponibilidad
// El bot muestra disponibilidad + servicios extras en la misma respuesta.
// Usuario confirma con servicio extra en el siguiente mensaje.
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
            UserMessage:   "Quiero reservar Plan Deluxe el 2025-08-15 a las 10am.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "Service",      "value": "Plan Deluxe",      "confidence": 0.98},
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
            """,
            ExpectedBotResponseContains: "disponib"), // Step 1: Disponibilidad + servicios extras en misma respuesta

        new(
            UserMessage:   "Sí, agrega el Masaje Extra 15m y confirma.",
            ExtractionJson: """
            {
              "extracted_fields": [
                {"field": "SelectedAddOns", "value": "Masaje Extra 15m", "confidence": 0.95}
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
            ExpectedBotResponseContains: "reserva") // Step 2: Reserva creada con servicio extra
    ];
}
