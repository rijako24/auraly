namespace Auraly.Contracts.Fiscal;

public static class DianFiscalDefaults
{
    public const string HabilitationAuthorizationNumber = "18760000001";
    public const string HabilitationPrefix = "SETP";
    public const long HabilitationRangeStart = 990000000;
    public const long HabilitationRangeEnd = 995000000;
    public const string HabilitationTechnicalKey =
        "fc8eac422eba16e22ffd8c6f94b3f40a6e38162c";
    public const string HabilitationTechnicalKeyVersion =
        "dian-habilitation-standard-v1";
    public const string HabilitationQrValidationUrl =
        "https://catalogo-vpfe-hab.dian.gov.co/document/searchqr";
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
    IReadOnlyList<string>? WarningMessages = null,
    int DianDocumentMonthlyLimit = 0,
    int DianDocumentsUsed = 0,
    bool HasDianDocumentQuota = false);

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

public sealed record UnassignFiscalDeviceSeriesRequest(Guid DeviceId);

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
    long RemainingNumberWarningThreshold = 100,
    bool ProductionActive = false);

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
