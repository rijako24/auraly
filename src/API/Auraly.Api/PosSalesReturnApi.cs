using Auraly.Application.Catalog;
using Auraly.Application.Inventory;
using Auraly.Application.Returns;
using Auraly.Application.WorkSessions;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Returns;
using Auraly.Contracts.WorkSessions;

namespace Auraly.Api;

public static class PosSalesReturnApi
{
    public static IEndpointRouteBuilder MapPosSalesReturnApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/pos/v1/sales-returns")
            .RequireAuthorization("pos.enrolled");

        group.MapPost("/search", async (HttpContext context, PosReturnableSalesRequest request,
            IWorkSessionStore workSessions, SalesReturnQueryService service, CancellationToken token) =>
            await Execute(async () =>
            {
                var identity = await ValidateAsync(context, request.Context, workSessions,
                    Permissions(SalesReturnPermissionCodes.Read), token);
                return Results.Ok(await service.ListReturnableSalesAsync(identity, request.Query, token));
            }));

        group.MapPost("/sales/{documentId:guid}", async (HttpContext context, Guid documentId,
            PosSalesReturnContext request, IWorkSessionStore workSessions,
            SalesReturnQueryService service, CancellationToken token) =>
            await Execute(async () =>
            {
                var identity = await ValidateAsync(context, request, workSessions,
                    Permissions(SalesReturnPermissionCodes.Read), token);
                var value = await service.GetReturnableSaleAsync(identity, documentId, token);
                return value is null ? Results.NotFound() : Results.Ok(value);
            }));

        group.MapPost("/bootstrap", async (HttpContext context, PosSalesReturnContext request,
            IWorkSessionStore workSessions, InventoryQueryService inventory,
            ReferenceOptionService references, AccountingService accounting,
            CancellationToken token) => await Execute(async () =>
        {
            var identity = await ValidateAsync(context, request, workSessions,
                Permissions(SalesReturnPermissionCodes.Read), token);
            var inventoryIdentity = new InventoryUserIdentity(identity.UserId, identity.TenantId,
                identity.BusinessId, new HashSet<string>(StringComparer.Ordinal));
            var reasons = await inventory.GetSelectableReasonsAsync(inventoryIdentity, "SalesReturn", token);
            var resolutionMethods = await references.ListAsync("sales-return-resolution-method", token);
            var scopes = await references.ListAsync("sales-return-scope", token);
            var settlement = await accounting.GetPosSettlementConfigurationAsync(identity.TenantId, token);
            return Results.Ok(new PosSalesReturnBootstrap(reasons, resolutionMethods, scopes, settlement));
        }));

        group.MapPost("/confirm", async (HttpContext context, ConfirmSalesReturnRequest request,
            IWorkSessionStore workSessions, SalesReturnService service, CancellationToken token) =>
            await Execute(async () =>
            {
                if (request.WorkSessionId is not { } workSessionId || workSessionId == Guid.Empty)
                    throw new SalesReturnValidationException(
                        "La devolución de la caja requiere una sesión de trabajo activa.");
                var identity = await ValidateAsync(context,
                    new PosSalesReturnContext(request.BusinessId, workSessionId), workSessions,
                    Permissions(SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm), token);
                var result = await service.ConfirmAsync(identity,
                    context.Request.Headers["Idempotency-Key"].ToString(), request, token);
                return Results.Accepted($"/api/commerce/v1/sales-returns/{result.ReturnId:D}", result);
            }));

        return endpoints;
    }

    private static async Task<SalesReturnUserIdentity> ValidateAsync(HttpContext context,
        PosSalesReturnContext requested, IWorkSessionStore workSessions,
        IReadOnlySet<string> permissions, CancellationToken token)
    {
        if (requested.BusinessId == Guid.Empty || requested.WorkSessionId == Guid.Empty ||
            !Guid.TryParse(context.Request.Headers["X-Auraly-User-Id"], out var userId))
            throw new SalesReturnForbiddenException(
                "El dispositivo no identificó el usuario y su sesión de trabajo.");
        var device = context.User.ToPosDeviceIdentity();
        var current = await workSessions.CurrentForDeviceAsync(
            new WorkSessionIdentity(userId, device.TenantId,
                new HashSet<string>([WorkSessionPermissionCodes.Read], StringComparer.Ordinal),
                requested.BusinessId), requested.BusinessId, device.DeviceId, token);
        if (current is null || current.WorkSessionId != requested.WorkSessionId ||
            current.DeviceId != device.DeviceId || current.Status != "Open")
            throw new SalesReturnForbiddenException(
                "La sesión de trabajo no pertenece al equipo enrolado.");
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

public sealed record PosSalesReturnContext(Guid BusinessId, Guid WorkSessionId);
public sealed record PosReturnableSalesRequest(PosSalesReturnContext Context, ReturnableSalesQuery Query);
public sealed record PosSalesReturnBootstrap(IReadOnlyList<InventoryReasonItem> Reasons,
    IReadOnlyList<ReferenceOption> ResolutionMethods, IReadOnlyList<ReferenceOption> Scopes,
    PosAccountingSettlementConfiguration SettlementConfiguration);
