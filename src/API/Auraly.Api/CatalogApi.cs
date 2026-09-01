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

        administration.MapGet("/{productId:guid}/warehouse-availability", async (
            HttpContext context, CatalogService service, Guid productId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.WarehouseAvailabilityAsync(
                context.User.ToCatalogUserIdentity(), productId, ct))));

        var onlinePos = endpoints.MapGroup("/api/commerce/v1/pos/catalog")
            .RequireAuthorization("pos.user");

        onlinePos.MapGet("/products/{productId:guid}/warehouse-availability", async (
            HttpContext context, CatalogService service, Guid productId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PosWarehouseAvailabilityAsync(
                context.User.ToCatalogUserIdentity(), productId, ct))));

        administration.MapGet("/", async (
            HttpContext context, CatalogService service, int? pageSize, string? afterProductCode,
            string? productCode, string? reference, string? barcode, string? name, bool? isActive,
            Guid? supplierId, decimal? minimumPrice, decimal? maximumPrice, bool? sortDescending,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PageAsync(
                context.User.ToCatalogUserIdentity(),
                new ProductPageRequest(pageSize ?? 50, afterProductCode, productCode, reference, barcode, name, isActive, supplierId, minimumPrice, maximumPrice, sortDescending ?? false),
                ct))));

        administration.MapGet("/{productId:guid}/tax-configuration", async (
            HttpContext context, CatalogService service, Guid productId, CancellationToken ct) =>
            await Handle(async () =>
            {
                var result = await service.GetProductTaxConfigurationAsync(
                    context.User.ToCatalogUserIdentity(), productId, ct);
                return result is null ? Results.NotFound() : Results.Ok(result);
            }));

        administration.MapGet("/{productId:guid}/rotation", async (
            HttpContext context, CatalogService service, Guid productId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ProductRotationAsync(
                context.User.ToCatalogUserIdentity(), productId, ct))));

        administration.MapPut("/{productId:guid}/tax-configuration", async (
            HttpContext context, CatalogService service, Guid productId,
            SaveProductTaxConfigurationRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SaveProductTaxConfigurationAsync(
                context.User.ToCatalogUserIdentity(), productId, request, ct))));
        administration.MapPost("/{productId:guid}/deactivate", async (
            HttpContext context, CatalogService service, Guid productId, CancellationToken ct) =>
            await Handle(async () =>
            {
                await service.DeactivateAsync(context.User.ToCatalogUserIdentity(), productId, ct);
                return Results.NoContent();
            }));

        administration.MapPatch("/{productId:guid}/status", async (
            HttpContext context, CatalogService service, Guid productId,
            SetProductStatusRequest request, CancellationToken ct) =>
            await Handle(async () =>
            {
                await service.SetStatusAsync(
                    context.User.ToCatalogUserIdentity(), productId, request.IsActive, ct);
                return Results.NoContent();
            }));
        var taxes = endpoints.MapGroup("/api/commerce/v1/tax-profiles")
            .RequireAuthorization("catalog.user");

        taxes.MapGet("/", async (
            HttpContext context, CatalogService service, bool? includeInactive,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ListTaxProfilesAsync(
                context.User.ToCatalogUserIdentity(), includeInactive ?? false, ct))));

        taxes.MapPost("/", async (
            HttpContext context, CatalogService service, SaveTaxProfileRequest request,
            CancellationToken ct) =>
            await Handle(async () =>
            {
                var result = await service.SaveTaxProfileAsync(
                    context.User.ToCatalogUserIdentity(), null, request, ct);
                return Results.Created($"/api/commerce/v1/tax-profiles/{result.TaxProfileId}", result);
            }));

        taxes.MapPut("/{taxProfileId:guid}", async (
            HttpContext context, CatalogService service, Guid taxProfileId,
            SaveTaxProfileRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.SaveTaxProfileAsync(
                context.User.ToCatalogUserIdentity(), taxProfileId, request, ct))));
        var pos = endpoints.MapGroup("/api/pos/v1")
            .RequireAuthorization("pos.enrolled");

        pos.MapPost("/catalog/sync-sessions", async (
            HttpContext context, PosCatalogService service, Guid businessId, Guid warehouseId, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.StartSyncAsync(
                context.User.ToCatalogDeviceIdentity(businessId, warehouseId), ct))));

        pos.MapGet("/catalog/sync-sessions/{sessionId:guid}/pages", async (
            HttpContext context, PosCatalogService service, Guid sessionId, Guid businessId, Guid warehouseId,
            string? cursor, int? pageSize, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.BootstrapPageAsync(
                context.User.ToCatalogDeviceIdentity(businessId, warehouseId),
                sessionId, cursor, pageSize ?? 500, ct))));

        pos.MapGet("/catalog/changes", async (
            HttpContext context, PosCatalogService service, Guid businessId, Guid warehouseId,
            long? cursor, int? pageSize, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.ChangesAsync(
                context.User.ToCatalogDeviceIdentity(businessId, warehouseId),
                cursor ?? 0, pageSize ?? 500, ct))));

        pos.MapGet("/pricing/snapshot", async (
            HttpContext context, PosCatalogService service, Guid businessId, Guid warehouseId,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.PricingSnapshotAsync(
                context.User.ToCatalogDeviceIdentity(businessId, warehouseId), ct))));

        pos.MapPost("/inventory/availability", async (
            HttpContext context, PosCatalogService service, Guid businessId,
            InventoryAvailabilityRequest request, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.AvailabilityAsync(
                context.User.ToCatalogDeviceIdentity(businessId, request.WarehouseId), request, ct))));

        pos.MapGet("/inventory/products/{productId:guid}/warehouse-availability", async (
            HttpContext context, PosCatalogService service, Guid productId,
            Guid businessId, bool? includeOtherBusinesses, CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.WarehouseAvailabilityAsync(
                context.User.ToCatalogDeviceIdentity(businessId, Guid.Empty),
                productId, includeOtherBusinesses ?? false, ct))));

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

    public static CatalogDeviceIdentity ToCatalogDeviceIdentity(
        this ClaimsPrincipal principal,
        Guid businessId,
        Guid warehouseId) =>
        new(
            RequiredGuid(principal, PosAuthenticationDefaults.DeviceIdClaim),
            RequiredGuid(principal, PosAuthenticationDefaults.TenantIdClaim),
            businessId,
            warehouseId);

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new CatalogForbiddenException($"The authenticated identity lacks claim '{claimType}'.");
}
