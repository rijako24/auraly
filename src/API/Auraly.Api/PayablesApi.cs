using System.Security.Claims;
using Auraly.Application.Payables;
using Auraly.Contracts.Payables;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Auraly.Platform.Application.Identity.Interfaces;

namespace Auraly.Api;

public static class PayablesApi
{
    public static IEndpointRouteBuilder MapPayablesApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/payables/expense-concepts",
                async (HttpContext context, string? search, int page, int pageSize,
                    PayablesService service, CancellationToken cancellationToken) =>
                    await ExecuteAsync(() => service.ListExpenseConceptsAsync(
                        context.User.ToPayablesIdentity(), search, page, pageSize, cancellationToken), Results.Ok))
            .RequireAuthorization("payables.user");
        endpoints.MapGet(
                "/api/commerce/v1/payables/suppliers",
                async (HttpContext context,int page,int pageSize,string? search,bool? overdue,
                    Guid? supplierId,string? status,DateOnly? from,DateOnly? to,
                    PayablesService service,CancellationToken cancellationToken) =>
                    await ExecuteAsync(() => service.ListSuppliersAsync(context.User.ToPayablesIdentity(),
                        new SupplierPortfolioQuery(page,pageSize,search,overdue,supplierId,status,from,to),cancellationToken),Results.Ok))
            .RequireAuthorization("payables.user");
        endpoints.MapGet(
                "/api/commerce/v1/payables",
                async (HttpContext context, int page, int pageSize, string? search,
                    Guid? supplierId, string? status, bool? overdue, bool? outstandingOnly,
                    DateOnly? from,DateOnly? to,Guid? conceptId,
                    PayablesService service, CancellationToken cancellationToken) =>
                    await ExecuteAsync(() => service.ListAsync(
                        context.User.ToPayablesIdentity(),
                        new PayableQuery(page, pageSize, search, supplierId, status, overdue, outstandingOnly==true,from,to,conceptId),
                        cancellationToken), Results.Ok))
            .RequireAuthorization("payables.user");

        endpoints.MapGet(
                "/api/commerce/v1/payables/{payableId:guid}",
                async (HttpContext context, Guid payableId, PayablesService service,
                    CancellationToken cancellationToken) =>
                    await ExecuteAsync(async () =>
                    {
                        var value = await service.GetAsync(
                            context.User.ToPayablesIdentity(), payableId, cancellationToken);
                        return value is null ? Results.NotFound() : Results.Ok(value);
                    }))
            .RequireAuthorization("payables.user");

        endpoints.MapGet(
                "/api/commerce/v1/suppliers/{supplierId:guid}/payments",
                async (HttpContext context, Guid supplierId, int? page, int? pageSize,
                    PayablesService service, CancellationToken cancellationToken) =>
                    await ExecuteAsync(() => service.PaymentHistoryAsync(
                        context.User.ToPayablesIdentity(), supplierId, page ?? 1, pageSize ?? 5,
                        cancellationToken), Results.Ok))
            .RequireAuthorization("payables.user");

        endpoints.MapGet(
                "/api/commerce/v1/payable-payments",
                async (HttpContext context,int page,int pageSize,string? search,Guid? supplierId,
                    string? status,bool? overdue,DateOnly? from,DateOnly? to,
                    PayablesService service,CancellationToken cancellationToken) =>
                    await ExecuteAsync(() => service.ListPaymentsAsync(context.User.ToPayablesIdentity(),
                        new SupplierPaymentHistoryQuery(page,pageSize,search,supplierId,status,overdue,from,to),cancellationToken),Results.Ok))
            .RequireAuthorization("payables.user");

        endpoints.MapPost(
                "/api/commerce/v1/payable-payments/confirm",
                async (HttpContext context, ConfirmSupplierPaymentRequest request,
                    PayablesService service, CancellationToken cancellationToken) =>
                    await ExecuteAsync(async () =>
                    {
                        var key = context.Request.Headers["Idempotency-Key"].ToString();
                        var value = await service.ConfirmPaymentAsync(
                            context.User.ToPayablesIdentity(), key, request, cancellationToken);
                        return Results.Accepted(
                            $"/api/commerce/v1/payable-payments/{value.PaymentId:D}", value);
                    }))
            .RequireAuthorization("payables.user");
        var device=endpoints.MapGroup("/api/pos/v1").RequireAuthorization("pos.enrolled");
        device.MapGet("/payables",async(HttpContext context,int page,int pageSize,string? search,Guid? supplierId,string? status,bool? overdue,bool? outstandingOnly,PayablesService service,WorkSessionService sessions,IUserService users,CancellationToken token)=>
            await ExecuteAsync(async()=>
            {
                var actor=await PosPortfolioDeviceContext.RequireAsync(context,sessions,users,token);
                actor.RequirePermission(PayablesPermissionCodes.Read);
                return Results.Ok(await service.ListAsync(new PayablesUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,
                    actor.Permissions),
                    new PayableQuery(page,pageSize,search,supplierId,status,overdue,outstandingOnly==true),token));
            }));
        device.MapPost("/payable-payments/confirm",async(HttpContext context,ConfirmSupplierPaymentRequest request,PayablesService service,WorkSessionService sessions,IUserService users,CancellationToken token)=>
            await ExecuteAsync(async()=>
            {
                var actor=await PosPortfolioDeviceContext.RequireAsync(context,sessions,users,token);
                actor.RequirePermission(PayablesPermissionCodes.RegisterPayment);
                actor.RequirePayment(request.BusinessId,request.WorkSessionId);
                var value=await service.ConfirmPaymentAsync(new PayablesUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,
                    actor.Permissions),
                    context.Request.Headers["Idempotency-Key"].ToString(),request,token);
                return Results.Accepted($"/api/commerce/v1/payable-payments/{value.PaymentId:D}",value);
            }));
        return endpoints;
    }

    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (PayablesForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (WorkSessionForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (PayablesValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (PayablesConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
    }

    private static async Task<IResult> ExecuteAsync<T>(
        Func<Task<T>> action, Func<T, IResult> success)
    {
        try { return success(await action()); }
        catch (PayablesForbiddenException exception)
        { return Results.Problem(exception.Message, statusCode: 403); }
        catch (PayablesValidationException exception)
        { return Results.Problem(exception.Message, statusCode: 400); }
        catch (PayablesConflictException exception)
        { return Results.Problem(exception.Message, statusCode: 409); }
    }
}

public static class PayablesClaimsPrincipalExtensions
{
    public static PayablesUserIdentity ToPayablesIdentity(this ClaimsPrincipal principal) =>
        new(
            RequiredGuid(principal, ClaimTypes.NameIdentifier),
            RequiredGuid(principal, "tenant_id"),
            RequiredGuid(principal, "business_id"),
            principal.FindAll("permission")
                .Select(claim => claim.Value)
                .ToHashSet(StringComparer.Ordinal));

    private static Guid RequiredGuid(ClaimsPrincipal principal, string claimType) =>
        Guid.TryParse(principal.FindFirstValue(claimType), out var value)
            ? value
            : throw new PayablesForbiddenException(
                $"The authenticated identity lacks claim '{claimType}'.");
}
