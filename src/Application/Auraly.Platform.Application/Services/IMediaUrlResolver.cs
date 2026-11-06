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
    Task<string> ResolveTenantAsync(Guid tenantId, string mediaRef, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, string>> ResolveTenantBatchAsync(
        Guid tenantId, IReadOnlyCollection<string> mediaRefs, CancellationToken ct = default);
}
