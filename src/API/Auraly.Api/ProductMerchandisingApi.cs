using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;

namespace Auraly.Api;

public static class ProductMerchandisingApi
{
    public static IEndpointRouteBuilder MapProductMerchandisingApi(this IEndpointRouteBuilder endpoints)
    {
        var products = endpoints.MapGroup("/api/commerce/v1/products").RequireAuthorization("catalog.user");
        products.MapGet("/{productId:guid}/merchandising", async (HttpContext context, ProductMerchandisingService service, Guid productId, CancellationToken ct) =>
        {
            var result = await service.GetAsync(context.User.ToCatalogUserIdentity(), productId, ct);
            return result is null ? Results.NotFound() : Results.Ok(result);
        });
        products.MapPut("/{productId:guid}/merchandising", async (HttpContext context, ProductMerchandisingService service, Guid productId, SaveProductMerchandisingRequest request, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.SaveAsync(
                    context.User.ToCatalogUserIdentity(), productId, request, ct));
            }
            catch (CatalogValidationException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
            }
            catch (CatalogConflictException exception)
            {
                return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
            }
        });

        var brands = endpoints.MapGroup("/api/commerce/v1/product-brands").RequireAuthorization("catalog.user");
        brands.MapGet("/", async (HttpContext context, ProductMerchandisingService service, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await service.ListBrandsAsync(context.User.ToCatalogUserIdentity(), includeInactive ?? false, ct)));
        brands.MapPost("/", async (HttpContext context, ProductMerchandisingService service, SaveProductBrandRequest request, CancellationToken ct) =>
        {
            var result = await service.SaveBrandAsync(context.User.ToCatalogUserIdentity(), null, request, ct);
            return Results.Created($"/api/commerce/v1/product-brands/{result.ProductBrandId}", result);
        });
        brands.MapPut("/{id:guid}", async (HttpContext context, ProductMerchandisingService service, Guid id, SaveProductBrandRequest request, CancellationToken ct) =>
            Results.Ok(await service.SaveBrandAsync(context.User.ToCatalogUserIdentity(), id, request, ct)));

        var units = endpoints.MapGroup("/api/commerce/v1/product-units").RequireAuthorization("catalog.user");
        units.MapGet("/", async (HttpContext context, ProductMerchandisingService service, bool? includeInactive, CancellationToken ct) =>
            Results.Ok(await service.ListUnitsAsync(context.User.ToCatalogUserIdentity(), includeInactive ?? false, ct)));
        units.MapPost("/", async (HttpContext context, ProductMerchandisingService service, SaveProductUnitRequest request, CancellationToken ct) =>
        {
            var result = await service.SaveUnitAsync(context.User.ToCatalogUserIdentity(), null, request, ct);
            return Results.Created($"/api/commerce/v1/product-units/{result.ProductUnitId}", result);
        });
        units.MapPut("/{id:guid}", async (HttpContext context, ProductMerchandisingService service, Guid id, SaveProductUnitRequest request, CancellationToken ct) =>
            Results.Ok(await service.SaveUnitAsync(context.User.ToCatalogUserIdentity(), id, request, ct)));
        return endpoints;
    }
}
