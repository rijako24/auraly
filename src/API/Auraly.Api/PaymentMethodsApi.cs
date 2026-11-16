using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;

namespace Auraly.Api;

public static class PaymentMethodsApi
{
    public static IEndpointRouteBuilder MapPaymentMethodsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/payment-methods").RequireAuthorization();
        group.MapGet("/", async (HttpContext context, [AsParameters] PaymentMethodQuery query,
            PaymentMethodService service, CancellationToken cancellationToken) =>
        {
            try { return Results.Ok(await service.ListAsync(context.User.ToCatalogUserIdentity(), query, cancellationToken)); }
            catch (CatalogForbiddenException error) { return Results.Problem(error.Message, statusCode: 403); }
            catch (CatalogValidationException error) { return Results.Problem(error.Message, statusCode: 400); }
        });
        group.MapPut("/{paymentMethodId:guid}", async (HttpContext context, Guid paymentMethodId,
            SavePaymentMethodRequest request, int? page,int? pageSize,string? search,bool? isActive,string? usage,string? methodCode,
            PaymentMethodService service, CancellationToken cancellationToken) =>
        {
            if (paymentMethodId != request.PaymentMethodId) return Results.Problem("La identidad no coincide.", statusCode: 400);
            try
            {
                var user = context.User.ToCatalogUserIdentity();
                var query=service.ValidateQuery(user,new(page??1,pageSize??50,search,isActive,usage,MethodCode:methodCode));
                var method = await service.SaveAsync(user, request, cancellationToken);
                // Return the canonical filtered page: changing order, activation or
                // membership can move records across page boundaries.
                return Results.Ok(new PaymentMethodSaveResult(method,
                    await service.ListAsync(user, query, cancellationToken)));
            }
            catch (CatalogForbiddenException error) { return Results.Problem(error.Message, statusCode: 403); }
            catch (CatalogValidationException error) { return Results.Problem(error.Message, statusCode: 400); }
            catch (CatalogConflictException error) { return Results.Problem(error.Message, statusCode: 409); }
        });
        return endpoints;
    }
}
