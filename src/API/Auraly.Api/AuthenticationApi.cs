using Auraly.Application.Authentication;
using Auraly.Application.Tenants;
using Auraly.Contracts.Tenants;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.Authentication;
using Auraly.Infrastructure.Persistence;

namespace Auraly.Api;

public static class AuthenticationApi
{
    public static IEndpointRouteBuilder MapAuthenticationApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth");

        group.MapPost("/invitations/accept", async (
            AcceptTenantInvitationRequest request,
            TenantInvitationService service,
            CancellationToken cancellationToken) =>
            await HandleInvitation(async () => Results.Ok(
                await service.AcceptAsync(request, cancellationToken))));

        group.MapPost("/login", async (
            HttpContext context,
            AuthenticationLoginRequest request,
            AuthenticationService service,
            IAuthenticationTokenIssuer tokenIssuer,
            WorkSessionService workSessions,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var response = await service.LoginAsync(
                    request,
                    RequiredClientId(context),
                    context.Request.Headers.UserAgent.ToString(),
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.TraceIdentifier,
                    cancellationToken);
                var token = tokenIssuer.ParseExpiredAccessToken(response.AccessToken);
                await workSessions.CloseForLoginAsync(
                    token.UserId,
                    token.TenantId,
                    token.AuthenticationSessionId,
                    cancellationToken);
                return Results.Ok(response);
            }));

        group.MapPost("/refresh", async (
            HttpContext context,
            AuthenticationRefreshRequest request,
            AuthenticationService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.RefreshAsync(
                request,
                RequiredClientId(context),
                context.TraceIdentifier,
                cancellationToken))));

        group.MapPost("/revoke", async (
            HttpContext context,
            AuthenticationRevokeRequest request,
            AuthenticationService service,
            WorkSessionService workSessions,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var token = JwtAuthenticationTokenIssuer.Parse(context.User);
                var identity = new AuthenticationSessionIdentity(
                    token.AuthenticationSessionId,
                    token.UserId,
                    token.TenantId,
                    RequiredClientId(context));
                await workSessions.CloseForLogoutAsync(
                    token.UserId,
                    token.TenantId,
                    token.AuthenticationSessionId,
                    cancellationToken);
                await service.RevokeAsync(
                    identity,
                    request.RefreshToken,
                    "UserLogout",
                    cancellationToken);
                return Results.NoContent();
            })).RequireAuthorization("authentication.user");

        group.MapGet("/me", async (
            HttpContext context,
            AuthenticationService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(
                await service.GetCurrentUserAsync(
                    JwtAuthenticationTokenIssuer.Parse(context.User),
                    cancellationToken))))
            .RequireAuthorization("authentication.user");

        return endpoints;
    }

    private static Guid RequiredClientId(HttpContext context) =>
        Guid.TryParse(
            context.Request.Headers[AuthenticationDefaults.ClientIdHeader].ToString(),
            out var clientId)
            ? clientId
            : throw new AuthenticationValidationException(
                $"Header '{AuthenticationDefaults.ClientIdHeader}' is required.");

    private static async Task<IResult> HandleInvitation(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (TenantInvitationException exception)
        {
            return Results.Problem(exception.Message, statusCode: 400, title: exception.Code);
        }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is >= 51031 and <= 51034)
        {
            return Results.Problem(exception.Message, statusCode: exception.Number == 51031 ? 404 : 409, title: "InvitationUnavailable");
        }
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (AuthenticationValidationException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status400BadRequest,
                title: "AuthenticationValidationFailed");
        }
        catch (AuthenticationDeniedException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status401Unauthorized,
                title: "AuthenticationDenied");
        }
        catch (AuthenticationSessionConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "AuthenticationSessionAlreadyActive");
        }
    }
}
