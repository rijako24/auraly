namespace Auraly.Contracts.Fiscal;

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
    bool IsReadyForEnrollment);

public sealed record FiscalDeviceSeriesAssignment(
    Guid DeviceId,
    string DeviceName,
    bool DeviceIsActive,
    DateTimeOffset? LastSeenAt,
    Guid BusinessId,
    string BusinessName,
    Guid? SeriesId,
    string? Prefix,
    long? RangeStart,
    long? RangeEnd,
    bool IsProvisioned);

public sealed record FiscalDeviceSeriesWorkspace(
    Guid BusinessId,
    long AvailableConsecutives,
    IReadOnlyList<FiscalDeviceSeriesAssignment> Devices);

public sealed record AssignFiscalDeviceSeriesRequest(Guid DeviceId);

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
    string AllocationState = "Active",
    long? AuthorizationRangeStart = null,
    long? AuthorizationRangeEnd = null);

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
