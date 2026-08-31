using System.Security.Claims;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using QRCoder;

namespace Auraly.Api;

public static class ServiceInvoiceApi
{
    public static IEndpointRouteBuilder MapServiceInvoiceApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/service-invoices")
            .RequireAuthorization();

        group.MapPost("/services/search", async (
            HttpContext context,
            ServiceInvoiceSearchRequest request,
            ServiceInvoiceWorkspaceService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchServicesAsync(
                context.User.ToServiceInvoiceUserIdentity(), request, ct)));

        group.MapPost("/customers/search", async (
            HttpContext context,
            ServiceInvoiceSearchRequest request,
            ServiceInvoiceWorkspaceService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchCustomersAsync(
                context.User.ToServiceInvoiceUserIdentity(), request, ct)));

        group.MapPost("/issue", async (
            HttpContext context,
            IssueServiceInvoiceRequest request,
            ServiceInvoiceWorkspaceService service,
            CancellationToken ct) =>
        {
            var key = context.Request.Headers["Idempotency-Key"].ToString();
            return await Handle(() => service.IssueAsync(
                context.User.ToServiceInvoiceUserIdentity(), request, key, ct));
        });

        group.MapPost("/history/search", async (
            HttpContext context,
            ServiceInvoiceHistoryRequest request,
            ServiceInvoiceWorkspaceService service,
            CancellationToken ct) =>
            await Handle(() => service.SearchInvoicesAsync(
                context.User.ToServiceInvoiceUserIdentity(), request, ct)));

        group.MapGet("/{documentId:guid}", async (
            HttpContext context,
            Guid documentId,
            Guid businessId,
            ServiceInvoiceWorkspaceService service,
            CancellationToken ct) =>
            await HandleNullable(() => service.GetInvoiceAsync(
                context.User.ToServiceInvoiceUserIdentity(), businessId,
                documentId, false, ct)));

        group.MapGet("/{documentId:guid}/print", async (
            HttpContext context,
            Guid documentId,
            Guid businessId,
            ServiceInvoiceWorkspaceService service,
            CancellationToken ct) =>
            await HandleNullable(() => service.GetInvoiceAsync(
                context.User.ToServiceInvoiceUserIdentity(), businessId,
                documentId, true, ct)));

        group.MapGet("/{documentId:guid}/qr", async (
            HttpContext context,
            Guid documentId,
            Guid businessId,
            ServiceInvoiceWorkspaceService service,
            CancellationToken ct) =>
        {
            try
            {
                var invoice = await service.GetInvoiceAsync(
                    context.User.ToServiceInvoiceUserIdentity(), businessId,
                    documentId, false, ct);
                if (invoice is null || string.IsNullOrWhiteSpace(invoice.QrPayload))
                    return Results.NotFound();
                using var data = QRCodeGenerator.GenerateQrCode(
                    invoice.QrPayload, QRCodeGenerator.ECCLevel.Q);
                using var qr = new SvgQRCode(data);
                return Results.Content(qr.GetGraphic(
                    pixelsPerModule: 4,
                    darkColorHex: "#061f22",
                    lightColorHex: "#ffffff",
                    drawQuietZones: true,
                    sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute),
                    "image/svg+xml; charset=utf-8");
            }
            catch (ServiceInvoiceForbiddenException exception)
            {
                return Results.Problem(exception.Message,
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (ServiceInvoiceValidationException exception)
            {
                return Results.Problem(exception.Message,
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        return endpoints;
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ServiceInvoiceForbiddenException exception)
        {
            return Results.Problem(exception.Message,
                statusCode: StatusCodes.Status403Forbidden);
        }
        catch (ServiceInvoiceIdempotencyException exception)
        {
            return Results.Problem(exception.Message,
                statusCode: StatusCodes.Status409Conflict,
                title: "ServiceInvoiceIdempotencyConflict");
        }
        catch (ServiceInvoiceValidationException exception)
        {
            return Results.Problem(exception.Message,
                statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> HandleNullable<T>(Func<Task<T?>> action)
        where T : class
    {
        var result = await Handle(action);
        return result is IValueHttpResult { Value: null }
            ? Results.NotFound()
            : result;
    }
}

public static class ServiceInvoiceClaimsPrincipalExtensions
{
    public static ServiceInvoiceUserIdentity ToServiceInvoiceUserIdentity(
        this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            principal.FindAll("permission").Select(value => value.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new ServiceInvoiceForbiddenException(
                $"La identidad autenticada no contiene '{claimType}'.");
}
