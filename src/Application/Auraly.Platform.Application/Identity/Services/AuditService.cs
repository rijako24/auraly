using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public sealed class AuditService : IAuditService
{
    private readonly IUnitOfWork _unitOfWork;

    public AuditService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResponse<AuditLogDto>> GetPagedAsync(
        Guid? tenantId, DateTime? from, DateTime? to, string? entityType,
        string? correlationId, PagedRequest request, CancellationToken ct)
    {
        var (items, totalCount) = await _unitOfWork.AuditLogs.GetPagedAsync(
            tenantId, from, to, entityType, correlationId,
            request.Page, request.PageSize, ct);

        return new PagedResponse<AuditLogDto>(
            items.Select(a => new AuditLogDto(
                a.AuditLogId, a.UserId, a.User?.FullName, a.Action,
                a.EntityType, a.EntityId, a.OldValues, a.NewValues,
                a.IpAddress, a.CorrelationId, a.Timestamp)).ToList(),
            totalCount, request.Page, request.PageSize);
    }
}