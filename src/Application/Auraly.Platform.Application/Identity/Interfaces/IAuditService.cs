using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IAuditService
{
    Task<PagedResponse<AuditLogDto>> GetPagedAsync(
        Guid? tenantId,
        DateTime? from,
        DateTime? to,
        string? entityType,
        string? correlationId,
        PagedRequest request,
        CancellationToken ct = default);
}