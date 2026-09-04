using Auraly.Application.Authentication;
using Auraly.Application.Tenants;
using Auraly.Contracts.Tenants;
using Auraly.Contracts.Authentication;
using Auraly.Infrastructure.Persistence;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Services;

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

        group.MapPost("/password-recovery/request", async (
            RequestPasswordRecoveryRequest request,
            PasswordRecoveryService service,
            CancellationToken cancellationToken) =>
            await HandlePasswordRecovery(async () => Results.Accepted(
                value: await service.RequestAsync(request, cancellationToken))));

        group.MapPost("/password-recovery/confirm", async (
            ConfirmPasswordRecoveryRequest request,
            PasswordRecoveryService service,
            CancellationToken cancellationToken) =>
            await HandlePasswordRecovery(async () =>
            {
                await service.ConfirmAsync(request, cancellationToken);
                return Results.NoContent();
            }));
        group.MapPost("/login", async (
            HttpContext context,
            AuthenticationLoginRequest request,
            AuthenticationService service,
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
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var token = JwtAuthenticationTokenIssuer.Parse(context.User);
                var identity = new AuthenticationSessionIdentity(
                    token.AuthenticationSessionId,
                    token.UserId,
                    token.TenantId,
                    RequiredClientId(context));
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

    private static async Task<IResult> HandlePasswordRecovery(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PasswordRecoveryException exception)
        {
            return Results.Problem(exception.Message, statusCode: 400, title: exception.Code);
        }
    }
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
            return exception.Number switch
            {
                51031 => Results.Problem(exception.Message, statusCode: 404,
                    title: "InvitationNotFound"),
                51033 => Results.Problem(exception.Message, statusCode: 410,
                    title: "InvitationExpired"),
                _ => Results.Problem(exception.Message, statusCode: 409,
                    title: "InvitationUnavailable")
            };
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
        catch (AuthenticationSessionReplacedException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "AuthenticationSessionReplaced");
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
