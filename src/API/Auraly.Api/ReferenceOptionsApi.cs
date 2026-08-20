using Auraly.Application.Catalog;

namespace Auraly.Api;

public static class ReferenceOptionsApi
{
    public static IEndpointRouteBuilder MapReferenceOptionsApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/commerce/v1/reference-options")
            .RequireAuthorization();

        group.MapGet("/{catalogCode}", async (
            string catalogCode,
            ReferenceOptionService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ListAsync(
                    catalogCode, cancellationToken));
            }
            catch (CatalogValidationException exception)
            {
                return Results.Problem(exception.Message, statusCode: 400);
            }
        });
        return endpoints;
    }
}
