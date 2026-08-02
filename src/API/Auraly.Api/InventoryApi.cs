using System.Security.Claims;
using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
namespace Auraly.Api;
public static class InventoryApi
{
    public static IEndpointRouteBuilder MapInventoryApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/commerce/v1/stock-counts/start", async (ClaimsPrincipal user, StartStockCountRequest request, InventoryOperationService service, CancellationToken token) => await ExecuteAsync(() => service.StartCountAsync(user.ToInventoryIdentity(), request, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/stock-counts/{documentId:guid}/confirm", async (HttpContext context, Guid documentId, ConfirmStockCountRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ConfirmCountAsync(context.User.ToInventoryIdentity(), documentId, Key(context), request, token))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory-adjustments/confirm", async (HttpContext context, ConfirmInventoryAdjustmentRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ConfirmAdjustmentAsync(context.User.ToInventoryIdentity(), Key(context), request, token))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/warehouse-transfers/confirm", async (HttpContext context, ConfirmWarehouseTransferRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ConfirmTransferAsync(context.User.ToInventoryIdentity(), Key(context), request, token))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/product-conversions/confirm", async (HttpContext context, ConfirmProductConversionRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ConfirmConversionAsync(context.User.ToInventoryIdentity(), Key(context), request, token))).RequireAuthorization("inventory.user");
        return endpoints;
    }
    private static string Key(HttpContext context) => context.Request.Headers["Idempotency-Key"].ToString();
    private static Task<IResult> AcceptedAsync(Func<Task<InventoryOperationAcceptance>> action) => ExecuteAsync(action, value => Results.Accepted($"/api/commerce/v1/inventory-operations/{value.DocumentId:D}", value));
    private static async Task<IResult> ExecuteAsync<T>(Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (InventoryForbiddenException exception) { return Results.Problem(exception.Message, statusCode: 403); }
        catch (InventoryValidationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
        catch (InventoryConflictException exception) { return Results.Problem(exception.Message, statusCode: 409); }
    }
}
public static class InventoryClaimsPrincipalExtensions
{
    public static InventoryUserIdentity ToInventoryIdentity(this ClaimsPrincipal principal) => new(RequiredGuid(principal, ClaimTypes.NameIdentifier), RequiredGuid(principal, "tenant_id"), RequiredGuid(principal, "business_id"), principal.FindAll("permission").Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) => Guid.TryParse(principal.FindFirstValue(claimType), out var value) ? value : throw new InventoryForbiddenException($"The authenticated identity lacks claim '{claimType}'.");
}
