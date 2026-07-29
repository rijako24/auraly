namespace Auraly.Contracts.Fiscal;

public sealed record FiscalDocumentView(
    Guid DocumentId,
    Guid BusinessId,
    string AuralyNumber,
    string DianNumber,
    string Cufe,
    string Status,
    Guid RegisterId,
    DateTimeOffset IssuedAt,
    int AttemptCount,
    string? TrackId,
    string? LastStatusCode,
    string? LastStatusDescription,
    DateTimeOffset UpdatedAt);

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
    string? Cufe,
    Guid? RegisterId,
    DateTimeOffset? IssuedFrom,
    DateTimeOffset? IssuedTo);