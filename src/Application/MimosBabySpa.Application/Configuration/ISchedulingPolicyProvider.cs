namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proveedor de política de agendamiento por negocio (horarios, intervalos, empleados).
/// Fuente única: BusinessConfiguration (Key=SchedulingPolicy).
/// </summary>
public interface ISchedulingPolicyProvider
{
    /// <summary>
    /// Obtiene la política de agendamiento del negocio.
    /// Retorna <see cref="AvailabilityParams.Default"/> si no existe configuración o el JSON es inválido.
    /// </summary>
    Task<AvailabilityParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default);
}
