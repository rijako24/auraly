using Auraly.Application.Authentication;
using Auraly.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace MimosBabySpa.WebAPI.Authorization;

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
            if (!await validator.IsActiveAsync(
                    token, context.HttpContext.RequestAborted))
                context.Fail(
                    "The authentication session is inactive or expired.");
        }
        catch (AuthenticationDeniedException exception)
        {
            context.Fail(exception.Message);
        }
    }
}
