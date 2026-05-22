using MimosBabySpa.Application.Configuration;

namespace MimosBabySpa.Application.Services;

/// <summary>
/// Fuente única de verdad para complementos (add-ons) compatibles con un servicio.
/// </summary>
public interface IAddOnCatalogService
{
    Task<IReadOnlyList<AddOnRuleInfo>> GetCompatibleAsync(
        Guid businessId,
        string serviceName,
        CancellationToken ct = default);

    Task<AddOnValidationResult> ValidateAsync(
        Guid businessId,
        string serviceName,
        string? addOnsCsv,
        CancellationToken ct = default);
}

public sealed record AddOnValidationResult(
    bool IsValid,
    string? NormalizedCsv,
    string? ErrorMessage,
    string? Hint)
{
    public static AddOnValidationResult Ok(string? normalizedCsv) =>
        new(true, normalizedCsv, null, null);

    public static AddOnValidationResult Fail(string message, string? hint = null) =>
        new(false, null, message, hint);
}
