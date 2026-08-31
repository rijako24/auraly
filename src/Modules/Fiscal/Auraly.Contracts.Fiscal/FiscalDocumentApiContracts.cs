namespace Auraly.Contracts.Fiscal;

public sealed record FiscalDocumentView(
    Guid DocumentId,
    Guid BusinessId,
    string SourceDocumentType,
    string FiscalDocumentType,
    string AuralyNumber,
    string DianNumber,
    string UniqueCodeType,
    string? UniqueCode,
    string Status,
    Guid? DeviceId,
    DateTimeOffset IssuedAt,
    int AttemptCount,
    string? TrackId,
    string? LastStatusCode,
    string? LastStatusDescription,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? QuotaBlockedAt);

public sealed record FiscalDocumentPage(
    IReadOnlyList<FiscalDocumentView> Items,
    int Page,
    int PageSize,
    long TotalCount);

public sealed record FiscalDocumentQuery(
    int Page,
    int PageSize,
    string? Status,
    string? AuralyNumber,
    string? DianNumber,
    string? UniqueCode,
    Guid? DeviceId,
    DateTimeOffset? IssuedFrom,
    DateTimeOffset? IssuedTo,
    bool QuotaOnly);

public sealed record PosFiscalStatusChange(
    Guid DocumentId,
    string FiscalNumber,
    string Cufe,
    string Status,
    string? StatusCode,
    string? StatusDescription,
    DateTimeOffset UpdatedAt);

public sealed record PosFiscalStatusPage(
    IReadOnlyList<PosFiscalStatusChange> Items,
    string NextCursor,
    bool HasMore);

public sealed record PosFiscalDeviceContext(
    Guid DeviceId,
    Guid BusinessId,
    IReadOnlySet<string> Permissions);
