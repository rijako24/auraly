using System.Security.Claims;
using Auraly.Application.Organization;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Organization;

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
                var identity = context.User.ToSalesWorkspaceUserIdentity();
                return Results.Ok(new SalesWorkspaceBootstrap(
                    await service.TenantNameAsync(identity, ct),
                    identity.UserId,
                    context.User.PosUserDisplayName(),
                    await service.ListAsync(identity, ct),
                    context.User.FindAll("permission").Any(claim =>
                        StringComparer.Ordinal.Equals(
                            claim.Value, CommercePermissionCodes.EnrolledDevicesEnroll))));
            }));

        group.MapGet("/options", async (
            HttpContext context, SalesWorkspaceService service, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ListAsync(
                context.User.ToSalesWorkspaceUserIdentity(), ct))));

        group.MapPost("/select", async (
            HttpContext context, SalesWorkspaceService service,
            SalesWorkspaceSelection selection, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SelectAsync(
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
            RequiredGuid(principal, "tenant_id"),
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