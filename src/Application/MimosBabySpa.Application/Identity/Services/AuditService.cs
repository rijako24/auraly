using System.Text.Json;
using System.Text.Json.Serialization;
using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Common.Interfaces;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class AuditService : IAuditService
{
    private static readonly JsonSerializerOptions AuditSerializationOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        WriteIndented = false
    };

    private readonly IUnitOfWork _unitOfWork;
    private readonly ITenantContext _tenantContext;
    private readonly ICorrelationIdProvider _correlationIdProvider;

    public AuditService(
        IUnitOfWork unitOfWork,
        ITenantContext tenantContext,
        ICorrelationIdProvider correlationIdProvider)
    {
        _unitOfWork = unitOfWork;
        _tenantContext = tenantContext;
        _correlationIdProvider = correlationIdProvider;
    }

    public async Task LogAsync(
        string action, string entityType, string? entityId,
        object? oldValues, object? newValues, CancellationToken ct)
    {
        await _unitOfWork.AuditLogs.AddAsync(new AuditLog
        {
            AuditLogId = Guid.NewGuid(),
            UserId = _tenantContext.UserId,
            TenantId = _tenantContext.TenantId,
            BusinessId = _tenantContext.BusinessId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            OldValues = oldValues is not null ? JsonSerializer.Serialize(oldValues, AuditSerializationOptions) : null,
            NewValues = newValues is not null ? JsonSerializer.Serialize(newValues, AuditSerializationOptions) : null,
            CorrelationId = _correlationIdProvider.CorrelationId,
            Timestamp = DateTime.UtcNow
        }, ct);

        await _unitOfWork.SaveChangesAsync(ct);
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
