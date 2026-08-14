namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Proveedor de polÃ­tica de agendamiento por negocio (horarios, intervalos, empleados).
/// Fuente Ãºnica: BusinessSchedulingSettings.
/// </summary>
public interface ISchedulingPolicyProvider
{
    /// <summary>
    /// Obtiene la polÃ­tica de agendamiento del negocio.
    /// Retorna <see cref="AvailabilityParams.Default"/> si no existe configuraciÃ³n o el JSON es invÃ¡lido.
    /// </summary>
    Task<AvailabilityParams> GetAsync(Guid businessId, CancellationToken cancellationToken = default);
}

