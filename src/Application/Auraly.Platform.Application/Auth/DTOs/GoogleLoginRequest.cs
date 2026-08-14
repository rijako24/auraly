namespace Auraly.Platform.Application.Auth.DTOs;

public record GoogleLoginRequest(string IdToken, Guid? TenantId = null);
