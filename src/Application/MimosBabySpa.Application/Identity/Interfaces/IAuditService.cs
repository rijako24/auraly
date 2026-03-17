using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IAuditService
{
    Task LogAsync(string action, string entityType, string? entityId, object? oldValues, object? newValues, CancellationToken ct = default);
    Task<PagedResponse<AuditLogDto>> GetPagedAsync(Guid? tenantId, DateTime? from, DateTime? to, string? entityType, string? correlationId, PagedRequest request, CancellationToken ct = default);
}
