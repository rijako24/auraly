namespace MimosBabySpa.Application.Services;

/// <summary>
/// Genera dinámicamente el contenido del catálogo de servicios a partir de las tablas de base de datos.
/// </summary>
public interface ICatalogContentGenerator
{
    Task<string> GenerateAsync(Guid businessId, CancellationToken ct = default);

    Task<string> GenerateAsync(Guid businessId, string? query, CancellationToken ct = default);

    Task<string> GenerateAsync(
        Guid businessId,
        string? query,
        CatalogContentView view,
        CancellationToken ct = default);
}

public enum CatalogContentView
{
    Services,
    Categories
}
