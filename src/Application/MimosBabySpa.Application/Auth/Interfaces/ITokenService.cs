using System.Security.Claims;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Application.Auth.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(AppUser user, IReadOnlyList<string> roles, IReadOnlyList<string> permissions, Guid activeTenantId);
    string GenerateRefreshToken();
    ClaimsPrincipal? ValidateExpiredToken(string token);
}
