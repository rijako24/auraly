namespace Auraly.Contracts.Fiscal;

public static class DianFiscalDefaults
{
    public const string ProductionQrValidationUrl =
        "https://catalogo-vpfe.dian.gov.co/document/searchqr";
    public const string NumberingRangeTechnicalKeyVersion = "dian-get-numbering-range";
}

public sealed record FiscalResolutionConfiguration(
    Guid BusinessId,
    Guid? FiscalAuthorizationId,
    string? AuthorizationNumber,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string? Prefix,
    long? RangeStart,
    long? RangeEnd,
    bool HasActiveAuthorization,
    bool IsReadyForOnlineSales,
    bool IsReadyForEnrollment,
    long? NextConsecutive = null,
    long? RemainingConsecutives = null,
    int ExpirationWarningDays = 3,
    long RemainingNumberWarningThreshold = 100,
    IReadOnlyList<string>? WarningMessages = null);

public sealed record FiscalOnlineSeriesAssignment(
    Guid SeriesId,
    Guid FiscalAuthorizationId,
    string AuthorizationNumber,
    string Prefix,
    long RangeStart,
    long RangeEnd,
    long NextConsecutive,
    long RemainingConsecutives,
    DateOnly ValidFrom,
    DateOnly ValidUntil);

public sealed record FiscalDeviceSeriesAssignment(
    Guid DeviceId,
    string DeviceName,
    bool DeviceIsActive,
    DateTimeOffset? LastSeenAt,
    Guid BusinessId,
    string BusinessName,
    Guid? SeriesId,
    Guid? FiscalAuthorizationId,
    string? AuthorizationNumber,
    string? Prefix,
    long? RangeStart,
    long? RangeEnd,
    bool IsProvisioned);

public sealed record FiscalAssignableResolution(
    Guid DianNumberingRangeId,
    string AuthorizationNumber,
    string Prefix,
    long RangeStart,
    long RangeEnd,
    DateOnly ValidFrom,
    DateOnly ValidUntil);

public sealed record FiscalDeviceSeriesWorkspace(
    Guid BusinessId,
    long AvailableConsecutives,
    IReadOnlyList<FiscalAssignableResolution> AvailableResolutions,
    IReadOnlyList<FiscalDeviceSeriesAssignment> Devices,
    FiscalOnlineSeriesAssignment? OnlineAssignment = null,
    int ExpirationWarningDays = 3,
    long RemainingNumberWarningThreshold = 100);

public sealed record AssignFiscalDeviceSeriesRequest(
    Guid DeviceId,
    Guid DianNumberingRangeId);

public sealed record SaveFiscalResolutionAlertSettingsRequest(
    int ExpirationWarningDays,
    long RemainingNumberWarningThreshold);

public sealed record PosFiscalSeriesProvisioning(
    Guid SeriesId,
    Guid FiscalAuthorizationId,
    string Prefix,
    string AuthorizationNumber,
    long RangeStart,
    long RangeEnd,
    DateOnly ValidUntil,
    int Environment,
    string SupplierTaxId,
    string TechnicalKey,
    string TechnicalKeyVersion,
    string QrValidationUrl,
    DateOnly ValidFrom,
    long? AuthorizationRangeStart = null,
    long? AuthorizationRangeEnd = null,
    int ExpirationWarningDays = 3,
    long RemainingNumberWarningThreshold = 100);

public interface IFiscalTechnicalKeySecretWriter
{
    Task SaveAsync(
        Guid tenantId,
        Guid businessId,
        Guid fiscalAuthorizationId,
        string authorizationNumber,
        string version,
        int environment,
        string supplierTaxId,
        string qrValidationUrl,
        string technicalKey,
        CancellationToken cancellationToken);
}
