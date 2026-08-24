using System.Security.Claims;
using Auraly.Application.Authorization;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Auraly.Api;

public static class PosApprovalApi
{
    public static IEndpointRouteBuilder MapPosApprovalApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/approvals")
            .RequireAuthorization("authentication.user");

        group.MapPost("/", async (
            ClaimsPrincipal principal,
            CreatePosApprovalRequest request,
            PosApprovalService service,
            PosApprovalWebPushService push,
            CancellationToken cancellationToken) =>
            await Handle(async () =>
            {
                var created = await service.CreateAsync(principal.ToPosApprovalIdentity(), request, cancellationToken);
                await push.NotifyAsync(created, cancellationToken);
                return created;
            }));

        group.MapGet("/push/public-key", (PosApprovalWebPushService push) =>
            HandleSync(() => Results.Ok(new { publicKey = push.PublicKey() })));
        group.MapPut("/push/subscription", async (
            ClaimsPrincipal principal,
            PosApprovalPushSubscriptionRequest request,
            PosApprovalWebPushService push,
            CancellationToken cancellationToken) =>
            await HandleNoContent(() => push.SubscribeAsync(principal.ToPosApprovalIdentity(), request, cancellationToken)));
        group.MapDelete("/push/subscription", async (
            ClaimsPrincipal principal,
            [FromBody] PosApprovalPushSubscriptionRequest request,
            PosApprovalWebPushService push,
            CancellationToken cancellationToken) =>
            await HandleNoContent(() => push.UnsubscribeAsync(principal.ToPosApprovalIdentity(), request.Endpoint, cancellationToken)));

        group.MapGet("/{approvalRequestId:guid}", async (
            ClaimsPrincipal principal,
            Guid approvalRequestId,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
            await HandleNullable(() => service.GetAsync(
                principal.ToPosApprovalIdentity(), approvalRequestId, cancellationToken)));

        group.MapGet("/pending", async (
            ClaimsPrincipal principal,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
        {
            var identity = principal.ToPosApprovalIdentity();
            return await Handle(() => service.PendingAsync(
                identity, identity.BusinessId, cancellationToken));
        });

        group.MapPost("/{approvalRequestId:guid}/decision", async (
            ClaimsPrincipal principal,
            Guid approvalRequestId,
            DecidePosApprovalRequest request,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
            await Handle(() => service.DecideAsync(
                principal.ToPosApprovalIdentity(),
                approvalRequestId,
                request.Approve,
                cancellationToken)));

        group.MapPost("/{approvalRequestId:guid}/local-authorization", async (
            ClaimsPrincipal principal,
            Guid approvalRequestId,
            AuthorizePosApprovalLocallyRequest request,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
            await Handle(() => service.AuthorizeLocallyAsync(
                principal.ToPosApprovalIdentity(),
                approvalRequestId,
                request.Secret,
                cancellationToken)));

        group.MapPut("/supervisor-credential", async (
            ClaimsPrincipal principal,
            ConfigureSupervisorCredentialRequest request,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
        {
            await service.ConfigureCredentialAsync(
                principal.ToPosApprovalIdentity(), request.Secret, request.ValidityHours,
                request.IsOneTime, cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/supervisor-credential", async (
            ClaimsPrincipal principal,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
            await Handle(() => service.CredentialStatusAsync(
                principal.ToPosApprovalIdentity(), cancellationToken)));

        group.MapDelete("/supervisor-credential", async (
            ClaimsPrincipal principal,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
        {
            await service.RevokeCredentialAsync(
                principal.ToPosApprovalIdentity(), cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/users/{userId:guid}/supervisor-credential", async (
            ClaimsPrincipal principal, Guid userId, PosApprovalService service, CancellationToken cancellationToken) =>
            await Handle(() => service.CredentialStatusForUserAsync(principal.ToPosApprovalIdentity(), userId, cancellationToken)));
        group.MapPut("/users/{userId:guid}/supervisor-credential", async (
            ClaimsPrincipal principal, Guid userId, ConfigureSupervisorCredentialRequest request, PosApprovalService service, CancellationToken cancellationToken) =>
        { await service.ConfigureCredentialForUserAsync(principal.ToPosApprovalIdentity(), userId, request.Secret, request.ValidityHours, request.IsOneTime, cancellationToken); return Results.NoContent(); });
        group.MapDelete("/users/{userId:guid}/supervisor-credential", async (
            ClaimsPrincipal principal, Guid userId, PosApprovalService service, CancellationToken cancellationToken) =>
        { await service.RevokeCredentialForUserAsync(principal.ToPosApprovalIdentity(), userId, cancellationToken); return Results.NoContent(); });

        group.MapPost("/synchronization/negotiate", (
            ClaimsPrincipal principal,
            IPosSynchronizationPushGateway gateway,
            CancellationToken cancellationToken) =>
        {
            var identity = principal.ToPosApprovalIdentity();
            var uri = gateway.CreateUserClientAccessUri(
                identity.TenantId,
                identity.BusinessId,
                identity.UserId,
                cancellationToken);
            return Results.Ok(new PosSynchronizationNegotiationResponse(
                uri,
                DateTimeOffset.UtcNow.AddMinutes(15)));
        });

        var device = endpoints.MapGroup("/api/pos/v1/approvals")
            .RequireAuthorization("pos.approvals.consume");

        device.MapPost("/", async (
            ClaimsPrincipal principal,
            CreatePosApprovalRequest request,
            HttpContext context,
            PosApprovalService service,
            PosApprovalWebPushService push,
            CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParse(context.Request.Headers["X-Auraly-User-Id"], out var userId) ||
                !Guid.TryParse(context.Request.Headers["X-Auraly-Work-Session-Id"], out var workSessionId))
                return Results.Problem("El dispositivo no identificó el usuario y su sesión.", statusCode: 400, title: "InvalidScope");
            return await Handle(async () =>
            {
                var created = await service.CreateForDeviceAsync(
                    RequiredDeviceGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
                    RequiredDeviceGuid(principal, PosAuthenticationDefaults.DeviceIdClaim),
                    userId,
                    workSessionId,
                    request,
                    cancellationToken);
                await push.NotifyAsync(created, cancellationToken);
                return created;
            });
        });

        device.MapPost("/{approvalRequestId:guid}/reserve", async (
            ClaimsPrincipal principal,
            Guid approvalRequestId,
            ReservePosApprovalForDeviceRequest request,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
            await Handle(() => service.ReserveForDeviceAsync(
                RequiredDeviceGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
                RequiredDeviceGuid(principal, PosAuthenticationDefaults.DeviceIdClaim),
                approvalRequestId,
                request,
                cancellationToken)));

        device.MapPost("/{approvalRequestId:guid}/complete", async (
            ClaimsPrincipal principal,
            Guid approvalRequestId,
            CompletePosApprovalForDeviceRequest request,
            PosApprovalService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await service.CompleteForDeviceAsync(
                    RequiredDeviceGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
                    RequiredDeviceGuid(principal, PosAuthenticationDefaults.DeviceIdClaim),
                    approvalRequestId,
                    request,
                    cancellationToken);
                return Results.NoContent();
            }
            catch (PosApprovalException exception) { return Problem(exception); }
        });

        return endpoints;
    }

    private static Guid RequiredDeviceGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) && value != Guid.Empty
            ? value
            : throw new PosApprovalException("Forbidden", $"The authenticated device lacks claim '{claimType}'.");

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (PosApprovalException exception) { return Problem(exception); }
    }

    private static async Task<IResult> HandleNullable<T>(Func<Task<T?>> action)
        where T : class
    {
        try
        {
            var result = await action();
            return result is null ? Results.NotFound() : Results.Ok(result);
        }
        catch (PosApprovalException exception) { return Problem(exception); }
    }

    private static IResult HandleSync(Func<IResult> action)
    {
        try { return action(); }
        catch (PosApprovalException exception) { return Problem(exception); }
    }

    private static async Task<IResult> HandleNoContent(Func<Task> action)
    {
        try { await action(); return Results.NoContent(); }
        catch (PosApprovalException exception) { return Problem(exception); }
    }

    private static IResult Problem(PosApprovalException exception)
    {
        var statusCode = exception.Code switch
        {
            "NotFound" => StatusCodes.Status404NotFound,
            "Forbidden" or "SelfApprovalForbidden" => StatusCodes.Status403Forbidden,
            "AlreadyDecidedOrExpired" or "InvalidApproval" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return Results.Problem(
            exception.Message,
            statusCode: statusCode,
            title: exception.Code);
    }
}

public static class PosApprovalClaimsPrincipalExtensions
{
    public static PosApprovalUserIdentity ToPosApprovalIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(
        ClaimsPrincipal principal,
        string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value) &&
        value != Guid.Empty
            ? value
            : throw new PosApprovalException(
                "Forbidden",
                $"The authenticated identity lacks claim '{claimType}'.");
}
