using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Operations.Reservation;

public static class ReservationCreationOutcomeCodes
{
    public const string Created = "reservation.created";
    public const string IdempotentReplay = "reservation.idempotent_replay";
    public const string PendingConfirmation = "reservation.pending_confirmation";
    public const string PaymentRequired = "payment.required";
    public const string PaymentFulfillmentPending = "payment.fulfillment_pending";
    public const string SlotUnavailable = "reservation.slot_unavailable";
}

public sealed class CreateReservationOperation : IAgentOperation
{
    private readonly IReservationCreationService _creation;

    public CreateReservationOperation(IReservationCreationService creation)
    {
        _creation = creation;
    }

    public OperationDescriptor Descriptor { get; } = new(
        "reservation.create",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "service": { "type": "string" },
            "date": { "type": "string" },
            "time": { "type": "string" },
            "customer_name": { "type": "string" },
            "customer_phone": { "type": "string" },
            "customer_email": { "type": ["string", "null"] },
            "add_ons": { "type": ["string", "null"] },
            "customer_confirmed": { "type": "boolean" }
          },
          "required": ["service", "date", "time", "customer_name", "customer_phone", "customer_confirmed"]
        }
        """,
        [
            ReservationCreationOutcomeCodes.Created,
            ReservationCreationOutcomeCodes.IdempotentReplay,
            ReservationCreationOutcomeCodes.PendingConfirmation,
            ReservationCreationOutcomeCodes.PaymentRequired,
            ReservationCreationOutcomeCodes.PaymentFulfillmentPending,
            ReservationCreationOutcomeCodes.SlotUnavailable,
            "input.missing_prerequisites",
            "input.invalid_date",
            "input.past_date",
            "input.invalid_time",
            "reservation.business_rule_violation",
            "reservation.invalid_booking_data"
        ],
        ["reservation.create"],
        [],
        []);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        var result = await _creation.CreateAsync(
            new ReservationCreationRequest(
                context.AgentId,
                context.BusinessId,
                context.ConversationId,
                context.BusinessToday,
                context.Config,
                context.Facts,
                ReadBool(input, "customer_confirmed"),
                ReadString(input, "service"),
                ReadString(input, "date"),
                ReadString(input, "time"),
                ReadString(input, "customer_name"),
                ReadString(input, "customer_phone"),
                ReadString(input, "customer_email"),
                ReadString(input, "add_ons")),
            cancellationToken);

        if (!result.Success)
        {
            return OperationOutcome.Fail(
                result.Code,
                result.Message ?? "Reservation could not be created.",
                result.Recoverable,
                RemediationSignal(result.Code),
                new { missingPrerequisites = result.MissingPrerequisites });
        }

        return OperationOutcome.Ok(
            result.Code,
            new
            {
                reservationId = result.ReservationId,
                service = result.Service,
                date = result.Date,
                time = result.Time,
                customerName = result.CustomerName,
                customerPhone = result.CustomerPhone,
                status = result.IsBookingConfirmed ? "Confirmed" : "PendingConfirmation",
                isBookingConfirmed = result.IsBookingConfirmed,
                employee = result.Employee,
                durationMinutes = result.DurationMinutes,
                addOns = result.AddOnNames,
                idempotentReplay = result.IdempotentReplay
            },
            events: result.Code == ReservationCreationOutcomeCodes.Created
                ? ["reservation_created"]
                : [],
            domainEvents: result.Code == ReservationCreationOutcomeCodes.Created
                ?
                [
                    OperationEvent.Create("reservation_created", new
                    {
                        reservationId = result.ReservationId,
                        service = result.Service,
                        date = result.Date,
                        time = result.Time,
                        customerName = result.CustomerName,
                        customerPhone = result.CustomerPhone
                    })
                ]
                : []);
    }

    private static string? ReadString(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();

    private static string? RemediationSignal(string code) => code switch
    {
        "input.missing_prerequisites" => "facts.collect_missing_reservation_data",
        "input.invalid_date" or "input.past_date" => "facts.collect_valid_date",
        "input.invalid_time" => "facts.collect_valid_time",
        ReservationCreationOutcomeCodes.PaymentRequired => "payment.await_confirmation",
        ReservationCreationOutcomeCodes.PaymentFulfillmentPending => "payment.await_fulfillment",
        ReservationCreationOutcomeCodes.SlotUnavailable => "reservation.check_availability",
        "reservation.business_rule_violation" => "reservation.change_request",
        _ => null
    };
}
