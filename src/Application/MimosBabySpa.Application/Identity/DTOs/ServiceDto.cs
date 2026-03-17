using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

public record ServiceDto(
    Guid ServiceId,
    Guid BusinessId,
    string ServiceName,
    string Description,
    int DurationMinutes,
    decimal Price,
    bool IsActive,
    Guid CategoryId,
    string CategoryName,
    ServiceTier Tier,
    ServiceType ServiceType,
    DateTime CreatedAt);
