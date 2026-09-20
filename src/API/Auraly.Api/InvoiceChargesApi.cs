using System.Security.Claims;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;

namespace Auraly.Api;

public static class InvoiceChargesApi
{
    public static IEndpointRouteBuilder MapInvoiceChargesApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/invoice-charges").RequireAuthorization("pos.user");
        endpoints.MapGet("/api/pos/v1/invoice-charges", (HttpContext context, InvoiceChargeService service,
            Guid businessId, int? page, long throughCursor, CancellationToken ct) =>
            Execute(() => service.DeviceOptionsAsync(context.User.ToPosDeviceIdentity(), businessId,
                page ?? 1, throughCursor, ct))).RequireAuthorization("pos.enrolled");
        endpoints.MapGet("/api/commerce/v1/pos/invoice-charges", (HttpContext context, InvoiceChargeService service,
            int? page, CancellationToken ct) =>
            Execute(() => service.PosOptionsAsync(Actor(context.User), page ?? 1, ct))).RequireAuthorization("pos.user");
        group.MapGet("/history", (HttpContext context, InvoiceChargeService service, DateOnly from, DateOnly to,
            int? page, int? pageSize, string? search, CancellationToken ct) => Execute(() =>
                service.HistoryAsync(Actor(context.User), from, to, page ?? 1, pageSize ?? 25, search, ct)));
        group.MapGet("/options", (HttpContext context, InvoiceChargeService service, CancellationToken ct) =>
            Execute(() => service.OptionsAsync(Actor(context.User), ct)));
        group.MapGet("", (HttpContext context, InvoiceChargeService service, int? page,
            int? pageSize, string? search, bool? includeInactive, CancellationToken ct) =>
            Execute(() => service.ListAsync(Actor(context.User), page ?? 1, pageSize ?? 25,
                search, includeInactive ?? false, ct)));
        group.MapPut("/{id:guid}", (HttpContext context, InvoiceChargeService service, Guid id,
            SaveInvoiceChargeRequest request, CancellationToken ct) =>
            id != request.ChargeId ? Task.FromResult<IResult>(Results.BadRequest()) :
                Execute(() => service.SaveAsync(Actor(context.User), request, ct)));
        return endpoints;
    }

    private static InvoiceChargeActor Actor(ClaimsPrincipal principal) => new(
        Required(principal, "tenant_id"), Required(principal, "business_id"),
        Required(principal, ClaimTypes.NameIdentifier), principal.FindAll("permission")
            .Select(x => x.Value).ToHashSet(StringComparer.Ordinal));

    private static Guid Required(ClaimsPrincipal principal, string name) =>
        Guid.TryParse(principal.FindFirstValue(name), out var value) && value != Guid.Empty
            ? value : throw new InvoiceChargeForbiddenException("Selecciona una sede autorizada.");

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (InvoiceChargeForbiddenException error) { return Results.Problem(error.Message, statusCode: 403); }
        catch (InvoiceChargeValidationException error) { return Results.Problem(error.Message, statusCode: 400); }
        catch (InvoiceChargeConflictException error) { return Results.Problem(error.Message, statusCode: 409); }
        catch (Auraly.Application.Catalog.CatalogForbiddenException error) { return Results.Problem(error.Message, statusCode: 403); }
    }
}
