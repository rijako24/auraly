namespace Auraly.Platform.Application.Services;

/// <summary>
/// Resuelve referencias de media a URLs descargables.
/// - Rutas de blob → genera SAS URL de corta duración.
/// - URLs absolutas (https) → las retorna tal cual.
/// </summary>
public interface IMediaUrlResolver
{
    /// <summary>
    /// Resuelve mediaRef a una URL pública.
    /// Si es ruta de blob (ej. "confirmations/guia.pdf"), genera SAS.
    /// Si es URL https, la retorna sin cambios.
    /// </summary>
    Task<string> ResolveAsync(Guid businessId, string mediaRef, CancellationToken ct = default);
    /// <summary>Firma en lote las referencias del contenedor de la sede, sin consultar cada blob.
    /// Conserva referencias originales como claves y no firma URLs de otro contenedor.</summary>
    Task<IReadOnlyDictionary<string, string>> ResolveBusinessBatchAsync(
        Guid businessId, IReadOnlyCollection<string> mediaRefs, CancellationToken ct = default);
    Task<string> ResolveTenantAsync(Guid tenantId, string mediaRef, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> ResolveTenantBatchAsync(
        Guid tenantId, IReadOnlyCollection<string> mediaRefs, CancellationToken ct = default);
}
