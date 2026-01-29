using MimosBabySpa.Application.FlowEngine;

namespace MimosBabySpa.Application.Configuration;

/// <summary>
/// Proveedor de configuración específica del negocio.
/// 
/// Este componente es CRÍTICO para la arquitectura domain-agnostic.
/// Proporciona toda la configuración específica del negocio sin hardcodear en el código.
/// 
/// PRINCIPIOS:
/// - Configuración cargada dinámicamente (desde BD, configuración, o prompts)
/// - Sin hardcoding de campos específicos de negocio
/// - Extensible para cualquier tipo de negocio
/// - Versionado para evolución de configuración
/// </summary>
public interface IBusinessConfigurationProvider
{
    /// <summary>
    /// Obtiene la configuración de campos requeridos para un negocio
    /// </summary>
    Task<RequiredFieldsConfiguration> GetRequiredFieldsAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el prompt del sistema para un negocio específico
    /// Este prompt contiene toda la información específica del negocio:
    /// - Catálogo de servicios
    /// - Campos requeridos
    /// - Tono y personalidad
    /// - Reglas de negocio
    /// </summary>
    Task<string> GetSystemPromptAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene el catálogo de servicios disponibles
    /// </summary>
    Task<List<ServiceInfo>> GetServicesAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene información del negocio (nombre, descripción, horarios, etc.)
    /// </summary>
    Task<BusinessInfo> GetBusinessInfoAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Obtiene configuración de atributos dinámicos específicos del negocio
    /// </summary>
    Task<Dictionary<string, AttributeDefinition>> GetBusinessAttributesAsync(
        Guid businessId,
        CancellationToken cancellationToken = default);
}

