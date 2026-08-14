using Auraly.Platform.Application.Configuration;

namespace Auraly.Platform.Application.Services;

/// <summary>
/// Fuente Ãºnica de verdad para complementos (add-ons) compatibles con un servicio.
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
    string? Remediation,
    string? ErrorCode = null)
{
    public static AddOnValidationResult Ok(string? normalizedCsv) =>
        new(true, normalizedCsv, null, null);

    public static AddOnValidationResult Fail(string message, string? remediation = null, string? errorCode = null) =>
        new(false, null, message, remediation, errorCode);
}
