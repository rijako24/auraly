namespace MimosBabySpa.Application.Services;

/// <summary>
/// Resuelve una referencia de media a una URL descargable por WhatsApp.
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
}
