namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proveedor de configuración de integraciones por negocio (Google Calendar, Wompi, etc.).
/// Fuente única: BusinessConfiguration (Key=Integrations).
/// Reemplaza IOptions&lt;CalendarSettings&gt; y IOptions&lt;WompiSettings&gt; para config multi-tenant.
/// </summary>
public interface IIntegrationsConfigProvider
{
    /// <summary>
    /// Obtiene la configuración de integraciones para el negocio dado.
    /// Retorna null si no existe o el JSON es inválido.
    /// </summary>
    Task<IntegrationsConfiguration?> GetAsync(Guid businessId, CancellationToken cancellationToken = default);
}
