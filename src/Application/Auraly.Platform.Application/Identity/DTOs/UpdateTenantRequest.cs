namespace Auraly.Platform.Application.Identity.DTOs;

public record UpdateTenantRequest(string? Name, string? Email, int? MaximumUsers, int? MaximumEnrolledDevices,
    string? LegalName = null, string? Nit = null, string? VerificationDigit = null,
    string? EntityType = null, string? IdentificationTypeCode = null,
    string? InventoryCostBasis = null,
    bool? AllowPromotionChannelCombination = null);
