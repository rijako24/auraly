namespace MimosBabySpa.Application.Common.DTOs;

public record PagedRequest(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    string? SortBy = null,
    bool SortDescending = false);
