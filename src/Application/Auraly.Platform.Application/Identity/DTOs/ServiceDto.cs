using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.DTOs;

public record ServiceDto(
    Guid ServiceId,
    Guid BusinessId,
    string ServiceName,
    string Description,
    string? Keywords,
    int DurationMinutes,
    decimal Price,
    bool IncludeInCheckoutTotal,
    bool IsActive,
    Guid? CategoryId,
    string CategoryName,
    ServiceTier Tier,
    ServiceType ServiceType,
    ServiceFulfillmentKind FulfillmentKind,
    string? FixedScheduleLabel,
    DateTime CreatedAt);
