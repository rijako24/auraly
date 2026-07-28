using System.Security.Claims;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;

namespace Auraly.Api;

public static class CatalogApi
{
    public static IEndpointRouteBuilder MapCatalogApi(this IEndpointRouteBuilder endpoints)
    {
        var administration = endpoints.MapGroup("/api/commerce/v1/products")
            .RequireAuthorization("catalog.user");

        administration.MapPost("/", Execute(async (context, service, request, ct) =>
        {
            var product = await service.CreateAsync(context.User.ToCatalogUserIdentity(), request, ct);
            return Results.Created($"/api/commerce/v1/products/{product.ProductId}", product);
        }));

        administration.MapPut("/{productId:guid}", ExecuteWithId(async (context, service, productId, request, ct) =>
            Results.Ok(await service.UpdateAsync(context.User.ToCatalogUserIdentity(), productId, request, ct))));

        administration.MapGet("/{productId:guid}", async (
            HttpContext context, CatalogService service, Guid productId, CancellationToken ct) =>
            await Handle(async () =>
            {
                var product = await service.GetAsync(context.User.ToCatalogUserIdentity(), productId, ct);
                return product is null ? Results.NotFound() : Results.Ok(product);
            }));

        administration.MapGet("/", async (
            HttpContext context, CatalogService service, int? pageSize, string? afterProductCode,
            string? productCode, string? reference, string? barcode, string? name, bool? isActive,
            Guid? supplierId, decimal? minimumPrice, decimal? maximumPrice, bool? sortDescending,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PageAsync(
                context.User.ToCatalogUserIdentity(),
                new ProductPageRequest(pageSize ?? 50, afterProductCode, productCode, reference, barcode, name, isActive, supplierId, minimumPrice, maximumPrice, sortDescending ?? false),
                ct))));

        administration.MapPost("/{productId:guid}/deactivate", async (
            HttpContext context, CatalogService service, Guid productId, CancellationToken ct) =>
            await Handle(async () =>
            {
                await service.DeactivateAsync(context.User.ToCatalogUserIdentity(), productId, ct);
                return Results.NoContent();
            }));

        var pos = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.catalog.sync");

        pos.MapPost("/catalog/sync-sessions", async (
            HttpContext context, PosCatalogService service, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.StartSyncAsync(context.User.ToCatalogDeviceIdentity(), ct))));

        pos.MapGet("/catalog/sync-sessions/{sessionId:guid}/pages", async (
            HttpContext context, PosCatalogService service, Guid sessionId, string? cursor, int? pageSize,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.BootstrapPageAsync(
                context.User.ToCatalogDeviceIdentity(), sessionId, cursor, pageSize ?? 500, ct))));

        pos.MapGet("/catalog/changes", async (
            HttpContext context, PosCatalogService service, long? cursor, int? pageSize, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ChangesAsync(
                context.User.ToCatalogDeviceIdentity(), cursor ?? 0, pageSize ?? 500, ct))));

        pos.MapPost("/inventory/availability", async (
            HttpContext context, PosCatalogService service, InventoryAvailabilityRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.AvailabilityAsync(
                context.User.ToCatalogDeviceIdentity(), request, ct))));

        return endpoints;
    }

    private static Func<HttpContext, CatalogService, SaveProductRequest, CancellationToken, Task<IResult>> Execute(
        Func<HttpContext, CatalogService, SaveProductRequest, CancellationToken, Task<IResult>> action) =>
        async (context, service, request, ct) => await Handle(() => action(context, service, request, ct));

    private static Func<HttpContext, CatalogService, Guid, SaveProductRequest, CancellationToken, Task<IResult>> ExecuteWithId(
        Func<HttpContext, CatalogService, Guid, SaveProductRequest, CancellationToken, Task<IResult>> action) =>
        async (context, service, productId, request, ct) =>
            await Handle(() => action(context, service, productId, request, ct));

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (CatalogForbiddenException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden);
        }
        catch (CatalogValidationException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (CatalogConflictException exception)
        {
            return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}

public static class CatalogClaimsPrincipalExtensions
{
    public static CatalogUserIdentity ToCatalogUserIdentity(this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            principal.FindAll("permission").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));

    public static CatalogDeviceIdentity ToCatalogDeviceIdentity(this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, PosAuthenticationDefaults.DeviceIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.BusinessIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.WarehouseIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.RegisterIdClaim),
            principal.FindAll(PosAuthenticationDefaults.PermissionClaim)
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new CatalogForbiddenException($"The authenticated identity lacks claim '{claimType}'.");
}
