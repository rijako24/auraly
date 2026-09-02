using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;

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

        group.MapPost("/{catalogCode}", async (
            HttpContext context,
            string catalogCode,
            CreateReferenceOptionRequest request,
            ReferenceOptionService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var result = await service.CreateAsync(
                    context.User.ToCatalogUserIdentity(),
                    catalogCode,
                    request,
                    cancellationToken);
                return Results.Created(
                    $"/api/commerce/v1/reference-options/{catalogCode}", result);
            }
            catch (CatalogForbiddenException exception)
            {
                return Results.Problem(exception.Message, statusCode: 403);
            }
            catch (CatalogValidationException exception)
            {
                return Results.Problem(exception.Message, statusCode: 400);
            }
            catch (CatalogConflictException exception)
            {
                return Results.Problem(exception.Message, statusCode: 409);
            }
        });
        return endpoints;
    }
}
