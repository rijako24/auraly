using System.Security.Claims;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;

namespace Auraly.Api;

public static class WorkSessionApi
{
    public static IEndpointRouteBuilder MapWorkSessionApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/work-sessions")
            .RequireAuthorization("pos.user");

        group.MapGet("/current", async (
            HttpContext context,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var current = await service.CurrentAsync(
                    context.User.ToWorkSessionIdentity(), cancellationToken);
                return current is null ? Results.NoContent() : Results.Ok(current);
            }));

        group.MapPost("/current", async (
            HttpContext context,
            OpenWorkSessionRequest request,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.OpenOrResumeAsync(
                context.User.ToWorkSessionIdentity(), request, cancellationToken))));

        group.MapPost("/{workSessionId:guid}/close", async (
            HttpContext context,
            Guid workSessionId,
            CloseWorkSessionRequest request,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.CloseAsync(
                context.User.ToWorkSessionIdentity(),
                workSessionId,
                context.Request.Headers["Idempotency-Key"].ToString(),
                request,
                cancellationToken))));

        group.MapGet("/{workSessionId:guid}/closure", async (
            HttpContext context,
            Guid workSessionId,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var closure = await service.GetClosureAsync(
                    context.User.ToWorkSessionIdentity(),
                    workSessionId,
                    cancellationToken);
                return closure is null ? Results.NotFound() : Results.Ok(closure);
            }));

        return endpoints;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (WorkSessionForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (WorkSessionValidationException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (WorkSessionNotFoundException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (WorkSessionConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "WorkSessionConflict");
        }
    }
}

public static class WorkSessionClaimsPrincipalExtensions
{
    public static WorkSessionIdentity ToWorkSessionIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(
        ClaimsPrincipal principal,
        string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new WorkSessionForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
