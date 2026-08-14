namespace Auraly.Platform.Application.Configuration;

/// <summary>
/// Proveedor de configuraciÃ³n de integraciones por negocio (Google Calendar, Wompi, etc.).
/// Fuente Ãºnica: IntegrationConnections.
/// Reemplaza IOptions&lt;CalendarSettings&gt; y IOptions&lt;WompiSettings&gt; para config multi-tenant.
/// </summary>
public interface IIntegrationsConfigProvider
{
    /// <summary>
    /// Obtiene la configuraciÃ³n de integraciones para el negocio dado.
    /// Retorna null si no existe o el JSON es invÃ¡lido.
    /// </summary>
    Task<IntegrationsConfiguration?> GetAsync(Guid businessId, CancellationToken cancellationToken = default);
}

