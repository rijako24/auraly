namespace MimosBabySpa.Application.Services;

/// <summary>
/// Servicio para obtener configuración de recursos del negocio.
/// Permite que el backend interprete reglas de coexistencia y recursos.
/// </summary>
public interface IResourceConfigurationService
{
    /// <summary>
    /// Obtiene el modelo de recursos para un negocio
    /// </summary>
    Task<Models.ResourceModel> GetResourceModelAsync(Guid businessId);
}
