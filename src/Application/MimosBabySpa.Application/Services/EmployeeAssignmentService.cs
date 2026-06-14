using Microsoft.Extensions.Logging;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Implementación del servicio de asignación de empleados.
/// 
/// Aplica reglas de negocio para encontrar el mejor empleado disponible:
/// - Validación de especialidad (capacidad del empleado)
/// - Validación de disponibilidad horaria (sin solapamientos)
/// - Preservación de recursos (seleccionar empleado menos polivalente cuando ambos pueden dar el servicio)
/// </summary>
public class EmployeeAssignmentService : IEmployeeAssignmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IWorkingHoursService _workingHoursService;
    private readonly ILogger<EmployeeAssignmentService> _logger;

    public EmployeeAssignmentService(
        IUnitOfWork unitOfWork,
        IWorkingHoursService workingHoursService,
        ILogger<EmployeeAssignmentService> logger)
    {
        _unitOfWork = unitOfWork;
        _workingHoursService = workingHoursService;
        _logger = logger;
    }

    public async Task<Employee?> FindBestAvailableEmployeeAsync(
        Guid businessId,
        Guid serviceId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default,
        Guid? preferredEmployeeId = null)
    {
        _logger.LogInformation(
            "Buscando mejor empleado disponible: BusinessId={BusinessId}, ServiceId={ServiceId}, StartTime={StartTime}, EndTime={EndTime}, Preferred={PreferredEmployeeId}",
            businessId, serviceId, startTime, endTime, preferredEmployeeId);

        var capableEmployees = await _unitOfWork.Employees.GetByBusinessIdAndServiceIdAsync(
            businessId, 
            serviceId);

        if (!capableEmployees.Any())
        {
            _logger.LogWarning(
                "No hay empleados capaces de dar el servicio {ServiceId} en el negocio {BusinessId}",
                serviceId, businessId);
            return null;
        }

        var availableEmployees = await FilterAvailableEmployeesAsync(
            capableEmployees,
            businessId,
            startTime,
            endTime,
            cancellationToken);

        if (!availableEmployees.Any())
        {
            _logger.LogWarning(
                "No hay empleados disponibles (sin solapamientos) para el servicio {ServiceId} en el horario {StartTime}-{EndTime}",
                serviceId, startTime, endTime);
            return null;
        }

        if (preferredEmployeeId.HasValue)
        {
            var preferred = availableEmployees.FirstOrDefault(e => e.EmployeeId == preferredEmployeeId.Value);
            if (preferred is not null)
            {
                _logger.LogInformation(
                    "Empleado preferido disponible: {EmployeeId} ({EmployeeName})",
                    preferred.EmployeeId, preferred.Name);
                return preferred;
            }

            _logger.LogInformation(
                "Empleado preferido {PreferredEmployeeId} no disponible; seleccionando alternativa",
                preferredEmployeeId.Value);
        }

        var bestEmployee = await SelectBestEmployeeByResourcePreservationAsync(
            availableEmployees,
            businessId,
            startTime.Date,
            cancellationToken);

        if (bestEmployee != null)
        {
            var serviceCount = await _unitOfWork.EmployeeServices.GetServiceCountByEmployeeIdAsync(bestEmployee.EmployeeId);
            _logger.LogInformation(
                "Empleado seleccionado: {EmployeeId} ({EmployeeName}) con {ServiceCount} servicios disponibles",
                bestEmployee.EmployeeId, bestEmployee.Name, serviceCount);
        }

        return bestEmployee;
    }

    /// <summary>
    /// Filtra empleados que no tienen reservas solapadas en el horario especificado.
    /// </summary>
    private async Task<IEnumerable<Employee>> FilterAvailableEmployeesAsync(
        IEnumerable<Employee> employees,
        Guid businessId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken)
    {
        // Obtener todas las reservas del día una sola vez para optimizar
        var dayReservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId,
            startTime.Date,
            startTime.Date.AddDays(1).AddMinutes(-1));

        var activeReservations = dayReservations
            .Where(r => r.Status != ReservationStatus.Cancelled)
            .ToList();

        var availableEmployees = new List<Employee>();

        foreach (var employee in employees)
        {
            var hasOverlap = HasScheduleOverlap(
                employee.EmployeeId,
                activeReservations,
                startTime,
                endTime);

            if (!hasOverlap)
            {
                var worksAtRequestedTime = await WorksAtRequestedTimeAsync(
                    businessId,
                    employee.EmployeeId,
                    startTime,
                    endTime,
                    cancellationToken);

                if (worksAtRequestedTime)
                    availableEmployees.Add(employee);
            }
        }

        return availableEmployees;
    }

    private async Task<bool> WorksAtRequestedTimeAsync(
        Guid businessId,
        Guid employeeId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken)
    {
        var blocks = await _workingHoursService.GetEffectiveWorkingHoursAsync(
            businessId,
            employeeId,
            DateOnly.FromDateTime(startTime),
            cancellationToken);

        var start = startTime.TimeOfDay;
        var end = endTime.TimeOfDay;
        return blocks.Any(block =>
            block.IsValid() &&
            block.OpenTime <= start &&
            block.CloseTime >= end);
    }

    /// <summary>
    /// Verifica si un empleado tiene reservas solapadas en el horario especificado.
    /// Dos intervalos se solapan si: start1 < end2 && end1 > start2
    /// Versión optimizada que recibe las reservas ya cargadas.
    /// </summary>
    private bool HasScheduleOverlap(
        Guid employeeId,
        IEnumerable<Reservation> dayReservations,
        DateTime startTime,
        DateTime endTime)
    {
        // Filtrar reservas del empleado específico y que se solapen
        var employeeReservations = dayReservations
            .Where(r => r.EmployeeId == employeeId);

        foreach (var reservation in employeeReservations)
        {
            var reservationEnd = reservation.EndDateTime;

            // Verificar solapamiento: start1 < end2 && end1 > start2
            if (reservation.ReservationDateTime < endTime && reservationEnd > startTime)
            {
                _logger.LogDebug(
                    "Empleado {EmployeeId} tiene solapamiento: Reserva {ReservationId} ({ReservationStart}-{ReservationEnd}) con ({RequestedStart}-{RequestedEnd})",
                    employeeId, reservation.ReservationId, reservation.ReservationDateTime, reservationEnd, startTime, endTime);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Selecciona el mejor empleado basándose en la regla de preservación de recursos.
    /// 
    /// Regla de negocio: Si dos empleados pueden dar el mismo servicio, usar primero al que MENOS servicios puede brindar.
    /// Esto preserva al empleado más polivalente para servicios que solo él puede ofrecer.
    /// 
    /// Ordenamiento:
    /// 1) Menor número de servicios que puede cubrir (ASC) - preserva empleados versátiles
    /// 2) Menor carga del día (ASC) - desempate por disponibilidad actual
    /// </summary>
    private async Task<Employee?> SelectBestEmployeeByResourcePreservationAsync(
        IEnumerable<Employee> employees,
        Guid businessId,
        DateTime reservationDate,
        CancellationToken cancellationToken)
    {
        // Obtener reservas del día una sola vez para optimizar consultas
        var dayReservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId,
            reservationDate,
            reservationDate.AddDays(1).AddMinutes(-1));

        var activeDayReservations = dayReservations
            .Where(r => r.Status != ReservationStatus.Cancelled)
            .ToList();

        var employeesWithMetrics = new List<(Employee Employee, int ServiceCount, int DayReservationCount)>();

        foreach (var employee in employees)
        {
            // Calcular número total de servicios que puede cubrir (polivalencia)
            var serviceCount = await _unitOfWork.EmployeeServices.GetServiceCountByEmployeeIdAsync(employee.EmployeeId);

            // Calcular carga del día (número de reservas activas en el día de la reserva)
            var dayReservationCount = activeDayReservations
                .Count(r => r.EmployeeId == employee.EmployeeId);

            employeesWithMetrics.Add((employee, serviceCount, dayReservationCount));
        }

        // Ordenar por: 1) Menor polivalencia (ASC) - preserva empleados versátiles, 2) Menor carga del día (ASC) - desempate
        var bestEmployee = employeesWithMetrics
            .OrderBy(e => e.ServiceCount) // Menor número de servicios primero (preserva al más versátil)
            .ThenBy(e => e.DayReservationCount) // Menor carga del día como desempate
            .FirstOrDefault();

        if (bestEmployee.Employee != null)
        {
            _logger.LogInformation(
                "Empleado seleccionado por preservación de recursos: {EmployeeId} ({EmployeeName}) - {ServiceCount} servicios disponibles, {DayReservationCount} reservas en el día. " +
                "Seleccionado para preservar empleados más versátiles.",
                bestEmployee.Employee.EmployeeId,
                bestEmployee.Employee.Name,
                bestEmployee.ServiceCount,
                bestEmployee.DayReservationCount);
        }

        return bestEmployee.Employee;
    }
}
