using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.DTOs;

public record CreateServiceRequest(
    Guid BusinessId,
    string ServiceName,
    string? Description,
    string? Keywords,
    int DurationMinutes,
    decimal Price,
    Guid? CategoryId,
    ServiceTier Tier = ServiceTier.Base,
    ServiceType ServiceType = ServiceType.Standard,
    ServiceFulfillmentKind FulfillmentKind = ServiceFulfillmentKind.Reservation,
    string? FixedScheduleLabel = null,
    bool IncludeInCheckoutTotal = true);
