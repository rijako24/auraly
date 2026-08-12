using Auraly.Application.Parties;
using Auraly.Contracts.Parties;

namespace Auraly.Api;

public static class ExternalCustomerReconciliationApi
{
    public static IEndpointRouteBuilder MapExternalCustomerReconciliationApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/commerce/v1/external-customers")
            .RequireAuthorization("parties.user");

        group.MapGet("/", async (
            HttpContext context,
            ExternalCustomerReconciliationService service,
            int? page,
            int? pageSize,
            string? search,
            string? status,
            Guid? integrationConnectionId,
            CancellationToken cancellationToken) =>
            await HandleAsync(async () => Results.Ok(await service.PageAsync(
                context.User.ToPartyUserIdentity(),
                page ?? 1,
                new ExternalCustomerReconciliationQuery(
                    pageSize ?? 25,
                    search,
                    status,
                    integrationConnectionId),
                cancellationToken))));

        group.MapPost("/{externalCommerceCustomerId:guid}/reconcile", async (
            HttpContext context,
            ExternalCustomerReconciliationService service,
            Guid externalCommerceCustomerId,
            CancellationToken cancellationToken) =>
            await HandleAsync(async () => Results.Ok(await service.ReconcileAsync(
                context.User.ToPartyUserIdentity(),
                externalCommerceCustomerId,
                cancellationToken))));

        group.MapPost("/reconcile-pending", async (
            HttpContext context,
            ExternalCustomerReconciliationService service,
            ReconcilePendingExternalCustomersRequest request,
            CancellationToken cancellationToken) =>
            await HandleAsync(async () => Results.Ok(await service.ReconcilePendingAsync(
                context.User.ToPartyUserIdentity(),
                request,
                cancellationToken))));

        return endpoints;
    }

    private static async Task<IResult> HandleAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PartyForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: 403);
        }
        catch (PartyValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: 400);
        }
        catch (PartyConflictException exception)
        {
            return Results.Problem(
                exception.Message,
                statusCode: 409,
                title: "PartyConflict");
        }
    }
}
