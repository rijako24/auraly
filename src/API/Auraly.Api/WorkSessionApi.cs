using System.Security.Claims;
using Auraly.Application.Authorization;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.WorkSessions;
using Auraly.BuildingBlocks.Application.Synchronization;

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
            PosApprovalService approvals,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var identity = context.User.ToWorkSessionIdentity();
                var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
                if (identity.Permissions.Contains(WorkSessionPermissionCodes.Close))
                    return Results.Ok(await service.CloseAsync(
                        identity, workSessionId, idempotencyKey, request, cancellationToken));

                if (!Guid.TryParse(idempotencyKey, out var operationId) || operationId == Guid.Empty)
                    throw new WorkSessionValidationException(
                        "El cierre requiere un identificador de operación válido.");
                if (!Guid.TryParse(context.Request.Headers["X-Auraly-Draft-Id"].ToString(), out var draftId) ||
                    draftId == Guid.Empty)
                    throw new WorkSessionValidationException(
                        "No fue posible identificar la venta activa para solicitar autorización.");
                var approvalId = Guid.TryParse(
                    context.Request.Headers["X-Auraly-Approval-Id"].ToString(),
                    out var parsedApprovalId)
                    ? parsedApprovalId
                    : Guid.Empty;
                var approvalIdentity = context.User.ToPosApprovalIdentity();
                var authorizedIdentity = identity with
                {
                    Permissions = identity.Permissions
                        .Append(WorkSessionPermissionCodes.Close)
                        .ToHashSet(StringComparer.Ordinal)
                };
                var closure = await approvals.ExecuteSensitiveAsync(
                    approvalIdentity,
                    approvalId,
                    approvalIdentity.BusinessId,
                    draftId,
                    null,
                    WorkSessionPermissionCodes.Close,
                    operationId,
                    () => service.CloseAsync(
                        authorizedIdentity, workSessionId, idempotencyKey, request,
                        cancellationToken),
                    cancellationToken);
                return Results.Ok(closure);
            }));

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
            PosApprovalService approvals,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var identity = context.User.ToWorkSessionIdentity();
                if (!identity.Permissions.Contains(WorkSessionPermissionCodes.Close))
                {
                    if (!Guid.TryParse(
                            context.Request.Headers["X-Auraly-Draft-Id"].ToString(),
                            out var draftId) || draftId == Guid.Empty)
                        throw new WorkSessionValidationException(
                            "No fue posible identificar la venta activa para solicitar autorización.");
                    var approvalId = Guid.TryParse(
                        context.Request.Headers["X-Auraly-Approval-Id"].ToString(),
                        out var parsedApprovalId)
                        ? parsedApprovalId
                        : Guid.Empty;
                    var approvalIdentity = context.User.ToPosApprovalIdentity();
                    await approvals.ValidateEntryAsync(
                        approvalIdentity,
                        approvalId,
                        approvalIdentity.BusinessId,
                        draftId,
                        null,
                        WorkSessionPermissionCodes.Close,
                        cancellationToken);
                }
                return Results.Ok(await service.PreviewClosureAsync(
                    identity, workSessionId, cancellationToken));
            }));

        group.MapGet("/cash-differences", async (
            HttpContext context,
            DateOnly from,
            DateOnly to,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.ListCashDifferencesAsync(
                context.User.ToWorkSessionIdentity(), from, to, cancellationToken))));

        group.MapGet("/closures", async (HttpContext context, DateOnly from, DateOnly to,
            string? status, int? page, int? pageSize, WorkSessionService service, CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.ListClosuresAsync(
                context.User.ToWorkSessionIdentity(),from,to,status,page ?? 1,pageSize ?? 50,cancellationToken))));

        group.MapGet("/closures/{closureId:guid}/payment-verifications", async (
            HttpContext context, Guid closureId, WorkSessionService service, CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.ListClosurePaymentVerificationsAsync(
                context.User.ToWorkSessionIdentity(), closureId, cancellationToken))));

        group.MapPost("/closures/{closureId:guid}/reconcile", async (HttpContext context, Guid closureId,
            ReconcileWorkSessionClosureRequest request, WorkSessionService service, CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(await service.ReconcileClosureAsync(
                context.User.ToWorkSessionIdentity(),closureId,
                context.Request.Headers["Idempotency-Key"].ToString(),request,cancellationToken))));

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
            IPosSynchronizationOutboxDispatcher synchronization,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                if (request.ReasonId != reasonId)
                    throw new WorkSessionValidationException(
                        "The route and request reason identifiers differ.");
                var identity = context.User.ToWorkSessionIdentity();
                var saved = await service.UpsertCashReasonAsync(
                    identity, request, cancellationToken);
                await synchronization.DispatchPendingAsync(
                    identity.TenantId, request.BusinessId, CancellationToken.None);
                return Results.Ok(saved);
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

        var deviceCatalogGroup = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.synchronization");
        deviceCatalogGroup.MapGet("/cash-movement-reasons", async (
            HttpContext context,
            Guid businessId,
            string? direction,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(
                await service.ListCashReasonsFromDeviceAsync(
                    context.User.ToDeviceWorkSessionIdentity(),
                    businessId,
                    direction,
                    cancellationToken))));
        var deviceCashGroup = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.cash.manage");
        deviceCashGroup.MapPost("/cash-movements", async (
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
                var acceptance = await service.ConfirmCashMovementFromDeviceAsync(
                    identity,
                    context.Request.Headers["Idempotency-Key"].ToString(),
                    request.Movement,
                    cancellationToken);
                return Results.Accepted(
                    $"/api/commerce/v1/work-sessions/{request.Movement.WorkSessionId:D}/cash-movements/{acceptance.DocumentId:D}",
                    acceptance);
            }));

        var deviceOpenGroup = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.sales.upload");
        deviceOpenGroup.MapPost("/work-sessions/current", async (
            HttpContext context,
            DeviceOpenWorkSessionRequest request,
            WorkSessionService service,
            CancellationToken cancellationToken) =>
            await Handle(async () => Results.Ok(
                await service.OpenOrResumeDeviceAsync(
                    context.User.ToDeviceWorkSessionIdentity() with
                    {
                        UserId = request.UserId
                    },
                    request,
                    context.User.ToDeviceId(),
                    cancellationToken))));

        var deviceCloseGroup = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.sales.upload");
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
                return Results.Ok(await service.CloseFromDeviceAsync(
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
                return Results.Ok(await service.PreviewClosureFromDeviceAsync(
                    identity,
                    workSessionId,
                    cancellationToken));
            }));
        deviceCloseGroup.MapGet("/work-sessions/{workSessionId:guid}/closure", async (
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
                var closure = await service.GetClosureFromDeviceAsync(
                    identity,
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
        catch (PosApprovalException exception)
        {
            var statusCode = exception.Code is "Forbidden" or "SelfApprovalForbidden"
                ? StatusCodes.Status403Forbidden
                : exception.Code is "InvalidApproval" or "AlreadyDecidedOrExpired"
                    ? StatusCodes.Status409Conflict
                    : exception.Code == "ApprovalRequired"
                        ? StatusCodes.Status428PreconditionRequired
                        : StatusCodes.Status400BadRequest;
            return Results.Problem(
                exception.Message, statusCode: statusCode, title: exception.Code);
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

    public static Guid ToDeviceId(this ClaimsPrincipal principal) =>
        RequiredGuid(principal, PosAuthenticationDefaults.DeviceIdClaim);


    private static Guid RequiredGuid(
        ClaimsPrincipal principal,
        string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new WorkSessionForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
