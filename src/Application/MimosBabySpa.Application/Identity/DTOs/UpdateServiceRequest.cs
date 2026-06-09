using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

public record UpdateServiceRequest(
    string? ServiceName,
    string? Description,
    int? DurationMinutes,
    decimal? Price,
    bool? IsActive,
    Guid? CategoryId,
    ServiceTier? Tier,
    ServiceType? ServiceType,
    ServiceFulfillmentKind? FulfillmentKind,
    string? FixedScheduleLabel);
