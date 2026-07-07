using System.Text.Json;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class RequestReservationRescheduleTool : IAgentTool
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICustomerReservationResolver _reservationResolver;

    public RequestReservationRescheduleTool(
        IUnitOfWork unitOfWork,
        ICustomerReservationResolver reservationResolver)
    {
        _unitOfWork = unitOfWork;
        _reservationResolver = reservationResolver;
    }

    public string Name => "request_reservation_reschedule";

    public string Description =>
        "Registers that the customer requested to reschedule an existing reservation without applying a new slot. " +
        "Use when the customer taps a reschedule button or asks to move the appointment but has not provided a new date/time yet. " +
        "If the customer provides the target date/time, use prepare_reservation_change and confirm_reservation_change instead.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "reservation_id": { "type": "string", "description": "Optional reservation UUID; omit when there is only one reservation in ESTADO RESERVA." },
            "job_id": { "type": "string", "description": "Optional ScheduledAutomationJob UUID from a WhatsApp button payload." },
            "notes": { "type": "string" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "job_id", out var jobIdStr);
        ToolResultHelper.TryGetString(arguments, "reservation_id", out var reservationIdStr);
        ToolResultHelper.TryGetString(arguments, "notes", out var notes);
        jobIdStr = string.IsNullOrWhiteSpace(jobIdStr)
            ? TryParseJobIdFromPayload(ctx)
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
                return resolved.ErrorJson ?? ToolResultHelper.Error("reservation_not_found", "No reservation was found.", recoverable: true);
            reservation = resolved.Reservation;
        }

        if (reservation is null)
            return ToolResultHelper.Error("reservation_not_found", "No reservation was found.", recoverable: true);

        if (reservation.Status == ReservationStatus.Confirmed || reservation.CustomerConfirmed)
        {
            reservation.Status = ReservationStatus.OnHold;
            reservation.CustomerConfirmed = false;
            reservation.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Reservations.UpdateAsync(reservation);
        }

        var latestResponse = await _unitOfWork.ReservationAttendanceResponses.GetLatestByReservationAsync(
            ctx.BusinessId,
            reservation.ReservationId,
            cancellationToken);
        if (latestResponse?.ResponseType == ReservationAttendanceResponseType.RescheduleRequested)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToolResultHelper.Ok(new
            {
                reservation_id = reservation.ReservationId,
                reschedule_requested = true,
                status = reservation.Status.ToString(),
                responded_at_utc = latestResponse.RespondedAtUtc,
                idempotent_replay = true
            });
        }

        var response = new ReservationAttendanceResponse
        {
            ReservationAttendanceResponseId = Guid.NewGuid(),
            BusinessId = ctx.BusinessId,
            ReservationId = reservation.ReservationId,
            SourceJobId = sourceJob?.ScheduledAutomationJobId,
            ResponseType = ReservationAttendanceResponseType.RescheduleRequested,
            RespondedAtUtc = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        await _unitOfWork.ReservationAttendanceResponses.AddAsync(response, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservation.ReservationId,
            reschedule_requested = true,
            status = reservation.Status.ToString(),
            responded_at_utc = response.RespondedAtUtc
        });
    }

    private static string? TryParseJobIdFromPayload(AgentToolContext ctx)
    {
        var action = ctx.InteractiveAction;
        if (action is null && !InteractivePayloadParser.TryParse(ctx.InteractivePayload, out action))
            return null;

        return action.Scope.Equals("reservation_attendance", StringComparison.OrdinalIgnoreCase)
            && action.Outcome.Equals("reschedule", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(action.SourceId, out var jobId)
                ? jobId.ToString("D")
                : null;
    }
}
