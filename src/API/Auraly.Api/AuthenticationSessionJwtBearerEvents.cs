using Auraly.Application.Authentication;
using Auraly.Contracts.Authentication;
using Auraly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Auraly.Api;

public sealed class AuthenticationSessionJwtBearerEvents(
    IAuthenticationSessionValidator validator) : JwtBearerEvents
{
    public override async Task TokenValidated(TokenValidatedContext context)
    {
        try
        {
            var token = JwtAuthenticationTokenIssuer.Parse(
                context.Principal ?? throw new AuthenticationDeniedException(
                    "The authenticated principal is missing."));
            if (!Guid.TryParse(
                    context.HttpContext.Request.Headers[
                        AuthenticationDefaults.ClientIdHeader].ToString(),
                    out var clientId))
            {
                context.Fail("The authentication client identifier is missing or invalid.");
                return;
            }
            if (!await validator.IsActiveAsync(
                    token, clientId, context.HttpContext.RequestAborted))
                context.Fail("The authentication session is inactive or expired.");
        }
        catch (AuthenticationDeniedException exception)
        {
            context.Fail(exception.Message);
        }
    }
}
