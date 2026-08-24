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

        group.MapGet("/{workSessionId:guid}/closure-preview", async (
            HttpContext context,
            Guid workSessionId,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.PreviewClosureAsync(
                context.User.ToWorkSessionIdentity(), workSessionId, cancellationToken))));

        group.MapGet("/cash-reasons", async (
            HttpContext context,
            Guid businessId,
            string? direction,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(
                await service.ListCashReasonsAsync(
                    context.User.ToWorkSessionIdentity(),
                    businessId,
                    direction,
                    cancellationToken))));

        group.MapPut("/cash-reasons/{reasonId:guid}", async (
            HttpContext context,
            Guid reasonId,
            UpsertCashMovementReasonRequest request,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                if (request.ReasonId != reasonId)
                    throw new WorkSessionValidationException(
                        "The route and request reason identifiers differ.");
                return Results.Ok(await service.UpsertCashReasonAsync(
                    context.User.ToWorkSessionIdentity(),
                    request,
                    cancellationToken));
            }));

        group.MapPost("/{workSessionId:guid}/cash-movements", async (
            HttpContext context,
            Guid workSessionId,
            ConfirmCashMovementRequest request,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                if (request.WorkSessionId != workSessionId)
                    throw new WorkSessionValidationException(
                        "The route and request work-session identifiers differ.");
                var acceptance = await service.ConfirmCashMovementAsync(
                    context.User.ToWorkSessionIdentity(),
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    request,
                    cancellationToken);
                return Results.Accepted(
                    $"/api/commerce/v1/work-sessions/{workSessionId:D}/cash-movements/{acceptance.DocumentId:D}",
                    acceptance);
            }));

        var deviceGroup = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.cash.manage");
        deviceGroup.MapGet("/cash-movement-reasons", async (
            HttpContext context,
            Guid businessId,
            string? direction,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(
                await service.ListCashReasonsAsync(
                    context.User.ToDeviceWorkSessionIdentity(),
                    businessId,
                    direction,
                    cancellationToken))));
        deviceGroup.MapPost("/cash-movements", async (
            HttpContext context,
            DeviceCashMovementRequest request,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                if (request.UserId == Guid.Empty)
                    throw new WorkSessionValidationException(
                        "The local cashier is required.");
                var deviceIdentity = context.User.ToDeviceWorkSessionIdentity();
                var identity = deviceIdentity with { UserId = request.UserId };
                var acceptance = await service.ConfirmCashMovementAsync(
                    identity,
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    request.Movement,
                    cancellationToken);
                return Results.Accepted(
                    $"/api/commerce/v1/work-sessions/{request.Movement.WorkSessionId:D}/cash-movements/{acceptance.DocumentId:D}",
                    acceptance);
            }));

        var deviceCloseGroup = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.work-session.close");
        deviceCloseGroup.MapPost("/work-sessions/{workSessionId:guid}/close", async (
            HttpContext context,
            Guid workSessionId,
            DeviceCloseWorkSessionRequest request,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                if (request.UserId == Guid.Empty || request.WorkSessionId != workSessionId)
                    throw new WorkSessionValidationException(
                        "The local supervisor and work session are required.");
                var identity = context.User.ToDeviceWorkSessionIdentity() with
                {
                    UserId = request.UserId
                };
                return Results.Ok(await service.CloseAsync(
                    identity,
                    workSessionId,
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    new CloseWorkSessionRequest(
                        request.CountedCash,
                        request.Note,
                        request.AuthorizedByUserId,
                        request.PaymentCounts),
                    cancellationToken));
            }));
        deviceCloseGroup.MapGet("/work-sessions/{workSessionId:guid}/closure-preview", async (
            HttpContext context,
            Guid workSessionId,
            Guid userId,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                if (userId == Guid.Empty)
                    throw new WorkSessionValidationException(
                        "The local cashier is required.");
                var identity = context.User.ToDeviceWorkSessionIdentity() with
                {
                    UserId = userId
                };
                return Results.Ok(await service.PreviewClosureAsync(
                    identity,
                    workSessionId,
                    cancellationToken));
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
    public static WorkSessionIdentity ToDeviceWorkSessionIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
            principal.FindAll(PosAuthenticationDefaults.PermissionClaim)
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
