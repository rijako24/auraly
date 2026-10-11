using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using QRCoder;

namespace Auraly.Api;

public static class RestaurantDigitalMenuApi
{
    public static IEndpointRouteBuilder MapRestaurantDigitalMenuApi(
        this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup(
            "/api/commerce/v1/restaurant/digital-menu")
            .RequireAuthorization("pos.user");
        admin.MapGet("", async (HttpContext context,
            RestaurantDigitalMenuService service, CancellationToken ct) =>
            await RestaurantAccountApi.Handle(async () =>
            {
                var actor = context.User.ToPartyUserIdentity();
                var config = await service.GetConfigurationAsync(
                    actor.TenantId, actor.BusinessId, actor.Permissions, ct);
                return config is null ? Results.NoContent() : Results.Ok(config);
            }));
        admin.MapPut("", async (HttpContext context,
            RestaurantDigitalMenuService service,
            SaveRestaurantDigitalMenuRequest request, CancellationToken ct) =>
            await RestaurantAccountApi.Handle(async () =>
            {
                var actor = context.User.ToPartyUserIdentity();
                return Results.Ok(await service.SaveConfigurationAsync(
                    actor.TenantId, actor.BusinessId, actor.ActorId,
                    actor.Permissions, request, ct));
            }));
        admin.MapGet("/preview", async (HttpContext context,
            RestaurantDigitalMenuService service, CancellationToken ct) =>
            await RestaurantAccountApi.Handle(async () =>
            {
                var actor = context.User.ToPartyUserIdentity();
                var menu = await service.GetPreviewAsync(actor.TenantId,
                    actor.BusinessId, actor.Permissions, ct);
                return menu is null ? Results.NotFound() : Results.Ok(menu);
            }));
        admin.MapGet("/preview/products", async (HttpContext context,
            RestaurantDigitalMenuService service, Guid categoryId,
            int? page, CancellationToken ct) =>
            await RestaurantAccountApi.Handle(async () =>
            {
                var actor = context.User.ToPartyUserIdentity();
                var products = await service.GetPreviewProductsAsync(
                    actor.TenantId, actor.BusinessId, actor.Permissions,
                    categoryId, page ?? 1, ct);
                return products is null ? Results.NotFound() :
                    Results.Ok(products);
            }));

        var publicMenus = endpoints.MapGroup(
            "/api/public/v1/restaurant-menus").AllowAnonymous();
        publicMenus.MapGet("/{code}", async (
            RestaurantDigitalMenuService service, string code,
            CancellationToken ct) =>
            await RestaurantAccountApi.Handle(async () =>
            {
                var menu = await service.GetPublicAsync(code, ct);
                return menu is null ? Results.NotFound() : Results.Ok(menu);
            }));
        publicMenus.MapGet("/{code}/products", async (
            RestaurantDigitalMenuService service, string code,
            Guid categoryId, int? page, CancellationToken ct) =>
            await RestaurantAccountApi.Handle(async () =>
            {
                var products = await service.GetPublicProductsAsync(
                    code, categoryId, page ?? 1, ct);
                return products is null ? Results.NotFound() : Results.Ok(products);
            }));
        publicMenus.MapGet("/{code}/qr", async (
            RestaurantDigitalMenuService service, string code,
            string url, CancellationToken ct) =>
            await RestaurantAccountApi.Handle(async () =>
            {
                if (url.Length > 300 || !Uri.TryCreate(url, UriKind.Absolute,
                        out var target) ||
                    target.Scheme is not ("http" or "https") ||
                    target.AbsolutePath != $"/menu/{code}" ||
                    target.Query.Length > 0 || target.Fragment.Length > 0)
                    throw new RestaurantAccountValidationException(
                        "El enlace del QR no es válido.");
                var menu = await service.GetPublicAsync(code, ct);
                if (menu is null) return Results.NotFound();
                using var data = QRCodeGenerator.GenerateQrCode(
                    url, QRCodeGenerator.ECCLevel.Q);
                using var qr = new SvgQRCode(data);
                return Results.Content(qr.GetGraphic(
                    pixelsPerModule: 5,
                    darkColorHex: "#065f55",
                    lightColorHex: "#ffffff",
                    drawQuietZones: true,
                    sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute),
                    "image/svg+xml; charset=utf-8");
            }));
        return endpoints;
    }
}
