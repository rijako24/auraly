using Microsoft.Extensions.Logging;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class ReservationAdminService : IReservationAdminService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly ILogger<ReservationAdminService> _logger;

    public ReservationAdminService(
        IUnitOfWork unitOfWork,
        IAuditService auditService,
        ICorrelationIdProvider correlationIdProvider,
        ILogger<ReservationAdminService> logger)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _correlationIdProvider = correlationIdProvider;
        _logger = logger;
    }

    public async Task<ReservationDto> GetByIdAsync(Guid tenantId, Guid reservationId, CancellationToken ct)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId)
            ?? throw new NotFoundException(nameof(Reservation), reservationId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, reservation.BusinessId, ct);
        return MapToDto(reservation);
    }

    public async Task<IReadOnlyList<ReservationDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAsync(businessId);
        return reservations.Select(MapToDto).ToList();
    }

    public async Task<IReadOnlyList<ReservationDto>> GetByBusinessIdAndDateRangeAsync(
        Guid tenantId, Guid businessId, DateTime startDate, DateTime endDate, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(businessId, startDate, endDate);
        return reservations.Select(MapToDto).ToList();
    }

    public async Task<PagedResponse<ReservationDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request,
        DateTime? startDate, DateTime? endDate, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var (items, totalCount) = await _unitOfWork.Reservations.GetPagedByBusinessIdAsync(
            businessId, request.Page, request.PageSize, request.Search, startDate, endDate, ct);
        return new PagedResponse<ReservationDto>(
            items.Select(MapToDto).ToList(), totalCount, request.Page, request.PageSize);
    }

    public async Task<ReservationDto> CreateAsync(Guid tenantId, CreateReservationRequest request, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, request.BusinessId, ct);

        var service = await _unitOfWork.Services.GetByIdAsync(request.ServiceId)
            ?? throw new NotFoundException(nameof(Service), request.ServiceId);
        if (service.BusinessId != request.BusinessId || !service.IsActive)
            throw new DomainValidationException("Service", "El servicio no es válido para este negocio.");

        var employee = await _unitOfWork.Employees.GetByIdAsync(request.EmployeeId)
            ?? throw new NotFoundException(nameof(Employee), request.EmployeeId);
        if (employee.BusinessId != request.BusinessId || !employee.IsActive)
            throw new DomainValidationException("Employee", "El empleado no es válido para este negocio.");

        var canPerformService = await _unitOfWork.EmployeeServices.GetByEmployeeIdAsync(request.EmployeeId);
        if (!canPerformService.Any(es => es.ServiceId == request.ServiceId))
            throw new DomainValidationException("Employee", "El empleado no ofrece este servicio.");

        var duration = request.DurationMinutes ?? service.DurationMinutes;
        var reservationDate = request.ReservationDateTime.Date;
        var reservationTime = request.ReservationDateTime.TimeOfDay;

        var overlaps = await _unitOfWork.Reservations.ExistsOverlappingReservationAsync(
            request.BusinessId, reservationDate, reservationTime, duration);
        if (overlaps)
            throw new ConflictException("Ya existe una reserva en ese horario para el empleado.");

        var reservation = new Reservation
        {
            ReservationId = Guid.NewGuid(),
            BusinessId = request.BusinessId,
            ServiceId = request.ServiceId,
            EmployeeId = request.EmployeeId,
            ReservationDateTime = request.ReservationDateTime,
            DurationMinutes = duration,
            Status = ReservationStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Reservations.CreateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Create", "Reservation", reservation.ReservationId.ToString(), null, reservation, ct);
        _logger.LogInformation("Reservation created for business {BusinessId} at {DateTime} [CorrelationId: {CorrelationId}]",
            request.BusinessId, request.ReservationDateTime, _correlationIdProvider.CorrelationId);

        var created = await _unitOfWork.Reservations.GetByIdAsync(reservation.ReservationId);
        return MapToDto(created!);
    }

    public async Task<ReservationDto> UpdateAsync(Guid tenantId, Guid reservationId, UpdateReservationRequest request, CancellationToken ct)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId)
            ?? throw new NotFoundException(nameof(Reservation), reservationId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, reservation.BusinessId, ct);

        if (reservation.Status == ReservationStatus.Cancelled)
            throw new DomainValidationException("Reservation", "No se puede modificar una reserva cancelada.");

        var oldState = MapToDto(reservation);

        if (request.ServiceId.HasValue)
        {
            var service = await _unitOfWork.Services.GetByIdAsync(request.ServiceId.Value)
                ?? throw new NotFoundException(nameof(Service), request.ServiceId.Value);
            if (service.BusinessId != reservation.BusinessId || !service.IsActive)
                throw new DomainValidationException("Service", "El servicio no es válido.");
            reservation.ServiceId = request.ServiceId.Value;
        }
        if (request.EmployeeId.HasValue)
        {
            var employee = await _unitOfWork.Employees.GetByIdAsync(request.EmployeeId.Value)
                ?? throw new NotFoundException(nameof(Employee), request.EmployeeId.Value);
            if (employee.BusinessId != reservation.BusinessId || !employee.IsActive)
                throw new DomainValidationException("Employee", "El empleado no es válido.");
            var canPerform = await _unitOfWork.EmployeeServices.GetByEmployeeIdAsync(request.EmployeeId.Value);
            if (!canPerform.Any(es => es.ServiceId == reservation.ServiceId))
                throw new DomainValidationException("Employee", "El empleado no ofrece este servicio.");
            reservation.EmployeeId = request.EmployeeId.Value;
        }
        if (request.ReservationDateTime.HasValue)
            reservation.ReservationDateTime = request.ReservationDateTime.Value;
        if (request.DurationMinutes.HasValue)
            reservation.DurationMinutes = request.DurationMinutes.Value;
        if (request.Status.HasValue)
            reservation.Status = request.Status.Value;

        var overlaps = await _unitOfWork.Reservations.ExistsOverlappingReservationAsync(
            reservation.BusinessId,
            reservation.ReservationDateTime.Date,
            reservation.ReservationDateTime.TimeOfDay,
            reservation.DurationMinutes,
            reservationId);
        if (overlaps)
            throw new ConflictException("La modificación generaría solapamiento con otra reserva.");

        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Update", "Reservation", reservationId.ToString(), oldState, MapToDto(reservation), ct);
        return MapToDto(reservation);
    }

    public async Task CancelAsync(Guid tenantId, Guid reservationId, CancellationToken ct)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(reservationId)
            ?? throw new NotFoundException(nameof(Reservation), reservationId);
        await EnsureBusinessBelongsToTenantAsync(tenantId, reservation.BusinessId, ct);

        if (reservation.Status == ReservationStatus.Cancelled)
            return;

        reservation.Status = ReservationStatus.Cancelled;
        reservation.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Reservations.UpdateAsync(reservation);
        await _unitOfWork.SaveChangesAsync(ct);

        await _auditService.LogAsync("Cancel", "Reservation", reservationId.ToString(), null, null, ct);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }

    private static ReservationDto MapToDto(Reservation r) => new(
        r.ReservationId, r.BusinessId, r.ServiceId,
        r.Service?.ServiceName ?? string.Empty,
        r.EmployeeId, r.Employee?.Name ?? string.Empty,
        r.ReservationDateTime, r.DurationMinutes, r.Status, r.CreatedAt);
}
