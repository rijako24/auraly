using System.Security.Claims;
using Auraly.Application.Cash;
using Auraly.Contracts.Cash;

namespace Auraly.Api;

public static class CashApi
{
    public static IEndpointRouteBuilder MapCashApi(this IEndpointRouteBuilder endpoints)
    {
        var cash = endpoints.MapGroup("/api/commerce/v1/cash")
            .RequireAuthorization("pos.user");

        cash.MapGet("/registers/{registerId:guid}/session", async (
            HttpContext context,
            Guid registerId,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var session = await service.CurrentAsync(
                    context.User.ToCashUserIdentity(), registerId, ct);
                return session is null ? Results.NoContent() : Results.Ok(session);
            }));

        cash.MapPost("/registers/{registerId:guid}/session", async (
            HttpContext context,
            Guid registerId,
            OpenCashSessionRequest request,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.OpenOrResumeAsync(
                context.User.ToCashUserIdentity(), registerId, request, ct))));

        cash.MapPost("/registers/{registerId:guid}/handoff-authorizations", async (
            HttpContext context,
            Guid registerId,
            SupervisorAuthorizationRequest request,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.AuthorizeHandoffAsync(
                context.User.ToCashUserIdentity(), registerId, request, ct))));

        cash.MapPost("/registers/{registerId:guid}/handoff", async (
            HttpContext context,
            Guid registerId,
            HandoffCashRequest request,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.HandoffAsync(
                context.User.ToCashUserIdentity(), registerId, request, ct))));

        cash.MapPost("/registers/{registerId:guid}/close", async (
            HttpContext context,
            Guid registerId,
            CloseCashSessionRequest request,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.CloseAsync(
                context.User.ToCashUserIdentity(), registerId, request, ct))));

        cash.MapGet("/counts/{cashCountId:guid}/receipt", async (
            HttpContext context,
            Guid cashCountId,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var receipt = await service.ReceiptAsync(
                    context.User.ToCashUserIdentity(), cashCountId, ct);
                return receipt is null ? Results.NotFound() : Results.Ok(receipt);
            }));

        cash.MapGet("/registers/{registerId:guid}/daily/{businessDate}", async (
            HttpContext context,
            Guid registerId,
            DateOnly businessDate,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.DailyAsync(
                context.User.ToCashUserIdentity(), registerId, businessDate, ct))));

        var security = endpoints.MapGroup(
                "/api/commerce/v1/security/supervisor-credentials")
            .RequireAuthorization("pos.user");

        security.MapPost("/", async (
            HttpContext context,
            ProvisionSupervisorCredentialRequest request,
            CashSessionService service,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(
                await service.ProvisionSupervisorCredentialAsync(
                    context.User.ToCashUserIdentity(), request, ct))));

        return endpoints;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CashForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (CashValidationException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (CashNotFoundException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status404NotFound);
        }
        catch (CashConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "CashSessionConflict");
        }
    }
}

public static class CashClaimsPrincipalExtensions
{
    public static CashUserIdentity ToCashUserIdentity(this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new CashForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
