using Auraly.Application.Catalog;
using Auraly.Application.Inventory;
using Auraly.Application.Returns;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Returns;

namespace Auraly.Api;

public static class PosSalesReturnApi
{
    public static IEndpointRouteBuilder MapPosSalesReturnApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pos/v1/sales-returns")
            .RequireAuthorization("pos.enrolled");

        group.MapPost("/search", async (HttpContext context, PosReturnableSalesRequest request,
            SalesReturnQueryService service, CancellationToken token) =>
            await Execute(async () =>
            {
                var identity = Validate(context, request.Context,
                    Permissions(SalesReturnPermissionCodes.Create));
                return Results.Ok(await service.ListReturnableSalesAsync(identity, request.Query, token));
            }));

        group.MapPost("/sales/{documentId:guid}", async (HttpContext context, Guid documentId,
            PosSalesReturnContext request, SalesReturnQueryService service, CancellationToken token) =>
            await Execute(async () =>
            {
                var identity = Validate(context, request,
                    Permissions(SalesReturnPermissionCodes.Create));
                var value = await service.GetReturnableSaleAsync(identity, documentId, token);
                return value is null ? Results.NotFound() : Results.Ok(value);
            }));

        group.MapPost("/bootstrap", async (HttpContext context, PosSalesReturnContext request,
            InventoryQueryService inventory,
            ReferenceOptionService references, AccountingService accounting,
            CancellationToken token) => await Execute(async () =>
        {
            var identity = Validate(context, request,
                Permissions(SalesReturnPermissionCodes.Create));
            var inventoryIdentity = new InventoryUserIdentity(identity.UserId, identity.TenantId,
                identity.BusinessId, new HashSet<string>(StringComparer.Ordinal));
            var reasons = await inventory.GetSelectableReasonsAsync(inventoryIdentity, "SalesReturn", token);
            var resolutionMethods = await references.ListAsync("sales-return-resolution-method", token);
            var scopes = await references.ListAsync("sales-return-scope", token);
            var settlement = await accounting.GetPosSettlementConfigurationAsync(identity.TenantId, token);
            return Results.Ok(new PosSalesReturnBootstrap(reasons, resolutionMethods, scopes, settlement));
        }));

        group.MapPost("/confirm", async (HttpContext context, ConfirmSalesReturnRequest request,
            SalesReturnService service, CancellationToken token) =>
            await Execute(async () =>
            {
                var identity = Validate(context,
                    new PosSalesReturnContext(request.BusinessId, request.WorkSessionId),
                    Permissions(SalesReturnPermissionCodes.Create));
                var result = await service.ConfirmAsync(identity,
                    context.Request.Headers["Idempotency-Key"].ToString(), request, token);
                return Results.Accepted($"/api/commerce/v1/sales-returns/{result.ReturnId:D}", result);
            }));

        return endpoints;
    }

    private static SalesReturnUserIdentity Validate(HttpContext context,
        PosSalesReturnContext requested, IReadOnlySet<string> permissions)
    {
        if (requested.BusinessId == Guid.Empty || !requested.WorkSessionId.HasValue ||
            requested.WorkSessionId.Value == Guid.Empty ||
            !Guid.TryParse(context.Request.Headers["X-Auraly-User-Id"], out var userId))
            throw new SalesReturnForbiddenException(
                "El dispositivo no identificó el usuario o el negocio.");
        var device = context.User.ToPosDeviceIdentity();
        return new SalesReturnUserIdentity(userId, device.TenantId, requested.BusinessId, permissions);
    }

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SalesReturnForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (SalesReturnValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (SalesReturnConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
        catch (InventoryForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (InventoryValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
    }

    private static IReadOnlySet<string> Permissions(params string[] values) =>
        new HashSet<string>(values, StringComparer.Ordinal);
}

public sealed record PosSalesReturnContext(Guid BusinessId, Guid? WorkSessionId = null);
public sealed record PosReturnableSalesRequest(PosSalesReturnContext Context, ReturnableSalesQuery Query);
public sealed record PosSalesReturnBootstrap(IReadOnlyList<InventoryReasonItem> Reasons,
    IReadOnlyList<ReferenceOption> ResolutionMethods, IReadOnlyList<ReferenceOption> Scopes,
    PosAccountingSettlementConfiguration SettlementConfiguration);
