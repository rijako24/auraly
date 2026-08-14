using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Auraly.Platform.Application.Auth.DTOs;
using Auraly.Platform.Application.Auth.Interfaces;
using Auraly.Api.Extensions;

namespace Auraly.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        await authService.ChangePasswordAsync(
            User.GetUserId(), request, cancellationToken);
        return NoContent();
    }
}
