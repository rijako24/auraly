using System.Security.Claims;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;

namespace Auraly.Api;

public static class FiscalApi
{
    public static IEndpointRouteBuilder MapFiscalApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/fiscal/documents")
            .RequireAuthorization("fiscal.user");
        group.MapGet("/{documentId:guid}", async (
            HttpContext context, FiscalDocumentService service, Guid documentId, CancellationToken ct) =>
            await Handle(async () =>
            {
                var result = await service.GetAsync(context.User.ToFiscalUserIdentity(), documentId, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }));
        group.MapGet("/", async (
            HttpContext context, FiscalDocumentService service, int? page, int? pageSize,
            string? status, string? auralyNumber, string? dianNumber, string? cufe,
            Guid? registerId, DateTimeOffset? issuedFrom, DateTimeOffset? issuedTo,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PageAsync(
                context.User.ToFiscalUserIdentity(),
                new FiscalDocumentQuery(page ?? 1, pageSize ?? 50, status, auralyNumber,
                    dianNumber, cufe, registerId, issuedFrom, issuedTo), ct))));
        group.MapPost("/{documentId:guid}/retry", async (
            HttpContext context, FiscalDocumentService service, Guid documentId, CancellationToken ct) =>
            await Handle(async () =>
            {
                var result = await service.RetryAsync(context.User.ToFiscalUserIdentity(), documentId, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }));

        endpoints.MapGet("/api/pos/v1/fiscal/statuses", async (
            HttpContext context,
            PosFiscalStatusService service,
            string? cursor,
            int? pageSize,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var identity = context.User.ToPosDeviceIdentity();
                var device = new PosFiscalDeviceContext(
                    identity.DeviceId,
                    identity.BusinessId,
                    identity.RegisterId,
                    identity.Permissions);
                return Results.Ok(await service.PageAsync(
                    device, cursor, pageSize ?? 100, ct));
            }))
            .RequireAuthorization("pos.fiscal.status.sync");

        return endpoints;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (FiscalForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (FiscalOperationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    private static FiscalUserIdentity ToFiscalUserIdentity(this ClaimsPrincipal principal)
    {
        var userId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUser)
            ? parsedUser : throw new FiscalForbiddenException("The authenticated identity lacks a valid user identifier.");
        var businessId = Guid.TryParse(principal.FindFirstValue("business_id"), out var parsedBusiness)
            ? parsedBusiness : throw new FiscalForbiddenException("The authenticated identity lacks a valid business identifier.");
        return new FiscalUserIdentity(userId, businessId,
            principal.FindAll("permission").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));
    }
}