using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Application.Services;

/// <summary>
/// Servicio para asignar empleados a reservas basándose en:
/// - Capacidad del empleado (especialidad)
/// - Disponibilidad horaria (sin solapamientos)
/// - Preservación de recursos: si dos empleados pueden dar el mismo servicio,
///   se selecciona primero al que menos servicios puede brindar para preservar
///   al empleado más versátil para servicios que solo él puede ofrecer.
/// </summary>
public interface IEmployeeAssignmentService
{
    /// <summary>
    /// Encuentra el mejor empleado disponible para un servicio en un horario específico.
    /// 
    /// Reglas aplicadas:
    /// 1. Especialidad: Solo empleados con capacidad para el servicio solicitado
    /// 2. Disponibilidad horaria: Excluye empleados con reservas solapadas
    /// 3. Preservación de recursos: Si múltiples empleados pueden dar el servicio,
    ///    selecciona primero al que menos servicios puede brindar para preservar
    ///    al empleado más versátil para servicios exclusivos.
    /// </summary>
    /// <param name="businessId">ID del negocio</param>
    /// <param name="serviceId">ID del servicio a asignar</param>
    /// <param name="startTime">Fecha y hora de inicio de la reserva</param>
    /// <param name="endTime">Fecha y hora de fin de la reserva</param>
    /// <param name="cancellationToken">Token de cancelación</param>
    /// <returns>El empleado más adecuado o null si no hay disponibilidad</returns>
    Task<Employee?> FindBestAvailableEmployeeAsync(
        Guid businessId,
        Guid serviceId,
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default,
        Guid? preferredEmployeeId = null);
}
