using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("manage_reservation", Capabilities = new[] { ToolCapabilities.ReservationManage })]
public sealed class ManageReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly ICustomerReservationResolver _reservationResolver;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationVerificationService _verifications;
    private readonly IEscalationNotifier _escalationNotifier;

    public ManageReservationTool(
        IReservationService reservations,
        ICustomerReservationResolver reservationResolver,
        IPaymentLifecycleService paymentLifecycle,
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        IUnitOfWork unitOfWork,
        IConversationVerificationService verifications,
        IEscalationNotifier escalationNotifier)
    {
        _reservations = reservations;
        _reservationResolver = reservationResolver;
        _paymentLifecycle = paymentLifecycle;
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _unitOfWork = unitOfWork;
        _verifications = verifications;
        _escalationNotifier = escalationNotifier;
    }

    public string Name => "manage_reservation";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.ReservationManage];

    public string Description =>
        "Manages customer reservation lifecycle through a single safe workflow. " +
        "Reservation change policy is supplied by the active agent configuration. " +
        "Use request_reschedule only when the customer wants to move the appointment but has not provided a target slot. " +
        "Use complete_paid_reschedule when a confirmed paid transaction has no linked reservation and the customer provides the replacement date/time.";

    public Func<JsonElement, AgentToolContext, IReadOnlyDictionary<string, string>?>? VerificationDependencyResolver =>
        (args, ctx) =>
        {
            ToolResultHelper.TryGetString(args, "action", out var action);
            if (!string.Equals(action, "complete_paid_reschedule", StringComparison.OrdinalIgnoreCase))
                return null;

            if (!ToolResultHelper.TryGetString(args, "date", out var date) || string.IsNullOrWhiteSpace(date))
                return null;
            if (!ToolResultHelper.TryGetString(args, "time", out var time) || string.IsNullOrWhiteSpace(time))
                return null;

            var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
            var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
            var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
            var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;

            ctx.Facts.TryGetValue(serviceKey, out var service);
            if (string.IsNullOrWhiteSpace(service))
                return null;

            return VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(serviceKey, service),
                new KeyValuePair<string, string>(dateKey, date),
                new KeyValuePair<string, string>(timeKey, time));
        };

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "enum": ["request_reschedule", "preview_change", "apply_change", "complete_paid_reschedule", "confirm_attendance", "cancel"],
              "description": "Operation to perform."
            },
            "reservation_id": { "type": "string", "description": "Optional internal UUID; omit when there is only one reservation in ESTADO RESERVA." },
            "payment_transaction_id": { "type": "string", "description": "Optional payment transaction UUID for complete_paid_reschedule." },
            "job_id": { "type": "string", "description": "Optional ScheduledAutomationJob UUID from a WhatsApp button payload." },
            "service": { "type": "string", "description": "Optional new service name." },
            "date": { "type": "string", "description": "Optional new date in YYYY-MM-DD format." },
            "time": { "type": "string", "description": "Optional new time in HH:mm format." },
            "add_ons": { "type": "string", "description": "Optional comma-separated add-on names." },
            "add_ons_mode": { "type": "string", "enum": ["add", "remove", "replace"] },
            "customer_confirmed": { "type": "boolean", "description": "True only when the customer clearly confirmed the operation." },
            "notes": { "type": "string" }
          },
          "required": ["action"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        ToolResultHelper.TryGetString(arguments, "action", out var action);
        action = NormalizeAction(action, arguments);

        return action switch
        {
            "preview_change" => await PreviewOrApplyChangeAsync(arguments, ctx, apply: false, cancellationToken),
            "apply_change" => await PreviewOrApplyChangeAsync(arguments, ctx, apply: true, cancellationToken),
            "complete_paid_reschedule" => await CompletePaidRescheduleAsync(arguments, ctx, cancellationToken),
            "request_reschedule" => await RequestRescheduleAsync(arguments, ctx, cancellationToken),
            "confirm_attendance" => await ConfirmAttendanceAsync(arguments, ctx, cancellationToken),
            "cancel" => await CancelAsync(arguments, ctx, cancellationToken),
            _ => ToolResultHelper.Error(
                "invalid_action",
                $"Unknown reservation management action '{action}'.",
                "Use one of: request_reschedule, preview_change, apply_change, complete_paid_reschedule, confirm_attendance, cancel.",
                recoverable: true)
        };
    }

    private async Task<string> PreviewOrApplyChangeAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        bool apply,
        CancellationToken cancellationToken)
    {
        var changePolicy = ReservationChangePolicy.From(ctx.Config?.ReservationManagement);
        var requestedFields = RequestedChangeFields(arguments, changePolicy.KnownChangeFields);
        var escalationFields = changePolicy.EscalationFieldsFor(requestedFields);
        if (escalationFields.Count > 0)
            return await EscalateUnsupportedReservationChangeAsync(arguments, ctx, cancellationToken);

        if (!changePolicy.HasAutomaticField(requestedFields))
        {
            return ToolResultHelper.ErrorWithLlm(
                "missing_supported_reservation_change",
                "Automatic reservation changes require at least one configured automatic change field.",
                null,
                new
                {
                    next_action = "collect_reschedule_target",
                    required_fields = changePolicy.AutomaticChangeFields
                },
                recoverable: true);
        }

        // Date/time reschedules are the customer's confirmed intent; no second confirmation turn.
        apply = true;

        var request = await PrepareReservationChangeTool.BuildRequestAsync(
            arguments,
            ctx,
            _reservationResolver,
            apply,
            cancellationToken);
        if (request.ErrorJson is not null)
            return request.ErrorJson;

        var result = await _reservations.UpdateReservationAsync(request.Request!, cancellationToken);
        if (!result.Success)
        {
            var remediation = result.Remediation;
            if (string.Equals(result.ErrorCode, "slot_unavailable", StringComparison.OrdinalIgnoreCase))
            {
                var availabilityHint = await BuildAvailabilityHintAsync(request.Request!, ctx, cancellationToken);
                remediation = null;

                return ToolResultHelper.ErrorWithLlm(
                    result.ErrorCode!,
                    result.ErrorMessage!,
                    remediation,
                    new
                    {
                        next_action = "offer_alternative_slots",
                        requested_date = availabilityHint?.Date,
                        available_slots = availabilityHint?.Slots ?? []
                    },
                    recoverable: true);
            }

            return ToolResultHelper.Error(result.ErrorCode!, result.ErrorMessage!, remediation, recoverable: true);
        }

        ctx.ManageableReservations =
        [
            new Reservation
            {
                ReservationId = result.ReservationId,
                BusinessId = ctx.BusinessId,
                ConversationId = ctx.ConversationId,
                Status = ReservationStatus.Confirmed,
                ReservationDateTime = result.Date.HasValue && result.Time.HasValue
                    ? result.Date.Value.ToDateTime(result.Time.Value)
                    : null,
                DurationMinutes = result.DurationMinutes,
                Service = new Service { ServiceName = result.ServiceName }
            }
        ];

        return ToolResultHelper.OkWithLlm(
            PrepareReservationChangeTool.ToPayload(result),
            PrepareReservationChangeTool.ToLlmPayload(result));
    }

    private async Task<string> EscalateUnsupportedReservationChangeAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken,
        string? forcedReason = null)
    {
        var resolved = await ResolveReservationAsync(arguments, ctx, cancellationToken);
        if (!resolved.Success)
            return resolved.ErrorJson!;

        var reservation = resolved.Reservation!;
        reservation.Status = ReservationStatus.OnHold;
        reservation.CustomerConfirmed = false;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        ctx.ConversationState.LastEscalatedAt = DateTime.UtcNow;
        ctx.ManageableReservations = [reservation];

        var reason = forcedReason ?? ResolveUnsupportedChangeReason(arguments, ctx, ReservationChangePolicy.From(ctx.Config?.ReservationManagement));
        var contactPhone = ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone) ?? string.Empty;
        if (ctx.EscalationContacts.Count > 0)
        {
            try
            {
                await _escalationNotifier.NotifyAsync(
                    ctx.BusinessId,
                    ctx.EscalationContacts,
                    new EscalationNotification(
                        ctx.ConversationId,
                        contactPhone,
                        reason,
                        ctx.LatestUserMessage),
                    cancellationToken);
            }
            catch
            {
                // Notification failures must not prevent the reservation from being placed on hold.
            }
        }

        return ToolResultHelper.OkWithLlm(new
        {
            reservation_id = reservation.ReservationId,
            status = ReservationStatus.OnHold.ToString(),
            escalated = true,
            reason
        }, new
        {
            next_action = "human_handoff",
            reservation_id = reservation.ReservationId,
            status = ReservationStatus.OnHold.ToString(),
            escalated = true,
            reason
        }, EscalatedToHuman);
    }

    private async Task<AvailabilityHint?> BuildAvailabilityHintAsync(
        UpdateReservationChangeRequest request,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(request.ReservationId);
        if (reservation is null)
            return null;

        var service = reservation.Service;
        if (service is null && reservation.ServiceId.HasValue)
            service = await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId.Value);
        if (string.IsNullOrWhiteSpace(service?.ServiceName))
            return null;

        DateOnly date;
        if (request.Date.HasValue)
            date = request.Date.Value;
        else if (reservation.ReservationDateTime.HasValue)
            date = DateOnly.FromDateTime(reservation.ReservationDateTime.Value);
        else
            return null;

        var policy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var availability = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId,
            service.ServiceName,
            date.ToDateTime(TimeOnly.MinValue),
            null,
            policy,
            cancellationToken);

        if (availability.AvailableOptions.Count == 0)
            return new AvailabilityHint(date, []);

        var options = availability.AvailableOptions
            .Select(option => option.Start)
            .Where(option => !string.IsNullOrWhiteSpace(option))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AvailabilityHint(date, options);
    }
    private async Task<string> CompletePaidRescheduleAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        PaymentTransaction? payment = null;

        if (ToolResultHelper.TryGetString(arguments, "payment_transaction_id", out var paymentIdStr)
            && Guid.TryParse(paymentIdStr, out var paymentId))
        {
            payment = await _unitOfWork.PaymentTransactions.GetByIdAsync(paymentId, cancellationToken);
        }

        payment ??= ctx.ActivePayment;
        payment ??= await _paymentLifecycle.GetPendingReschedulingByConversationAsync(ctx.ConversationId, cancellationToken);

        if (payment is null
            || payment.Status != PaymentTransactionStatus.Confirmed
            || !payment.RequiresRescheduling)
        {
            return ToolResultHelper.Error(
                "no_pending_reschedule",
                "There is no confirmed paid reservation pending rescheduling for this conversation.");
        }

        if (payment.ReservationId.HasValue)
        {
            return ToolResultHelper.Ok(new
            {
                reservation_id = payment.ReservationId.Value,
                payment_transaction_id = payment.PaymentTransactionId,
                status = ReservationStatus.Confirmed.ToString(),
                is_booking_confirmed = true,
                idempotent_replay = true
            });
        }

        var dateStr = Coalesce(arguments, "date", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredDate));
        var timeStr = Coalesce(arguments, "time", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime));

        if (string.IsNullOrWhiteSpace(dateStr) || string.IsNullOrWhiteSpace(timeStr))
            return ToolResultHelper.MissingPrerequisites(["date", "time"]);

        if (!AgentDateRules.TryParseDate(dateStr!, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.");
        if (!TimeOnly.TryParse(timeStr, out var time))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.");

        var snapshot = PaymentTransactionSnapshotMapper.ToIntentSnapshot(payment);
        if (snapshot is null)
        {
            return ToolResultHelper.Error(
                "invalid_payment_snapshot",
                "Payment transaction is missing reservation snapshot data.");
        }

        var service = await _unitOfWork.Services.GetByIdAsync(snapshot.ServiceId);
        if (service is null)
            return ToolResultHelper.Error("service_not_found", "Snapshot service no longer exists.");

        snapshot = snapshot with { ServiceName = service.ServiceName };

        var policy = await _schedulingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        var availability = await _availability.CheckAvailabilityAsync(
            ctx.BusinessId,
            service.ServiceName,
            date.ToDateTime(TimeOnly.MinValue),
            time.ToTimeSpan(),
            policy,
            cancellationToken);

        if (!availability.IsAvailable)
        {
            var availableSlots = availability.AvailableOptions
                .Select(option => option.Start)
                .Where(option => !string.IsNullOrWhiteSpace(option))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return ToolResultHelper.ErrorWithLlm(
                "slot_unavailable",
                availability.ResponseMessage ?? "The selected time is not available.",
                null,
                new
                {
                    next_action = "offer_alternative_slots",
                    requested_date = date,
                    available_slots = availableSlots
                },
                recoverable: true);
        }

        var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
        var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;

        _verifications.Record(
            ctx,
            VerificationFactTypes.AvailabilityChecked,
            VerificationSnapshot.FromValues(
                new KeyValuePair<string, string>(serviceKey, service.ServiceName),
                new KeyValuePair<string, string>(dateKey, dateStr!),
                new KeyValuePair<string, string>(timeKey, timeStr!)),
            VerificationTtl.AvailabilityChecked);

        var newDateTime = date.ToDateTime(time);

        CreateReservationResponse response;
        try
        {
            response = await _reservations.CreateFromIntentSnapshotAsync(
                ctx.BusinessId,
                ctx.ConversationId,
                snapshot,
                newDateTime,
                cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return ToolResultHelper.Error("no_employee_available", ex.Message);
        }

        await _paymentLifecycle.LinkReservationAsync(payment, response.ReservationId, cancellationToken);

        var reservation = new Reservation
        {
            ReservationId = response.ReservationId,
            BusinessId = ctx.BusinessId,
            ServiceId = snapshot.ServiceId,
            Service = service,
            ConversationId = ctx.ConversationId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = newDateTime,
            DurationMinutes = snapshot.DurationMinutes,
            CustomerNameSnapshot = snapshot.CustomerName,
            CustomerEmailSnapshot = snapshot.CustomerEmail,
            CustomerPhoneSnapshot = snapshot.CustomerPhone,
            CustomAttributesJson = snapshot.CustomAttributesJson
        };

        ctx.ManageableReservations = [reservation];
        ctx.NotificationContexts["reservation_created"] = new MessageSequenceContext { Reservation = reservation };

        return ToolResultHelper.OkWithEvents(new
        {
            reservation_id = response.ReservationId,
            payment_transaction_id = payment.PaymentTransactionId,
            service = response.ServiceName,
            date = dateStr,
            time = timeStr,
            customer_name = snapshot.CustomerName,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = true
        }, [RequestCompleted], ["reservation_created"]);
    }

    private async Task<string> RequestRescheduleAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        if (HasConcreteChange(arguments))
            return await PreviewOrApplyChangeAsync(arguments, ctx, apply: false, cancellationToken);

        var resolved = await ResolveReservationAsync(arguments, ctx, cancellationToken);
        if (!resolved.Success)
            return resolved.ErrorJson!;

        var reservation = resolved.Reservation!;

        var latestResponse = await _unitOfWork.ReservationAttendanceResponses.GetLatestByReservationAsync(
            ctx.BusinessId,
            reservation.ReservationId,
            cancellationToken);
        if (latestResponse?.ResponseType == ReservationAttendanceResponseType.RescheduleRequested)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToolResultHelper.OkWithLlm(new
            {
                reservation_id = reservation.ReservationId,
                reschedule_requested = true,
                status = reservation.Status.ToString(),
                responded_at_utc = latestResponse.RespondedAtUtc,
                idempotent_replay = true
            }, new
            {
                next_action = "collect_reschedule_target",
                required_fields = ReservationChangePolicy.From(ctx.Config?.ReservationManagement).AutomaticChangeFields
            });
        }

        ToolResultHelper.TryGetString(arguments, "notes", out var notes);
        var response = new ReservationAttendanceResponse
        {
            ReservationAttendanceResponseId = Guid.NewGuid(),
            BusinessId = ctx.BusinessId,
            ReservationId = reservation.ReservationId,
            SourceJobId = resolved.SourceJob?.ScheduledAutomationJobId,
            ResponseType = ReservationAttendanceResponseType.RescheduleRequested,
            RespondedAtUtc = DateTime.UtcNow,
            Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim()
        };

        await _unitOfWork.ReservationAttendanceResponses.AddAsync(response, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToolResultHelper.OkWithLlm(new
        {
            reservation_id = reservation.ReservationId,
            reschedule_requested = true,
            status = reservation.Status.ToString(),
            responded_at_utc = response.RespondedAtUtc
        }, new
        {
            next_action = "collect_reschedule_target",
            required_fields = ReservationChangePolicy.From(ctx.Config?.ReservationManagement).AutomaticChangeFields
        });
    }

    private async Task<string> ConfirmAttendanceAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        if (!ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var confirmed) || !confirmed)
        {
            return ToolResultHelper.ErrorWithLlm(
                "confirmation_required",
                "Customer confirmation is required before registering attendance.",
                null,
                new { next_action = "collect_confirmation", confirmation_type = "attendance" },
                recoverable: true);
        }

        var resolved = await ResolveReservationAsync(arguments, ctx, cancellationToken);
        if (!resolved.Success)
            return resolved.ErrorJson!;

        var reservation = resolved.Reservation!;
        if (!reservation.CustomerConfirmed)
        {
            reservation.CustomerConfirmed = true;
            reservation.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Reservations.UpdateAsync(reservation);
        }

        var latestResponse = await _unitOfWork.ReservationAttendanceResponses.GetLatestByReservationAsync(
            ctx.BusinessId,
            reservation.ReservationId,
            cancellationToken);
        if (latestResponse?.ResponseType == ReservationAttendanceResponseType.Confirmed)
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToolResultHelper.Ok(new
            {
                reservation_id = reservation.ReservationId,
                attendance_confirmed = true,
                responded_at_utc = latestResponse.RespondedAtUtc,
                idempotent_replay = true
            });
        }

        ToolResultHelper.TryGetString(arguments, "notes", out var notes);
        var response = new ReservationAttendanceResponse
        {
            ReservationAttendanceResponseId = Guid.NewGuid(),
            BusinessId = ctx.BusinessId,
            ReservationId = reservation.ReservationId,
            SourceJobId = resolved.SourceJob?.ScheduledAutomationJobId,
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

    private async Task<string> CancelAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        if (!ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var confirmed) || !confirmed)
        {
            return ToolResultHelper.ErrorWithLlm(
                "confirmation_required",
                "Customer confirmation is required before cancelling or suspending a reservation.",
                null,
                new { next_action = "collect_confirmation", confirmation_type = "cancellation" },
                recoverable: true);
        }

        var resolved = await ResolveReservationAsync(arguments, ctx, cancellationToken);
        if (!resolved.Success)
            return resolved.ErrorJson!;

        var success = await _reservations.SuspendAsync(resolved.Reservation!.ReservationId, cancellationToken);
        return success
            ? ToolResultHelper.Ok(new { reservation_id = resolved.Reservation.ReservationId, status = "suspended" })
            : ToolResultHelper.Error("suspend_failed", "The reservation could not be suspended.", "Verify the reservation is in an active state.");
    }

    private async Task<ManageReservationResolveResult> ResolveReservationAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken)
    {
        ToolResultHelper.TryGetString(arguments, "job_id", out var jobIdStr);
        ToolResultHelper.TryGetString(arguments, "reservation_id", out var reservationIdStr);
        jobIdStr = string.IsNullOrWhiteSpace(jobIdStr)
            ? TryParseJobIdFromPayload(ctx)
            : jobIdStr;

        if (Guid.TryParse(jobIdStr, out var jobId))
        {
            var sourceJob = await _unitOfWork.ScheduledAutomationJobs.GetByIdAsync(jobId, cancellationToken);
            if (sourceJob is not null && sourceJob.BusinessId == ctx.BusinessId && sourceJob.Reservation is not null)
                return ManageReservationResolveResult.Ok(sourceJob.Reservation, sourceJob);
        }

        var resolved = await _reservationResolver.ResolveAsync(
            ctx,
            string.IsNullOrWhiteSpace(reservationIdStr) ? null : reservationIdStr,
            cancellationToken);
        return resolved.Success
            ? ManageReservationResolveResult.Ok(resolved.Reservation!)
            : ManageReservationResolveResult.Fail(resolved.ErrorJson ?? ToolResultHelper.Error("reservation_not_found", "No reservation was found.", recoverable: true));
    }

    private static string? TryParseJobIdFromPayload(AgentToolContext ctx)
    {
        var action = ctx.InteractiveAction;
        if (action is null && !InteractivePayloadParser.TryParse(ctx.InteractivePayload, out action))
            return null;

        return action.Scope.Equals("reservation_attendance", StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(action.SourceId, out var jobId)
                ? jobId.ToString("D")
                : null;
    }

    private sealed class ManageReservationResolveResult
    {
        public bool Success { get; init; }
        public Reservation? Reservation { get; init; }
        public ScheduledAutomationJob? SourceJob { get; init; }
        public string? ErrorJson { get; init; }

        public static ManageReservationResolveResult Ok(Reservation reservation, ScheduledAutomationJob? sourceJob = null) =>
            new() { Success = true, Reservation = reservation, SourceJob = sourceJob };

        public static ManageReservationResolveResult Fail(string errorJson) =>
            new() { Success = false, ErrorJson = errorJson };
    }

    private static string ResolveUnsupportedChangeReason(
        JsonElement arguments,
        AgentToolContext ctx,
        ReservationChangePolicy changePolicy)
    {
        var fields = RequestedChangeFields(arguments, changePolicy.KnownChangeFields);
        var reasonCode = ctx.Config?.ReservationManagement.EscalationReasonCode;
        if (string.IsNullOrWhiteSpace(reasonCode))
            return string.Join(",", fields);

        return fields.Count == 0
            ? reasonCode
            : $"{reasonCode}:{string.Join(",", fields)}";
    }
    private static string NormalizeAction(string? action, JsonElement arguments)
    {
        if (!string.IsNullOrWhiteSpace(action))
            return action.Trim().ToLowerInvariant();

        if (ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var confirmed) && confirmed)
            return "apply_change";

        return HasConcreteChange(arguments) ? "preview_change" : string.Empty;
    }

    private static IReadOnlyList<string> RequestedChangeFields(
        JsonElement arguments,
        IReadOnlyList<string> configuredFields)
    {
        var fields = new List<string>();
        foreach (var field in configuredFields)
        {
            if (HasString(arguments, field))
                fields.Add(field);
        }

        return fields;
    }

    private static bool HasConcreteChange(JsonElement arguments) =>
        HasString(arguments, "service")
        || HasString(arguments, "date")
        || HasString(arguments, "time")
        || HasString(arguments, "add_ons");

    private static bool HasString(JsonElement arguments, string property) =>
        ToolResultHelper.TryGetString(arguments, property, out var value)
        && !string.IsNullOrWhiteSpace(value);

    private static string? Coalesce(JsonElement args, string property, string? fallback)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs))
            return fromArgs;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private sealed record AvailabilityHint(DateOnly Date, IReadOnlyList<string> Slots);
}
