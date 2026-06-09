using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.DTOs;

public record CreateServiceRequest(
    Guid BusinessId,
    string ServiceName,
    string? Description,
    int DurationMinutes,
    decimal Price,
    Guid CategoryId,
    ServiceTier Tier = ServiceTier.Base,
    ServiceType ServiceType = ServiceType.Standard,
    ServiceFulfillmentKind FulfillmentKind = ServiceFulfillmentKind.Reservation,
    string? FixedScheduleLabel = null);
