using System.Security.Claims;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;

namespace Auraly.Api;

public static class FiscalConfigurationApi
{
    public static IEndpointRouteBuilder MapFiscalConfigurationApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/fiscal/configuration")
            .RequireAuthorization("fiscal.user");

        group.MapGet("/", async (HttpContext context, Guid businessId,
            FiscalConfigurationService service, CancellationToken ct) =>
            await Handle(() => service.GetAsync(context.User.ToFiscalConfigurationUser(), businessId, ct)));

        group.MapPut("/", async (HttpContext context, Guid businessId,
            SaveFiscalResolutionConfiguration request,
            FiscalConfigurationService service, CancellationToken ct) =>
            await Handle(() => service.SaveAsync(
                context.User.ToFiscalConfigurationUser(), businessId, request, ct)));

        group.MapGet("/issuer", async (HttpContext context, Guid businessId,
            FiscalIssuerConnectionService service, CancellationToken ct) =>
            await Handle(() => service.GetAsync(
                context.User.ToFiscalConfigurationUser(), businessId, ct)));

        group.MapPut("/issuer", async (HttpContext context, Guid businessId,
            SaveFiscalIssuerConnectionConfiguration request,
            FiscalIssuerConnectionService service, CancellationToken ct) =>
            await Handle(() => service.SaveAsync(
                context.User.ToFiscalConfigurationUser(), businessId, request, ct)));

        var numbering = endpoints
            .MapGroup("/api/commerce/v1/fiscal/numbering")
            .RequireAuthorization("fiscal.user");
        numbering.MapGet("/", async (HttpContext context, Guid businessId,
            SalesInvoiceNumberingConfigurationService service,
            CancellationToken ct) =>
            await Handle(() => service.GetAsync(
                context.User.ToFiscalConfigurationUser(), businessId, ct)));
        numbering.MapPut("/", async (HttpContext context, Guid businessId,
            SaveSalesInvoiceNumberingConfiguration request,
            SalesInvoiceNumberingConfigurationService service,
            CancellationToken ct) =>
            await Handle(() => service.SaveAsync(
                context.User.ToFiscalConfigurationUser(), businessId, request, ct)));
        return endpoints;
    }

    private static async Task<IResult> Handle<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (FiscalConfigurationForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (FiscalConfigurationValidationException exception)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status400BadRequest); }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number is 51020 or 51021)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status403Forbidden); }
        catch (Microsoft.Data.SqlClient.SqlException exception) when (exception.Number == 51022)
        { return Results.Problem(exception.Message, statusCode: StatusCodes.Status409Conflict); }
    }

    private static FiscalConfigurationUser ToFiscalConfigurationUser(this ClaimsPrincipal principal)
    {
        var userId = Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var user)
            ? user : throw new FiscalConfigurationForbiddenException("La identidad no contiene un usuario válido.");
        var tenantId = Guid.TryParse(principal.FindFirstValue("tenant_id"), out var tenant)
            ? tenant : throw new FiscalConfigurationForbiddenException("La identidad no contiene una empresa válida.");
        return new FiscalConfigurationUser(userId, tenantId,
            principal.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.Ordinal));
    }
}
