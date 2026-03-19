namespace MimosBabySpa.Application.Services;

/// <summary>
/// Genera dinámicamente el contenido del catálogo de servicios a partir de las tablas de base de datos.
/// Permite que el flow engine use contenido siempre fresco sin depender de texto hardcodeado en KnowledgeSources.
/// </summary>
public interface ICatalogContentGenerator
{
    /// <summary>
    /// Genera el markdown completo del catálogo de servicios para el negocio indicado.
    /// Usa ServiceCatalogBuilder internamente con datos cargados desde DB.
    /// </summary>
    Task<string> GenerateAsync(Guid businessId, CancellationToken ct = default);
}
