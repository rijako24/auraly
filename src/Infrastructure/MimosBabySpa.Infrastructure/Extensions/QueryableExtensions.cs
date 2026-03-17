using Microsoft.EntityFrameworkCore;

namespace MimosBabySpa.Infrastructure.Extensions;

public static class QueryableExtensions
{
    public static async Task<(IReadOnlyList<T> Items, int TotalCount)> ToPagedListAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken ct = default)
    {
        var totalCount = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
