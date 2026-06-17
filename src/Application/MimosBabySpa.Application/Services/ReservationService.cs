using System.Text.Json;
using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

public class ReservationService : IReservationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICalendarService _calendarService;
    private readonly IEmployeeAssignmentService _employeeAssignmentService;
    private readonly IAddOnCatalogService _addOnCatalog;
    private readonly ServiceNameResolver _serviceNameResolver;
    private readonly ILogger<ReservationService> _logger;

    public ReservationService(
        IUnitOfWork unitOfWork,
        ICalendarService calendarService,
        IIntegrationsConfigProvider integrationsProvider,
        IEmployeeAssignmentService employeeAssignmentService,
        IAddOnCatalogService addOnCatalog,
        ServiceNameResolver serviceNameResolver,
        ILogger<ReservationService> logger)
    {
        _unitOfWork = unitOfWork;
        _calendarService = calendarService;
        _employeeAssignmentService = employeeAssignmentService;
        _addOnCatalog = addOnCatalog;
        _serviceNameResolver = serviceNameResolver;
        _logger = logger;
    }

    public async Task<CreateReservationResponse> CreateReservationAsync(
        CreateReservationRequest request,
        CancellationToken cancellationToken = default)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(request.BusinessId)
            ?? throw new InvalidOperationException($"El negocio con ID {request.BusinessId} no existe.");

        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(request.BusinessId, request.ServiceName)
            ?? throw new InvalidOperationException($"El servicio '{request.ServiceName}' no existe en el sistema.");

        var duration = service.DurationMinutes > 0 ? service.DurationMinutes : 60;
        var reservationDateTime = request.Date.ToDateTime(request.Time);
        var employee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
            request.BusinessId,
            service.ServiceId,
            reservationDateTime,
            reservationDateTime.AddMinutes(duration),
            cancellationToken)
            ?? throw new InvalidOperationException("No hay empleado disponible para este horario. Por favor intenta con otra fecha u hora.");

        var addOnsCsv = ReservationBusinessAttributeKeys.GetSelectedAddOnsCsv(request.BusinessAttributes);
        var (addOnServiceIds, addOnNames) = await ResolveAddOnsAsync(request.BusinessId, addOnsCsv, cancellationToken);

        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = request.BusinessId,
            ServiceId = service.ServiceId,
            EmployeeId = employee.EmployeeId,
            ReservationDateTime = reservationDateTime,
            DurationMinutes = duration,
            Status = ReservationStatus.Confirmed,
            ConversationId = request.ConversationId,
            CustomerNameSnapshot = request.CustomerName?.Trim(),
            CustomerEmailSnapshot = request.Email?.Trim(),
            CustomerPhoneSnapshot = request.Phone?.Trim(),
            CustomAttributesJson = request.CustomAttributesJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createdReservation = await _unitOfWork.Reservations.CreateAsync(reservation);

        foreach (var (addOnServiceId, addOnService) in addOnServiceIds)
        {
            await _unitOfWork.ReservationAddOns.AddAsync(new ReservationAddOn
            {
                ReservationAddOnId = Guid.NewGuid(),
                ReservationId = createdReservation.ReservationId,
                AddOnServiceId = addOnServiceId,
                PriceSnapshot = addOnService.Price
            });
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await SyncReservationToCalendarAsync(
            createdReservation,
            service.ServiceName,
            BuildMetadata(request),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Reserva creada exitosamente: {ReservationId} para servicio {ServiceName} el {DateTime} en negocio {BusinessId}",
            createdReservation.ReservationId,
            service.ServiceName,
            createdReservation.ReservationDateTime,
            business.BusinessId);

        return new CreateReservationResponse(
            createdReservation.ReservationId,
            service.ServiceName,
            employee.Name,
            request.Date,
            request.Time,
            duration,
            addOnNames);
    }

    public async Task<CreateReservationResponse> CreateFromIntentSnapshotAsync(
        Guid businessId,
        Guid conversationId,
        ReservationIntentSnapshot snapshot,
        DateTime reservationDateTime,
        CancellationToken cancellationToken = default)
    {
        var employee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
            businessId,
            snapshot.ServiceId,
            reservationDateTime,
            reservationDateTime.AddMinutes(snapshot.DurationMinutes),
            cancellationToken,
            preferredEmployeeId: snapshot.PreferredEmployeeId)
            ?? throw new InvalidOperationException("No hay empleado disponible para este horario. Por favor intenta con otra fecha u hora.");

        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = businessId,
            ServiceId = snapshot.ServiceId,
            EmployeeId = employee.EmployeeId,
            ReservationDateTime = reservationDateTime,
            DurationMinutes = snapshot.DurationMinutes,
            Status = ReservationStatus.Confirmed,
            ConversationId = conversationId,
            CustomerNameSnapshot = snapshot.CustomerName?.Trim(),
            CustomerEmailSnapshot = snapshot.CustomerEmail?.Trim(),
            CustomerPhoneSnapshot = snapshot.CustomerPhone?.Trim(),
            CustomAttributesJson = snapshot.CustomAttributesJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var createdReservation = await _unitOfWork.Reservations.CreateAsync(reservation);
        var addOnNames = new List<string>();

        foreach (var addOnServiceId in snapshot.AddOnServiceIds)
        {
            var addOnService = await _unitOfWork.Services.GetByIdAsync(addOnServiceId);
            if (addOnService is null)
                continue;

            await _unitOfWork.ReservationAddOns.AddAsync(new ReservationAddOn
            {
                ReservationAddOnId = Guid.NewGuid(),
                ReservationId = createdReservation.ReservationId,
                AddOnServiceId = addOnServiceId,
                PriceSnapshot = addOnService.Price
            });
            addOnNames.Add(addOnService.ServiceName);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await SyncReservationToCalendarAsync(
            createdReservation,
            snapshot.ServiceName,
            BuildMetadataFromSnapshot(snapshot),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new CreateReservationResponse(
            createdReservation.ReservationId,
            snapshot.ServiceName,
            employee.Name,
            DateOnly.FromDateTime(reservationDateTime),
            TimeOnly.FromDateTime(reservationDateTime),
            snapshot.DurationMinutes,
            addOnNames);
    }

    public async Task<ReservationDto?> GetReservationByIdAsync(Guid reservationId)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        return reservation != null ? MapToDto(reservation) : null;
    }

    public async Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAsync(Guid businessId)
    {
        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAsync(businessId);
        return reservations.Select(MapToDto);
    }

    public async Task<IEnumerable<ReservationDto>> GetReservationsByBusinessIdAndDateRangeAsync(
        Guid businessId,
        DateTime startDate,
        DateTime endDate)
    {
        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId,
            startDate,
            endDate);
        return reservations.Select(MapToDto);
    }

    public async Task<bool> SuspendAsync(Guid reservationId, CancellationToken cancellationToken = default)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        if (reservation == null)
            return false;

        if (reservation.Status == ReservationStatus.OnHold)
            return true;

        reservation.Status = ReservationStatus.OnHold;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RescheduleAsync(Guid reservationId, DateOnly newDate, TimeOnly newTime, CancellationToken cancellationToken = default)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId);
        if (reservation == null || reservation.Status == ReservationStatus.Cancelled || !reservation.ServiceId.HasValue)
            return false;

        var service = reservation.Service ?? await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId.Value);
        if (service == null)
            return false;

        var duration = service.DurationMinutes > 0 ? service.DurationMinutes : 60;
        var reservationDateTime = newDate.ToDateTime(newTime);
        var employee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
            reservation.BusinessId,
            service.ServiceId,
            reservationDateTime,
            reservationDateTime.AddMinutes(duration),
            cancellationToken);

        if (employee == null)
            return false;

        if (reservation.Status == ReservationStatus.OnHold)
            reservation.Status = ReservationStatus.Confirmed;

        reservation.ReservationDateTime = reservationDateTime;
        reservation.DurationMinutes = duration;
        reservation.EmployeeId = employee.EmployeeId;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await SyncReservationToCalendarAsync(
            reservation,
            service.ServiceName,
            new Dictionary<string, string>(),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<UpdateReservationChangeResult> UpdateReservationAsync(
        UpdateReservationChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(request.ReservationId);
        if (reservation is null)
        {
            return UpdateReservationChangeResult.Fail(
                "reservation_not_found",
                "Reservation was not found.",
                null,
                request.ReservationId);
        }

        if (reservation.Status is ReservationStatus.Cancelled or ReservationStatus.Completed)
        {
            return UpdateReservationChangeResult.Fail(
                "reservation_not_manageable",
                "Reservation can no longer be changed.",
                "Only upcoming confirmed or on-hold reservations can be changed.",
                request.ReservationId);
        }

        var currentService = reservation.Service
            ?? (reservation.ServiceId.HasValue
                ? await _unitOfWork.Services.GetByIdAsync(reservation.ServiceId.Value)
                : null);
        if (currentService is null)
        {
            return UpdateReservationChangeResult.Fail(
                "service_not_found",
                "The reservation has no service assigned.",
                "Escalate to a human to review this reservation.",
                request.ReservationId);
        }

        var targetService = currentService;
        if (!string.IsNullOrWhiteSpace(request.ServiceName))
        {
            var canonical = await _serviceNameResolver.ResolveAsync(
                reservation.BusinessId,
                request.ServiceName,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(canonical))
            {
                return UpdateReservationChangeResult.Fail(
                    "service_not_found",
                    $"Service '{request.ServiceName}' was not found in this business catalog.",
                    "Use get_service_catalog and ask the customer to choose an exact service.",
                    request.ReservationId);
            }

            targetService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(reservation.BusinessId, canonical);
            if (targetService is null)
            {
                return UpdateReservationChangeResult.Fail(
                    "service_not_found",
                    $"Service '{canonical}' was not found in this business catalog.",
                    "Use get_service_catalog and ask the customer to choose an exact service.",
                    request.ReservationId);
            }
        }

        var targetDateTime = reservation.ReservationDateTime;
        if (request.Date.HasValue || request.Time.HasValue)
        {
            if (!request.Date.HasValue && !reservation.ReservationDateTime.HasValue)
            {
                return UpdateReservationChangeResult.Fail(
                    "date_required",
                    "A date is required to change the reservation time.",
                    "Ask for the desired date.",
                    request.ReservationId);
            }

            if (!request.Time.HasValue && !reservation.ReservationDateTime.HasValue)
            {
                return UpdateReservationChangeResult.Fail(
                    "time_required",
                    "A time is required to change the reservation date.",
                    "Ask for the desired time.",
                    request.ReservationId);
            }

            var date = request.Date ?? DateOnly.FromDateTime(reservation.ReservationDateTime!.Value);
            var time = request.Time ?? TimeOnly.FromDateTime(reservation.ReservationDateTime!.Value);
            targetDateTime = date.ToDateTime(time);
        }

        if (!targetDateTime.HasValue)
        {
            return UpdateReservationChangeResult.Fail(
                "datetime_required",
                "Reservation date and time are required before applying this change.",
                "Ask for date and time.",
                request.ReservationId);
        }

        var targetDuration = targetService.DurationMinutes > 0 ? targetService.DurationMinutes : 60;
        var serviceChanged = reservation.ServiceId != targetService.ServiceId;
        var dateTimeChanged = reservation.ReservationDateTime != targetDateTime;
        var mustReassignEmployee = serviceChanged || dateTimeChanged || reservation.EmployeeId is null;
        Employee? targetEmployee = reservation.Employee;

        if (mustReassignEmployee)
        {
            targetEmployee = await _employeeAssignmentService.FindBestAvailableEmployeeAsync(
                reservation.BusinessId,
                targetService.ServiceId,
                targetDateTime.Value,
                targetDateTime.Value.AddMinutes(targetDuration),
                cancellationToken,
                preferredEmployeeId: serviceChanged ? null : reservation.EmployeeId);

            if (targetEmployee is null)
            {
                return UpdateReservationChangeResult.Fail(
                    "slot_unavailable",
                    "No employee is available for the requested service and time.",
                    "Call check_availability or ask for another date/time.",
                    request.ReservationId);
            }
        }

        var currentAddOnsCsv = await BuildCurrentAddOnsCsvAsync(reservation.ReservationId, cancellationToken);
        var targetAddOnsCsv = BuildTargetAddOnsCsv(
            currentAddOnsCsv,
            request.AddOnsCsv,
            request.AddOnsMode);

        if (!string.IsNullOrWhiteSpace(targetAddOnsCsv))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                reservation.BusinessId,
                targetService.ServiceName,
                targetAddOnsCsv,
                cancellationToken);
            if (!validation.IsValid)
            {
                return UpdateReservationChangeResult.Fail(
                    validation.ErrorCode ?? "invalid_add_ons",
                    validation.ErrorMessage ?? "Invalid add-on selection.",
                    validation.Hint,
                    request.ReservationId);
            }

            targetAddOnsCsv = validation.NormalizedCsv;
        }

        var newTotal = await ResolveReservationTotalAsync(
            reservation.BusinessId,
            targetService.ServiceName,
            targetAddOnsCsv,
            cancellationToken);
        var targetAddOnNames = SplitCsv(targetAddOnsCsv).ToList();

        if (!request.Apply)
        {
            return new UpdateReservationChangeResult(
                true,
                null,
                null,
                null,
                reservation.ReservationId,
                targetService.ServiceName,
                DateOnly.FromDateTime(targetDateTime.Value),
                TimeOnly.FromDateTime(targetDateTime.Value),
                targetEmployee?.Name ?? reservation.Employee?.Name,
                targetDuration,
                targetAddOnNames,
                newTotal,
                "No additional online payment is required for reservation changes after payment; any remaining balance is handled at the venue.",
                Applied: false);
        }

        reservation.ServiceId = targetService.ServiceId;
        reservation.ReservationDateTime = targetDateTime;
        reservation.DurationMinutes = targetDuration;
        if (targetEmployee is not null)
            reservation.EmployeeId = targetEmployee.EmployeeId;
        if (reservation.Status == ReservationStatus.OnHold)
            reservation.Status = ReservationStatus.Confirmed;
        reservation.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await SyncReservationAddOnsAsync(reservation, targetAddOnsCsv, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await SyncReservationToCalendarAsync(
            reservation,
            targetService.ServiceName,
            new Dictionary<string, string>
            {
                [ReservationBusinessAttributeKeys.SelectedAddOns] = targetAddOnsCsv ?? string.Empty
            },
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new UpdateReservationChangeResult(
            true,
            null,
            null,
            null,
            reservation.ReservationId,
            targetService.ServiceName,
            DateOnly.FromDateTime(targetDateTime.Value),
            TimeOnly.FromDateTime(targetDateTime.Value),
            targetEmployee?.Name ?? reservation.Employee?.Name,
            targetDuration,
            targetAddOnNames,
            newTotal,
            "No additional online payment is required for reservation changes after payment; any remaining balance is handled at the venue.",
            Applied: true);
    }

    private async Task SyncReservationToCalendarAsync(
        Reservation reservation,
        string serviceName,
        Dictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        if (!reservation.ReservationDateTime.HasValue || !reservation.EndDateTime.HasValue)
            return;

        var connection = await _unitOfWork.IntegrationConnections.GetByBusinessProviderCapabilityAsync(
            reservation.BusinessId,
            IntegrationProvider.GoogleCalendar,
            IntegrationCapability.Calendar,
            cancellationToken);

        if (connection is null || !connection.IsEnabled)
            return;

        var existing = await _unitOfWork.ReservationIntegrationEvents.GetByReservationAndConnectionAsync(
            reservation.ReservationId,
            connection.IntegrationConnectionId,
            cancellationToken);

        var integrationEvent = existing ?? new ReservationIntegrationEvent
        {
            ReservationIntegrationEventId = Guid.NewGuid(),
            BusinessId = reservation.BusinessId,
            ReservationId = reservation.ReservationId,
            IntegrationConnectionId = connection.IntegrationConnectionId,
            Provider = IntegrationProvider.GoogleCalendar,
            Capability = IntegrationCapability.Calendar,
            Status = IntegrationEventStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            var calendarEvent = BuildCalendarEvent(reservation, serviceName, metadata);
            if (string.IsNullOrWhiteSpace(integrationEvent.ExternalEventId))
            {
                integrationEvent.ExternalEventId = await _calendarService.CreateEventAsync(
                    reservation.BusinessId,
                    calendarEvent,
                    cancellationToken);
            }
            else
            {
                await _calendarService.UpdateEventAsync(
                    reservation.BusinessId,
                    integrationEvent.ExternalEventId,
                    calendarEvent,
                    cancellationToken);
            }

            integrationEvent.Status = IntegrationEventStatus.Synced;
            integrationEvent.LastError = null;
            integrationEvent.UpdatedAt = DateTime.UtcNow;
            connection.LastSyncAt = DateTime.UtcNow;
            connection.LastError = null;
            connection.UpdatedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            integrationEvent.Status = IntegrationEventStatus.Failed;
            integrationEvent.LastError = ex.Message;
            integrationEvent.UpdatedAt = DateTime.UtcNow;
            connection.LastError = ex.Message;
            connection.UpdatedAt = DateTime.UtcNow;
            _logger.LogWarning(ex, "No se pudo sincronizar reserva {ReservationId} con Google Calendar", reservation.ReservationId);
        }

        if (existing is null)
            await _unitOfWork.ReservationIntegrationEvents.AddAsync(integrationEvent, cancellationToken);
        else
            await _unitOfWork.ReservationIntegrationEvents.UpdateAsync(integrationEvent, cancellationToken);

        await _unitOfWork.IntegrationConnections.UpdateAsync(connection, cancellationToken);
    }

    private async Task<string?> BuildCurrentAddOnsCsvAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        var addOns = await _unitOfWork.ReservationAddOns.GetByReservationIdAsync(reservationId);
        if (addOns.Count == 0)
            return null;

        var names = new List<string>();
        foreach (var addOn in addOns)
        {
            var service = addOn.AddOnService
                ?? await _unitOfWork.Services.GetByIdAsync(addOn.AddOnServiceId);
            if (!string.IsNullOrWhiteSpace(service?.ServiceName))
                names.Add(service.ServiceName);
        }

        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private async Task<decimal> ResolveReservationTotalAsync(
        Guid businessId,
        string serviceName,
        string? addOnsCsv,
        CancellationToken cancellationToken)
    {
        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, serviceName);
        var total = service?.IncludeInCheckoutTotal == false ? 0m : service?.Price ?? 0m;

        foreach (var addOnName in SplitCsv(addOnsCsv))
        {
            var addOn = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, addOnName);
            if (addOn?.IncludeInCheckoutTotal != false)
                total += addOn?.Price ?? 0m;
        }

        return total;
    }

    private async Task SyncReservationAddOnsAsync(
        Reservation reservation,
        string? addOnsCsv,
        CancellationToken cancellationToken)
    {
        var existing = await _unitOfWork.ReservationAddOns.GetByReservationIdAsync(reservation.ReservationId);
        foreach (var addOn in existing)
            await _unitOfWork.ReservationAddOns.DeleteAsync(addOn);

        foreach (var addOnName in SplitCsv(addOnsCsv))
        {
            var addOnService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(
                reservation.BusinessId,
                addOnName);
            if (addOnService is null)
                continue;

            await _unitOfWork.ReservationAddOns.AddAsync(new ReservationAddOn
            {
                ReservationAddOnId = Guid.NewGuid(),
                ReservationId = reservation.ReservationId,
                AddOnServiceId = addOnService.ServiceId,
                PriceSnapshot = addOnService.Price
            });
        }

        reservation.AddOns.Clear();
    }

    private static string? BuildTargetAddOnsCsv(
        string? currentCsv,
        string? requestedCsv,
        string? mode)
    {
        if (string.IsNullOrWhiteSpace(requestedCsv))
            return currentCsv;

        var normalizedMode = string.IsNullOrWhiteSpace(mode)
            ? "add"
            : mode.Trim().ToLowerInvariant();

        if (normalizedMode == "replace")
            return NormalizeCsv(requestedCsv);

        var current = SplitCsv(currentCsv).ToList();
        var requested = SplitCsv(requestedCsv).ToList();

        if (normalizedMode == "remove")
        {
            current.RemoveAll(existing =>
                requested.Any(remove => string.Equals(existing, remove, StringComparison.OrdinalIgnoreCase)));
            return current.Count == 0 ? null : string.Join(", ", current);
        }

        foreach (var addOn in requested)
        {
            if (!current.Any(existing => string.Equals(existing, addOn, StringComparison.OrdinalIgnoreCase)))
                current.Add(addOn);
        }

        return current.Count == 0 ? null : string.Join(", ", current);
    }

    private static string? NormalizeCsv(string? csv)
    {
        var parts = SplitCsv(csv).ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static IEnumerable<string> SplitCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)
            || string.Equals(csv.Trim(), "ninguno", StringComparison.OrdinalIgnoreCase))
        {
            yield break;
        }

        foreach (var part in csv.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!string.Equals(part, "ninguno", StringComparison.OrdinalIgnoreCase))
                yield return part;
        }
    }

    private static CalendarEvent BuildCalendarEvent(
        Reservation reservation,
        string serviceName,
        Dictionary<string, string> metadata)
    {
        var reservationDateTime = reservation.ReservationDateTime!.Value;
        var endDateTime = reservation.EndDateTime!.Value;
        var titleParts = new List<string> { $"[{serviceName}] Reserva" };

        foreach (var kvp in metadata)
        {
            if (!string.IsNullOrWhiteSpace(kvp.Value))
                titleParts.Add(kvp.Value);
        }

        var description = $"""
        Reserva confirmada

        Servicio: {serviceName}
        Fecha: {reservationDateTime:dd/MM/yyyy}
        Hora: {reservationDateTime:HH:mm}
        Duracion: {reservation.DurationMinutes} minutos
        """;

        if (metadata.Count > 0)
        {
            description += "\n\nInformacion adicional:\n";
            foreach (var kvp in metadata)
                description += $"{kvp.Key}: {kvp.Value}\n";
        }

        return new CalendarEvent
        {
            Title = string.Join(" - ", titleParts),
            Description = description,
            StartDateTime = reservationDateTime,
            EndDateTime = endDateTime,
            ExtendedProperties = new Dictionary<string, string>
            {
                { "ReservationId", reservation.ReservationId.ToString() },
                { "BusinessId", reservation.BusinessId.ToString() }
            }
        };
    }

    private async Task<(List<(Guid Id, Service Entity)>, IReadOnlyList<string>)> ResolveAddOnsAsync(
        Guid businessId,
        string? selectedAddOnsCsv,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedAddOnsCsv))
            return ([], []);

        var names = selectedAddOnsCsv
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

        var addOns = new List<(Guid Id, Service Entity)>();
        var resolvedNames = new List<string>();

        foreach (var name in names)
        {
            var addOnService = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(businessId, name.Trim());
            if (addOnService == null)
                continue;

            addOns.Add((addOnService.ServiceId, addOnService));
            resolvedNames.Add(addOnService.ServiceName);
        }

        return (addOns, resolvedNames);
    }

    private static Dictionary<string, string> BuildMetadata(CreateReservationRequest request)
    {
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(request.CustomerName))
            metadata["CustomerName"] = request.CustomerName;
        if (!string.IsNullOrWhiteSpace(request.Email))
            metadata["Email"] = request.Email;
        if (!string.IsNullOrWhiteSpace(request.Phone))
            metadata["Phone"] = request.Phone;

        foreach (var kvp in request.BusinessAttributes)
            metadata[kvp.Key] = kvp.Value;

        return metadata;
    }

    private static Dictionary<string, string> BuildMetadataFromSnapshot(ReservationIntentSnapshot snapshot)
    {
        var metadata = new Dictionary<string, string>();

        if (!string.IsNullOrWhiteSpace(snapshot.CustomerName))
            metadata["CustomerName"] = snapshot.CustomerName;
        if (!string.IsNullOrWhiteSpace(snapshot.CustomerEmail))
            metadata["Email"] = snapshot.CustomerEmail;
        if (!string.IsNullOrWhiteSpace(snapshot.CustomerPhone))
            metadata["Phone"] = snapshot.CustomerPhone;

        if (string.IsNullOrWhiteSpace(snapshot.CustomAttributesJson) || snapshot.CustomAttributesJson == "{}")
            return metadata;

        try
        {
            var custom = JsonSerializer.Deserialize<Dictionary<string, string>>(snapshot.CustomAttributesJson);
            if (custom is null)
                return metadata;

            foreach (var kvp in custom)
                metadata[kvp.Key] = kvp.Value;
        }
        catch
        {
            // Metadata is best effort.
        }

        return metadata;
    }

    private static ReservationDto MapToDto(Reservation reservation)
    {
        return new ReservationDto
        {
            ReservationId = reservation.ReservationId,
            BusinessId = reservation.BusinessId,
            ServiceId = reservation.ServiceId,
            EmployeeId = reservation.EmployeeId,
            ServiceName = reservation.Service?.ServiceName ?? string.Empty,
            EmployeeName = reservation.Employee?.Name ?? string.Empty,
            ReservationDateTime = reservation.ReservationDateTime,
            DurationMinutes = reservation.DurationMinutes,
            Status = reservation.Status,
            ConversationId = reservation.ConversationId,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt
        };
    }
}
