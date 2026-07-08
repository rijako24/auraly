using System.Text.Json;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class ConfirmReservationChangeTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly ICustomerReservationResolver _reservationResolver;

    public ConfirmReservationChangeTool(
        IReservationService reservations,
        ICustomerReservationResolver reservationResolver)
    {
        _reservations = reservations;
        _reservationResolver = reservationResolver;
    }

    public string Name => "confirm_reservation_change";

    public string Description =>
        "Applies a previously discussed change to an existing customer reservation after customer confirmation. " +
        "Can update service, date, time, and add-ons together.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "Optional internal UUID; omit when there is only one reservation in ESTADO RESERVA." },
            "service": { "type": "string", "description": "Optional new exact or natural service name." },
            "date": { "type": "string", "description": "Optional new date in YYYY-MM-DD format." },
            "time": { "type": "string", "description": "Optional new time in HH:mm format." },
            "add_ons": { "type": "string", "description": "Optional comma-separated add-on names." },
            "add_ons_mode": { "type": "string", "enum": ["add", "remove", "replace"], "description": "How to apply add_ons. Default is add." },
            "customer_confirmed": { "type": "boolean", "description": "Must be true only when the customer has clearly confirmed the change." }
          },
          "required": ["customer_confirmed"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (!ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var confirmed) || !confirmed)
        {
            return ToolResultHelper.Error(
                "confirmation_required",
                "Customer confirmation is required before applying reservation changes.",
                "Summarize the change and ask the customer to confirm.",
                recoverable: true);
        }

        var request = await PrepareReservationChangeTool.BuildRequestAsync(
            arguments,
            ctx,
            _reservationResolver,
            apply: true,
            cancellationToken);
        if (request.ErrorJson is not null)
            return request.ErrorJson;

        var result = await _reservations.UpdateReservationAsync(request.Request!, cancellationToken);
        if (!result.Success)
            return ToolResultHelper.Error(result.ErrorCode!, result.ErrorMessage!, result.Remediation, recoverable: true);

        ctx.ManageableReservations =
        [
            new Domain.Entities.Reservation
            {
                ReservationId = result.ReservationId,
                BusinessId = ctx.BusinessId,
                ConversationId = ctx.ConversationId,
                Status = Domain.Enums.ReservationStatus.Confirmed,
                ReservationDateTime = result.Date.HasValue && result.Time.HasValue
                    ? result.Date.Value.ToDateTime(result.Time.Value)
                    : null,
                DurationMinutes = result.DurationMinutes,
                Service = new Domain.Entities.Service { ServiceName = result.ServiceName }
            }
        ];

        return ToolResultHelper.OkWithLlm(
            PrepareReservationChangeTool.ToPayload(result),
            PrepareReservationChangeTool.ToLlmPayload(result));
    }
}
