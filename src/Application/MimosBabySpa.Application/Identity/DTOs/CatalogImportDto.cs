namespace MimosBabySpa.Application.Identity.DTOs;

public sealed class CatalogImportServiceLineDto
{
    public string ServiceName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? Keywords { get; init; }
    public int DurationMinutes { get; init; } = 60;
    /// <summary>Precio en pesos COP (entero o decimal).</summary>
    public decimal Price { get; init; }
    public bool IncludeInCheckoutTotal { get; init; } = true;
    public string CategoryName { get; init; } = "General";
    public string ServiceType { get; init; } = "Standard";
    public string Tier { get; init; } = "Base";
    public bool Selected { get; init; } = true;
}

public sealed class CatalogImportDraftDto
{
    public string? SourceFileName { get; init; }
    public string? ExtractedTextPreview { get; init; }
    public IReadOnlyList<CatalogImportServiceLineDto> Services { get; init; } = [];
}

public sealed class ConfirmCatalogImportRequest
{
    public IReadOnlyList<CatalogImportServiceLineDto> Services { get; init; } = [];
    public bool SkipExistingByName { get; init; } = true;
}

public sealed class CatalogImportResultDto
{
    public int CategoriesCreated { get; init; }
    public int ServicesCreated { get; init; }
    public int ServicesSkipped { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
