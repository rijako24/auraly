using System.Security.Claims;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;

namespace Auraly.Api;

public static class OnlineSalesDraftApi
{
    public static IEndpointRouteBuilder MapOnlineSalesDraftApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pos/drafts")
            .RequireAuthorization("pos.user");

        group.MapPost("/active", async (
            HttpContext context,
            OpenOnlineSalesDraftRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.OpenAsync(
                context.User.ToOnlineSalesUserIdentity(), request, ct)));

        group.MapPost("/{draftId:guid}/lines", async (
            HttpContext context,
            Guid draftId,
            AddOnlineSalesDraftProductRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.AddProductAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

        group.MapPut("/{draftId:guid}/lines/{lineId:guid}/quantity", async (
            HttpContext context,
            Guid draftId,
            Guid lineId,
            ChangeOnlineSalesDraftQuantityRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.ChangeQuantityAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, lineId, request, IdempotencyKey(context), ct)));

        group.MapPost("/{draftId:guid}/reset", async (
            HttpContext context,
            Guid draftId,
            ResetOnlineSalesDraftRequest request,
            OnlineSalesDraftService service,
            CancellationToken ct) =>
            await Handle(() => service.ResetAsync(
                context.User.ToOnlineSalesUserIdentity(),
                draftId, request, IdempotencyKey(context), ct)));

        return endpoints;
    }

    private static string IdempotencyKey(HttpContext context) =>
        context.Request.Headers["Idempotency-Key"].ToString();

    private static async Task<IResult> Handle(
        Func<Task<OnlineSalesDraft>> action)
    {
        try { return Results.Ok(await action()); }
        catch (OnlineSalesDraftForbiddenException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (OnlineSalesDraftValidationException exception)
        {
            return Results.Problem(
                exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (OnlineSalesDraftConcurrencyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "SalesDraftVersionConflict");
        }
        catch (OnlineSalesDraftIdempotencyException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "SalesDraftIdempotencyConflict");
        }
    }
}

public static class OnlineSalesDraftClaimsPrincipalExtensions
{
    public static OnlineSalesUserIdentity ToOnlineSalesUserIdentity(
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
            : throw new OnlineSalesDraftForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
