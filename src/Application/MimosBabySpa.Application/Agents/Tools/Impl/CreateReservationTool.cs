using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations.Reservation;
using MimosBabySpa.Domain.Enums;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Compatibility adapter for the pre-deterministic runtime. Reservation creation behavior lives
/// in <see cref="IReservationCreationService"/> and is shared with the typed operation.
/// </summary>
[AgentToolMetadata("create_reservation", Capabilities = new[] { ToolCapabilities.ReservationCreate })]
public sealed class CreateReservationTool : IAgentTool
{
    private readonly IReservationCreationService _creation;

    public CreateReservationTool(IReservationCreationService creation)
    {
        _creation = creation;
    }

    public string Name => "create_reservation";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.ReservationCreate];

    public string Description =>
        "Creates one confirmed reservation after explicit customer confirmation and deterministic validation.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string" },
            "date": { "type": "string" },
            "time": { "type": "string" },
            "customer_name": { "type": "string" },
            "customer_phone": { "type": "string" },
            "customer_email": { "type": "string" },
            "add_ons": { "type": "string" },
            "customer_confirmed": { "type": "boolean" }
          },
          "required": ["customer_confirmed"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var result = await _creation.CreateAsync(
            new ReservationCreationRequest(
                ctx.AgentId,
                ctx.BusinessId,
                ctx.ConversationId,
                ctx.BusinessToday,
                ctx.Config ?? new AgentConfig(),
                ctx.Facts,
                ReadBool(arguments, "customer_confirmed"),
                ReadString(arguments, "service"),
                ReadString(arguments, "date"),
                ReadString(arguments, "time"),
                ReadString(arguments, "customer_name"),
                ReadString(arguments, "customer_phone"),
                ReadString(arguments, "customer_email"),
                ReadString(arguments, "add_ons"),
                ctx.Conversation.CustomerName,
                ctx.Conversation.CustomerEmail,
                ctx.ChannelPhone,
                ctx.ActivePayment,
                ctx.SingleManageableReservation),
            cancellationToken);

        if (!result.Success)
            return Failure(result);

        foreach (var (key, value) in result.EffectiveFacts)
        {
            if (!ctx.Facts.TryGetValue(key, out var current) || string.IsNullOrWhiteSpace(current))
                ctx.Facts[key] = value;
        }

        if (result.Code == ReservationCreationOutcomeCodes.PendingConfirmation)
        {
            return ToolResultHelper.Ok(new
            {
                status = "pending_confirmation",
                summary = new
                {
                    service = result.Service,
                    date = result.Date,
                    time = result.Time,
                    customer_name = result.CustomerName,
                    customer_phone = result.CustomerPhone
                }
            });
        }

        if (result.Reservation is not null)
            ctx.ManageableReservations = [result.Reservation];
        if (result.Code == ReservationCreationOutcomeCodes.Created && result.Reservation is not null)
        {
            ctx.NotificationContexts["reservation_created"] = new MessageSequenceContext
            {
                Reservation = result.Reservation
            };
        }

        var effects = result.IdempotentReplay
            ? Array.Empty<string>()
            : [RequestCompleted];
        var events = result.IdempotentReplay
            ? Array.Empty<string>()
            : ["reservation_created"];
        return ToolResultHelper.OkWithEvents(new
        {
            reservation_id = result.ReservationId,
            service = result.Service,
            date = result.Date,
            time = result.Time,
            customer_name = result.CustomerName,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = result.IsBookingConfirmed,
            employee = result.Employee,
            duration_minutes = result.DurationMinutes,
            add_ons = result.AddOnNames,
            idempotent_replay = result.IdempotentReplay
        }, effects, events);
    }

    private static string Failure(ReservationCreationResult result)
    {
        if (result.MissingPrerequisites.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. result.MissingPrerequisites]);

        if (result.Code is ReservationCreationOutcomeCodes.PaymentRequired
            or ReservationCreationOutcomeCodes.PaymentFulfillmentPending)
        {
            return ToolResultHelper.ErrorWithLlm(
                result.Code == ReservationCreationOutcomeCodes.PaymentRequired
                    ? "payment_required"
                    : "payment_fulfillment_pending",
                result.Message ?? "Payment processing is pending.",
                new
                {
                    next_action = result.Code == ReservationCreationOutcomeCodes.PaymentRequired
                        ? "await_payment_confirmation"
                        : "await_payment_fulfillment"
                },
                recoverable: true);
        }

        return ToolResultHelper.Error(
            LegacyCode(result.Code),
            result.Message ?? "Reservation could not be created.",
            result.Recoverable);
    }

    private static string LegacyCode(string code) => code switch
    {
        "input.invalid_date" => "invalid_date",
        "input.past_date" => "past_date",
        "input.invalid_time" => "invalid_time",
        "reservation.business_rule_violation" => "business_rule_violation",
        "reservation.slot_unavailable" => "slot_unavailable",
        "reservation.invalid_booking_data" => "invalid_booking_data",
        _ => code
    };

    private static string? ReadString(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool ReadBool(JsonElement input, string property) =>
        input.TryGetProperty(property, out var value)
        && value.ValueKind is JsonValueKind.True or JsonValueKind.False
        && value.GetBoolean();
}
