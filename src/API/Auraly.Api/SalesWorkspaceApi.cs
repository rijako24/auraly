using System.Security.Claims;
using Auraly.Application.Organization;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;
using Auraly.BuildingBlocks.Application.Synchronization;
using Auraly.Contracts.Authentication;

namespace Auraly.Api;

public static class SalesWorkspaceApi
{
    public static IEndpointRouteBuilder MapSalesWorkspaceApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/workspace")
            .RequireAuthorization("pos.user");

        group.MapGet("/bootstrap", async (
            HttpContext context, SalesWorkspaceService service, CancellationToken ct) =>
            await Handle(async () =>
            {
                context.Response.Headers.CacheControl =
                    "no-store, no-cache, must-revalidate";
                context.Response.Headers.Pragma = "no-cache";
                var identity = context.User.ToSalesWorkspaceUserIdentity();
                var hasPermission = context.User.FindAll("permission").Any(claim =>
                    StringComparer.Ordinal.Equals(
                        claim.Value, CommercePermissionCodes.EnrolledDevicesEnroll));
                var capacity = await service.EnrollmentCapacityAsync(identity, ct);
                var canEnroll = hasPermission && capacity.HasAvailableCapacity;
                return Results.Ok(new SalesWorkspaceBootstrap(
                    identity.TenantId,
                    await service.TenantNameAsync(identity, ct),
                    identity.UserId,
                    context.User.PosUserDisplayName(),
                    await service.ListAsync(identity, ct),
                    canEnroll,
                    capacity.ActiveEnrolledDeviceCount,
                    capacity.MaximumEnrolledDevices,
                    canEnroll
                        ? null
                        : !hasPermission
                            ? "Tu usuario no tiene permiso para enrolar cajas."
                            : $"Ya están enroladas las {capacity.MaximumEnrolledDevices} cajas permitidas. Comunícate con el administrador para liberar una caja o ampliar la capacidad."));
            }));

        group.MapGet("/options", async (
            HttpContext context, SalesWorkspaceService service, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ListAsync(
                context.User.ToSalesWorkspaceUserIdentity(), ct))));

        group.MapPost("/synchronization/negotiate", async (
            HttpContext context, Guid businessId, SalesWorkspaceService service,
            IPosSynchronizationPushGateway gateway, CancellationToken ct) =>
            await Handle(async () =>
            {
                var identity = context.User.ToSalesWorkspaceUserIdentity();
                var authorized = (await service.ListAsync(identity, ct))
                    .Any(option => option.BusinessId == businessId);
                if (!authorized)
                    throw new SalesWorkspaceForbiddenException(
                        "La sede no pertenece a la empresa autenticada o no está disponible para ventas.");
                var uri = gateway.CreateUserClientAccessUri(
                    identity.TenantId, businessId, identity.UserId, ct);
                return Results.Ok(new PosSynchronizationNegotiationResponse(
                    uri, DateTimeOffset.UtcNow.AddMinutes(15)));
            }));

        group.MapPost("/select", async (
            HttpContext context, SalesWorkspaceService service,
            SalesWorkspaceSelection selection, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SelectAsync(
                context.User.ToSalesWorkspaceUserIdentity(), selection, ct))));

        group.MapPost("/change", async (
            HttpContext context, SalesWorkspaceService service,
            SalesWorkspaceSelection selection, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ChangeAsync(
                context.User.ToSalesWorkspaceUserIdentity(), selection, ct))));

        return endpoints;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SalesWorkspaceForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (SalesWorkspaceValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}

public static class SalesWorkspaceClaimsPrincipalExtensions
{
    public static SalesWorkspaceUserIdentity ToSalesWorkspaceUserIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, AuthenticationDefaults.IdentityTenantIdClaim),
            principal.FindAll("permission").Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    public static string PosUserDisplayName(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return FirstNonEmpty(
            principal.FindFirstValue("full_name"),
            principal.FindFirstValue("username"),
            principal.FindFirstValue(ClaimTypes.Name),
            "Usuario");
    }

    private static string FirstNonEmpty(params string?[] values) =>
        values.First(value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new SalesWorkspaceForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
