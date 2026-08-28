using System.Security.Claims;
using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
namespace Auraly.Api;
public static class InventoryApi
{
    public static IEndpointRouteBuilder MapInventoryApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/inventory/products", async (ClaimsPrincipal principal, Guid warehouseId, Guid? productCategoryId, string? search, int page, int pageSize, InventoryQueryService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.GetProductsAsync(identity, new(identity.BusinessId, warehouseId, search, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, productCategoryId), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/conversion-products", async (ClaimsPrincipal principal, Guid warehouseId, Guid? familyRootProductId, string? search, int page, int pageSize, InventoryQueryService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.GetConversionProductsAsync(identity, new(identity.BusinessId, warehouseId, familyRootProductId, search, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/warehouses", async (ClaimsPrincipal principal, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.GetWarehousesAsync(principal.ToInventoryIdentity(), token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/balances", async (ClaimsPrincipal principal, Guid? warehouseId, Guid? productId, string? search, bool? onlyWithStock, int page, int pageSize, InventoryQueryService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.GetBalancesAsync(identity, new(identity.BusinessId, warehouseId, search, onlyWithStock ?? false, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize, productId), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/warehouse-masters", async (ClaimsPrincipal principal, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.GetWarehouseMastersAsync(principal.ToInventoryIdentity(), token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory/warehouse-masters", async (ClaimsPrincipal principal, SaveWarehouseRequest request, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.SaveWarehouseAsync(principal.ToInventoryIdentity(), null, request, token), value => Results.Created($"/api/commerce/v1/inventory/warehouse-masters/{value.WarehouseId:D}", value))).RequireAuthorization("inventory.user");
        endpoints.MapPut("/api/commerce/v1/inventory/warehouse-masters/{warehouseId:guid}", async (ClaimsPrincipal principal, Guid warehouseId, SaveWarehouseRequest request, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.SaveWarehouseAsync(principal.ToInventoryIdentity(), warehouseId, request, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/reasons", async (ClaimsPrincipal principal, string? operationType, bool? includeInactive, string? search, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.GetReasonsAsync(principal.ToInventoryIdentity(), operationType, includeInactive ?? false, search, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/reasons", async (ClaimsPrincipal principal, string reasonType, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.GetSelectableReasonsAsync(principal.ToInventoryIdentity(), reasonType, token), Results.Ok)).RequireAuthorization();
        endpoints.MapPost("/api/commerce/v1/inventory/reasons", async (ClaimsPrincipal principal, SaveInventoryReasonRequest request, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.SaveReasonAsync(principal.ToInventoryIdentity(), null, request, token), value => Results.Created($"/api/commerce/v1/inventory/reasons/{value.InventoryReasonId:D}", value))).RequireAuthorization("inventory.user");
        endpoints.MapPut("/api/commerce/v1/inventory/reasons/{inventoryReasonId:guid}", async (ClaimsPrincipal principal, Guid inventoryReasonId, SaveInventoryReasonRequest request, InventoryQueryService service, CancellationToken token) => await ExecuteAsync(() => service.SaveReasonAsync(principal.ToInventoryIdentity(), inventoryReasonId, request, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/movements", async (ClaimsPrincipal principal, Guid? warehouseId, Guid? productId, string? search, string? documentType, string? movementType, DateTimeOffset? from, DateTimeOffset? to, int page, int pageSize, InventoryQueryService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.GetMovementsAsync(identity, new(identity.BusinessId, warehouseId, productId, search, documentType, movementType, from, to, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/operations", async (ClaimsPrincipal principal, Guid? warehouseId, string? search, string? documentType, string? status, DateTimeOffset? from, DateTimeOffset? to, string? reasonCode, Guid? destinationWarehouseId, Guid? supplierId, string? purchaseEvidenceType, int page, int pageSize, InventoryQueryService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.GetOperationsAsync(identity, new(identity.BusinessId, warehouseId, search, documentType, status, from, to, reasonCode, destinationWarehouseId, supplierId, purchaseEvidenceType, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/operations/{documentId:guid}",
            async (ClaimsPrincipal principal, Guid documentId, InventoryQueryService service, CancellationToken token) =>
                await ExecuteAsync(
                    () => service.GetOperationDetailAsync(principal.ToInventoryIdentity(), documentId, token),
                    result => result is null ? Results.NotFound() : Results.Ok(result)))
            .RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/warehouse-transfers/pending", async (ClaimsPrincipal principal, Guid? destinationWarehouseId, string? search, int page, int pageSize, InventoryQueryService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.GetPendingTransfersAsync(identity,
                new(identity.BusinessId, destinationWarehouseId, search, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/warehouse-transfers/{transferId:guid}", async (ClaimsPrincipal principal, Guid transferId, InventoryQueryService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetTransferAsync(principal.ToInventoryIdentity(), transferId, token),
                value => value is null ? Results.NotFound() : Results.Ok(value))).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/physical-counts", async (ClaimsPrincipal principal, Guid? warehouseId, string? search, string? status, int page, int pageSize, InventoryPhysicalCountService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.ListAsync(identity, new(identity.BusinessId, warehouseId, search, status, page == 0 ? 1 : page, pageSize == 0 ? 50 : pageSize), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/physical-count-drafts", async (ClaimsPrincipal principal, Guid? warehouseId, string? search, DateTimeOffset? from, DateTimeOffset? to, string? status, int page, int pageSize, InventoryPhysicalCountService service, CancellationToken token) =>
        {
            var identity = principal.ToInventoryIdentity();
            return await ExecuteAsync(() => service.ListDraftsAsync(identity, new(identity.BusinessId, warehouseId, search, from, to, status, page == 0 ? 1 : page, pageSize == 0 ? 20 : pageSize), token), Results.Ok);
        }).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/physical-counts/{countId:guid}", async (ClaimsPrincipal principal, Guid countId, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetAsync(principal.ToInventoryIdentity(), countId, token), value => value is null ? Results.NotFound() : Results.Ok(value))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory/physical-counts", async (ClaimsPrincipal principal, CreateInventoryPhysicalCountRequest request, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CreateAsync(principal.ToInventoryIdentity(), request, token), value => Results.Created($"/api/commerce/v1/inventory/physical-counts/{value.InventoryPhysicalCountId:D}", value))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory/physical-counts/{countId:guid}/drafts", async (ClaimsPrincipal principal, Guid countId, CreateInventoryPhysicalCountDraftRequest request, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.CreateDraftAsync(principal.ToInventoryIdentity(), countId, request, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapPut("/api/commerce/v1/inventory/physical-counts/{countId:guid}/drafts/{draftId:guid}", async (ClaimsPrincipal principal, Guid countId, Guid draftId, SaveInventoryPhysicalCountDraftRequest request, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.SaveDraftAsync(principal.ToInventoryIdentity(), countId, draftId, request, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory/physical-counts/{countId:guid}/reconciliations", async (ClaimsPrincipal principal, Guid countId, PrepareInventoryReconciliationRequest request, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.PrepareReconciliationAsync(principal.ToInventoryIdentity(), countId, request, token), value => Results.Created($"/api/commerce/v1/inventory/physical-counts/{countId:D}/reconciliation", value))).RequireAuthorization("inventory.user");
        endpoints.MapGet("/api/commerce/v1/inventory/physical-counts/{countId:guid}/reconciliation", async (ClaimsPrincipal principal, Guid countId, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.GetReconciliationAsync(principal.ToInventoryIdentity(), countId, token), value => value is null ? Results.NotFound() : Results.Ok(value))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory/physical-counts/{countId:guid}/reconciliations/{reconciliationId:guid}/drafts", async (ClaimsPrincipal principal, Guid countId, Guid reconciliationId, SaveInventoryReconciliationDraftRequest request, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.SaveReconciliationDraftAsync(principal.ToInventoryIdentity(), countId, reconciliationId, request, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory/physical-counts/{countId:guid}/reconciliations/{reconciliationId:guid}/apply", async (ClaimsPrincipal principal, Guid countId, Guid reconciliationId, ApplyInventoryReconciliationRequest request, InventoryPhysicalCountService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ApplyAsync(principal.ToInventoryIdentity(), countId, reconciliationId, request, token), value => Results.Accepted($"/api/commerce/v1/inventory/physical-counts/{countId:D}", value))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/stock-counts/start", async (ClaimsPrincipal user, StartStockCountRequest request, InventoryOperationService service, CancellationToken token) => await ExecuteAsync(() => service.StartCountAsync(user.ToInventoryIdentity(), request, token), Results.Ok)).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/stock-counts/{documentId:guid}/confirm", async (HttpContext context, Guid documentId, ConfirmStockCountRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ConfirmCountAsync(context.User.ToInventoryIdentity(), documentId, Key(context), request, token))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/stock-counts/apply", async (HttpContext context, ApplyStockCountRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ApplyCountAsync(context.User.ToInventoryIdentity(), Key(context), request, token))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory-adjustments/confirm", async (HttpContext context, ConfirmInventoryAdjustmentRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ConfirmAdjustmentAsync(context.User.ToInventoryIdentity(), Key(context), request, token))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/warehouse-transfers/dispatch", async (HttpContext context, DispatchWarehouseTransferRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.DispatchTransferAsync(context.User.ToInventoryIdentity(), Key(context), request, token))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/warehouse-transfers/{transferId:guid}/receipts", async (HttpContext context, Guid transferId, ReceiveWarehouseTransferRequest request, InventoryOperationService service, CancellationToken token) =>
            await ExecuteAsync(() => service.ReceiveTransferAsync(context.User.ToInventoryIdentity(), transferId, Key(context), request, token),
                value => Results.Accepted($"/api/commerce/v1/warehouse-transfers/{transferId:D}", value))).RequireAuthorization("inventory.user");
        endpoints.MapPost("/api/commerce/v1/inventory-damages/confirm", async (HttpContext context, ConfirmInventoryDamageRequest request, InventoryOperationService service, CancellationToken token) => await AcceptedAsync(() => service.ConfirmDamageAsync(context.User.ToInventoryIdentity(), Key(context), request, token))).RequireAuthorization("inventory.user");
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
