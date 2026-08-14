namespace Auraly.Platform.Application.Identity.DTOs;

public record UpdateTenantRequest(string? Name, string? Email, int? MaximumUsers, int? MaximumEnrolledDevices);
