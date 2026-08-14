namespace Auraly.Platform.Application.Identity.DTOs;

public record AuditLogDto(
    Guid AuditLogId,
    Guid? UserId,
    string? UserFullName,
    string Action,
    string EntityType,
    string? EntityId,
    string? OldValues,
    string? NewValues,
    string? IpAddress,
    string? CorrelationId,
    DateTime Timestamp);
