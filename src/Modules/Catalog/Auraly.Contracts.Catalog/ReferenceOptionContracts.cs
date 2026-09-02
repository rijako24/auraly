namespace Auraly.Contracts.Catalog;

public sealed record ReferenceOption(
    Guid Id,
    string Code,
    string Label,
    string? Description,
    int SortOrder);

public sealed record CreateReferenceOptionRequest(
    string Code,
    string Label,
    string? Description = null);
