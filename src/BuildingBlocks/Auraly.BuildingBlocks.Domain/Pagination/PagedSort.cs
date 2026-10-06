namespace Auraly.BuildingBlocks.Domain.Pagination;

public static class PagedSort
{
    public static bool IsValid(string? field, string? direction, params string[] allowedFields) =>
        (field is null || allowedFields.Contains(field, StringComparer.Ordinal)) &&
        (direction is null or "asc" or "desc") &&
        (field is not null || direction is null);
}
