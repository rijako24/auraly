using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class ConfirmReservationAttendanceTool : IAgentTool
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerReservationResolver _reservationResolver;

    public ConfirmReservationAttendanceTool(
        IUnitOfWork unitOfWork,
        ICustomerReservationResolver reservationResolver)
    {
        _unitOfWork = unitOfWork;
        _reservationResolver = reservationResolver;
    }

    public string Name => "confirm_reservation_attendance";

    public string Description =>
        "Registers that the customer confirmed attendance for an existing reservation. " +
        "Use when the customer clearly says they will attend or taps a confirmation button.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "Optional reservation UUID; omit when there is only one reservation in ESTADO RESERVA." },
            "job_id": { "type": "string", "description": "Optional ScheduledAutomationJob UUID from a WhatsApp button payload." },
            "customer_confirmed": { "type": "boolean", "description": "Must be true only when the customer clearly confirms attendance." },
            "notes": { "type": "string" }
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
                "Customer confirmation is required before registering attendance.",
                "Ask the customer to confirm if they will attend.",
                recoverable: true);
        }

        ToolResultHelper.TryGetString(arguments, "job_id", out var jobIdStr);
        ToolResultHelper.TryGetString(arguments, "reservation_id", out var reservationIdStr);
        ToolResultHelper.TryGetString(arguments, "notes", out var notes);
        jobIdStr = string.IsNullOrWhiteSpace(jobIdStr)
            ? TryParseJobIdFromPayload(ctx.InteractivePayload)
            : jobIdStr;

        ScheduledAutomationJob? sourceJob = null;
        Reservation? reservation = null;

        if (Guid.TryParse(jobIdStr, out var jobId))
        {
            sourceJob = await _unitOfWork.ScheduledAutomationJobs.GetByIdAsync(jobId, cancellationToken);
            if (sourceJob is not null && sourceJob.BusinessId == ctx.BusinessId)
                reservation = sourceJob.Reservation;
        }

        if (reservation is null)
        {
            var resolved = await _reservationResolver.ResolveAsync(ctx, reservationIdStr, cancellationToken);
            if (!resolved.Success)
                return resolved.ErrorJson;
            reservation = resolved.Reservation;
        }

        if (reservation is null)
            return ToolResultHelper.Error("reservation_not_found", "No reservation was found.", recoverable: true);

        var response = new ReservationAttendanceResponse
        {
            ReservationAttendanceResponseId = Guid.NewGuid(),
            BusinessId = ctx.BusinessId,
            ReservationId = reservation.ReservationId,
            SourceJobId = sourceJob?.ScheduledAutomationJobId,
            ResponseType = ReservationAttendanceResponseType.Confirmed,
            RespondedAtUtc = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        await _unitOfWork.ReservationAttendanceResponses.AddAsync(response, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservation.ReservationId,
            attendance_confirmed = true,
            responded_at_utc = response.RespondedAtUtc
        });
    }

    private static string? TryParseJobIdFromPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return null;

        var parts = payload.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 3 &&
            parts[0].Equals("reservation_attendance", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("confirm", StringComparison.OrdinalIgnoreCase) &&
            Guid.TryParse(parts[2], out var jobId))
        {
            return jobId.ToString("D");
        }

        return null;
    }
}
