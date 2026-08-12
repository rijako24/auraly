using System.Security.Claims;
using Auraly.Application.Pricing;
using Auraly.Contracts.Pricing;

namespace Auraly.Api;

public static class PricingApi
{
    public static IEndpointRouteBuilder MapPricingApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pricing")
            .RequireAuthorization("pricing.user");

        group.MapGet("/proposals", async (
            HttpContext context, int? page, int? pageSize, string? search,
            string? status, Guid? supplierId, Guid? sourceDocumentId,
            PricingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.ListAsync(
                context.User.ToPricingIdentity(),
                new(page ?? 1, pageSize ?? 25, search, status, supplierId, sourceDocumentId), ct),
                Results.Ok));

        group.MapPost("/calculate", (
            HttpContext context, PriceCalculationRequest request, PricingService service) =>
            Execute(() => Results.Ok(service.Calculate(context.User.ToPricingIdentity(), request))));

        group.MapPut("/proposals/{proposalId:guid}", async (
            HttpContext context, Guid proposalId, ReviewPriceProposalRequest request,
            PricingService service, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await service.ReviewAsync(context.User.ToPricingIdentity(), proposalId, request, ct);
                return Results.NoContent();
            }));

        group.MapPost("/proposals/{proposalId:guid}/reject", async (
            HttpContext context, Guid proposalId, RejectPriceProposalRequest request,
            PricingService service, CancellationToken ct) =>
            await ExecuteAsync(async () =>
            {
                await service.RejectAsync(context.User.ToPricingIdentity(), proposalId, request, ct);
                return Results.NoContent();
            }));

        group.MapPost("/publish", async (
            HttpContext context, PublishPricesRequest request,
            PricingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.PublishAsync(
                context.User.ToPricingIdentity(), request, ct), Results.Ok));

        group.MapGet("/products/{productId:guid}/context", async (
            HttpContext context, Guid productId, PricingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.GetProductContextAsync(
                context.User.ToPricingIdentity(), productId, ct), Results.Ok));

        group.MapPut("/products/{productId:guid}/prepared-price", async (
            HttpContext context, Guid productId, PublishProductPriceRequest request,
            PricingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.SavePreparedProductAsync(
                context.User.ToPricingIdentity(), productId, request, ct), Results.Ok));
        group.MapGet("/products/{productId:guid}/history", async (
            HttpContext context, Guid productId, PricingService service, CancellationToken ct) =>
            await ExecuteAsync(() => service.HistoryAsync(
                context.User.ToPricingIdentity(), productId, ct), Results.Ok));

        return endpoints;
    }

    private static IResult Execute(Func<IResult> action)
    {
        try { return action(); }
        catch (PricingForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (PricingValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (PricingConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
        catch (PricingNotFoundException exception)
        { return Results.Problem(exception.Message, statusCode: 404); }
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PricingForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (PricingValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (PricingConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
        catch (PricingNotFoundException exception)
        { return Results.Problem(exception.Message, statusCode: 404); }
    }

    private static async Task<IResult> ExecuteAsync<T>(
        Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (PricingForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (PricingValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (PricingConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
        catch (PricingNotFoundException exception)
        { return Results.Problem(exception.Message, statusCode: 404); }
    }
}

public static class PricingClaimsPrincipalExtensions
{
    public static PricingUserIdentity ToPricingIdentity(this ClaimsPrincipal principal) =>
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
            : throw new PricingForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
