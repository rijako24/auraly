using System.Security.Claims;
using Auraly.Application.Payables;
using Auraly.Contracts.Payables;

namespace Auraly.Api;

public static class PayablesApi
{
    public static IEndpointRouteBuilder MapPayablesApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/commerce/v1/payables",
                async (HttpContext context, int page, int pageSize, string? search,
                    Guid? supplierId, string? status, bool? overdue,
                    PayablesService service, CancellationToken cancellationToken) =>
                    await ExecuteAsync(() => service.ListAsync(
                        context.User.ToPayablesIdentity(),
                        new PayableQuery(page, pageSize, search, supplierId, status, overdue),
                        cancellationToken), Results.Ok))
            .RequireAuthorization("payables.user");

        endpoints.MapGet(
                "/api/commerce/v1/payables/{payableId:guid}",
                async (HttpContext context, Guid payableId, PayablesService service,
                    CancellationToken cancellationToken) =>
                    await ExecuteAsync(async () =>
                    {
                        var value = await service.GetAsync(
                            context.User.ToPayablesIdentity(), payableId, cancellationToken);
                        return value is null ? Results.NotFound() : Results.Ok(value);
                    }))
            .RequireAuthorization("payables.user");

        endpoints.MapPost(
                "/api/commerce/v1/payable-payments/confirm",
                async (HttpContext context, ConfirmSupplierPaymentRequest request,
                    PayablesService service, CancellationToken cancellationToken) =>
                    await ExecuteAsync(async () =>
                    {
                        var key = context.Request.Headers["Idempotency-Key"].ToString();
                        var value = await service.ConfirmPaymentAsync(
                            context.User.ToPayablesIdentity(), key, request, cancellationToken);
                        return Results.Accepted(
                            $"/api/commerce/v1/payable-payments/{value.PaymentId:D}", value);
                    }))
            .RequireAuthorization("payables.user");
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PayablesForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (PayablesValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (PayablesConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
    }

    private static async Task<IResult> ExecuteAsync<T>(
        Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (PayablesForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (PayablesValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (PayablesConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
    }
}

public static class PayablesClaimsPrincipalExtensions
{
    public static PayablesUserIdentity ToPayablesIdentity(this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new PayablesForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
