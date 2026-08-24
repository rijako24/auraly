using System.Security.Claims;
using Auraly.Application.Returns;
using Auraly.Contracts.Returns;

namespace Auraly.Api;

public static class SalesReturnQueryApi
{
    public static IEndpointRouteBuilder MapSalesReturnQueryApi(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/sales-returns/sales", async (
                HttpContext context, int page, int pageSize, string? search,
                string? customer,
                DateOnly? from, DateOnly? to, bool? withAvailableQuantity,
                SalesReturnQueryService service, CancellationToken token) =>
            await Execute(() => service.ListReturnableSalesAsync(
                context.User.ToSalesReturnQueryIdentity(),
                new(page, pageSize, search, customer, from, to, withAvailableQuantity), token),
                Results.Ok))
            .RequireAuthorization("returns.user");

        endpoints.MapGet("/api/commerce/v1/sales-returns/sales/{documentId:guid}",
            async (HttpContext context, Guid documentId,
                SalesReturnQueryService service, CancellationToken token) =>
                await Execute(async () =>
                {
                    var value = await service.GetReturnableSaleAsync(
                        context.User.ToSalesReturnQueryIdentity(), documentId, token);
                    return value is null ? Results.NotFound() : Results.Ok(value);
                }))
            .RequireAuthorization("returns.user");

        endpoints.MapGet("/api/commerce/v1/sales-returns", async (
                HttpContext context, int page, int pageSize, string? search,
                string? status, DateOnly? from, DateOnly? to,
                SalesReturnQueryService service, CancellationToken token) =>
            await Execute(() => service.ListReturnsAsync(
                context.User.ToSalesReturnQueryIdentity(),
                new(page, pageSize, search, status, from, to), token), Results.Ok))
            .RequireAuthorization("returns.user");

        endpoints.MapGet("/api/commerce/v1/sales-returns/{returnId:guid}",
            async (HttpContext context, Guid returnId,
                SalesReturnQueryService service, CancellationToken token) =>
                await Execute(async () =>
                {
                    var value = await service.GetReturnAsync(
                        context.User.ToSalesReturnQueryIdentity(), returnId, token);
                    return value is null ? Results.NotFound() : Results.Ok(value);
                }))
            .RequireAuthorization("returns.user");

        return endpoints;
    }

    private static async Task<IResult> Execute<T>(
        Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (SalesReturnForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (SalesReturnValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
    }

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SalesReturnForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (SalesReturnValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
    }

    private static SalesReturnUserIdentity ToSalesReturnQueryIdentity(
        this ClaimsPrincipal principal) => new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            principal.FindAll("permission")
                .Concat(principal.FindAll(PosAuthenticationDefaults.PermissionClaim))
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new SalesReturnForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
