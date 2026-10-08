using System.Security.Claims;
using Auraly.Application.Receivables;
using Auraly.Contracts.Receivables;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Auraly.Platform.Application.Identity.Interfaces;

namespace Auraly.Api;

public static class ReceivablesApi
{
    public static IEndpointRouteBuilder MapReceivablesApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/receivables/report", async (
            HttpContext context, int page, int pageSize, bool consolidated, DateOnly cutoff,
            Guid? customerId, Guid? partySiteId, DateOnly? from, DateOnly? to,
            string? status, bool? outstandingOnly, bool? overdueOnly,
            string? sortBy, string? sortDirection, ReceivablesService service, CancellationToken token) =>
            await Execute(() => service.ReportAsync(context.User.ToReceivablesIdentity(),
                new(page, pageSize, consolidated, cutoff, customerId, partySiteId, from, to,
                    status, outstandingOnly == true, overdueOnly == true, sortBy, sortDirection), token), Results.Ok))
            .RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivables/report/print", async (
            HttpContext context, bool consolidated, DateOnly cutoff,
            Guid? customerId, Guid? partySiteId, DateOnly? from, DateOnly? to,
            string? status, bool? outstandingOnly, bool? overdueOnly,
            string? sortBy, string? sortDirection, ReceivablesService service, CancellationToken token) =>
            await Execute(async () =>
            {
                var html = await PortfolioReportPrint.ReceivablesAsync(
                    service, context.User.ToReceivablesIdentity(),
                    new(1, 100, consolidated, cutoff, customerId, partySiteId, from, to,
                        status, outstandingOnly == true, overdueOnly == true, sortBy, sortDirection), token);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Content(html, "text/html; charset=utf-8");
            })).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivables/customers",async(HttpContext context,int page,int pageSize,string? search,bool? overdue,Guid? customerId,Guid? partySiteId,string? status,DateOnly? from,DateOnly? to,string? sortBy,string? sortDirection,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListCustomersAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,overdue,customerId,status,from,to,partySiteId,sortBy,sortDirection),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivables",async(HttpContext context,int page,int pageSize,string? search,Guid? customerId,Guid? partySiteId,string? status,bool? overdue,bool? outstandingOnly,DateOnly? from,DateOnly? to,string? sortBy,string? sortDirection,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,customerId,status,overdue,partySiteId,OutstandingOnly:outstandingOnly==true,From:from,To:to,SortBy:sortBy,SortDirection:sortDirection),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivables/{receivableId:guid}",async(HttpContext context,Guid receivableId,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.GetAsync(context.User.ToReceivablesIdentity(),receivableId,token);return value is null?Results.NotFound():Results.Ok(value);})).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/customers/{customerId:guid}/credit",async(HttpContext context,Guid customerId,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.GetCreditProfileAsync(context.User.ToReceivablesIdentity(),customerId,token);return value is null?Results.NotFound():Results.Ok(value);})).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/customers/{customerId:guid}/payments",async(HttpContext context,Guid customerId,int? page,int? pageSize,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.PaymentHistoryAsync(context.User.ToReceivablesIdentity(),customerId,page??1,pageSize??5,token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivable-payments",async(HttpContext context,int page,int pageSize,string? search,Guid? customerId,Guid? partySiteId,string? status,bool? overdue,DateOnly? from,DateOnly? to,string? sortBy,string? sortDirection,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListPaymentsAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,customerId,status,overdue,from,to,partySiteId,sortBy,sortDirection),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapPut("/api/commerce/v1/customers/{customerId:guid}/credit",async(HttpContext context,Guid customerId,UpdateCustomerCreditProfileRequest request,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.UpdateCreditProfileAsync(context.User.ToReceivablesIdentity(),customerId,request,token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapPost("/api/commerce/v1/receivable-payments/confirm",async(HttpContext context,ConfirmCustomerPaymentRequest request,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.ConfirmPaymentAsync(context.User.ToReceivablesIdentity(),context.Request.Headers["Idempotency-Key"].ToString(),request,token);return Results.Accepted($"/api/commerce/v1/receivable-payments/{value.PaymentId:D}",value);})).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/pos/receivables",async(HttpContext context,int page,int pageSize,string? search,Guid? customerId,string? status,bool? overdue,bool? outstandingOnly,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListForPosAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,customerId,status,overdue,OutstandingOnly:outstandingOnly==true),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapPost("/api/commerce/v1/pos/receivable-payments/confirm",async(HttpContext context,ConfirmCustomerPaymentRequest request,ReceivablesService service,WorkSessionService sessions,CancellationToken token)=>
            await Execute(async()=>
            {
                var actor=context.User.ToReceivablesIdentity();
                if(!actor.Permissions.Contains(ReceivablesPermissionCodes.RegisterPosPayment))
                    throw new ReceivablesForbiddenException("El usuario no puede recibir abonos desde caja.");
                await sessions.RequireActiveWebSessionAsync(context.User.ToWorkSessionIdentity(),request.WorkSessionId,token);
                var value=await service.ConfirmPosPaymentAsync(actor,context.Request.Headers["Idempotency-Key"].ToString(),request,token);
                return Results.Accepted($"/api/commerce/v1/receivable-payments/{value.PaymentId:D}",value);
            })).RequireAuthorization("receivables.user");
        endpoints.MapPost("/api/commerce/v1/receivables/preexisting/import",async(HttpContext context,ImportPreexistingReceivablesRequest request,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>
            {
                var value=await service.ImportPreexistingAsync(context.User.ToReceivablesIdentity(),request,token);
                return Results.Accepted("/api/commerce/v1/receivables",value);
            })).RequireAuthorization("receivables.user");
        var device=endpoints.MapGroup("/api/pos/v1").RequireAuthorization("pos.enrolled");
        device.MapGet("/receivables",async(HttpContext context,int page,int pageSize,string? search,Guid? customerId,string? status,bool? overdue,bool? outstandingOnly,ReceivablesService service,WorkSessionService sessions,IUserService users,CancellationToken token)=>
            await Execute(async()=>
            {
                var actor=await PosPortfolioDeviceContext.RequireAsync(context,sessions,users,token);
                actor.RequirePermission(ReceivablesPermissionCodes.RegisterPosPayment);
                return Results.Ok(await service.ListForPosAsync(new ReceivablesUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,
                    actor.Permissions),
                    new(page,pageSize,search,customerId,status,overdue,OutstandingOnly:outstandingOnly==true),token));
            }));
        device.MapPost("/receivable-payments/confirm",async(HttpContext context,ConfirmCustomerPaymentRequest request,ReceivablesService service,WorkSessionService sessions,IUserService users,CancellationToken token)=>
            await Execute(async()=>
            {
                var actor=await PosPortfolioDeviceContext.RequireAsync(context,sessions,users,token);
                actor.RequirePermission(ReceivablesPermissionCodes.RegisterPosPayment);
                actor.RequirePayment(request.BusinessId,request.WorkSessionId);
                var value=await service.ConfirmPosPaymentAsync(new ReceivablesUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,
                    actor.Permissions),
                    context.Request.Headers["Idempotency-Key"].ToString(),request,token);
                return Results.Accepted($"/api/commerce/v1/receivable-payments/{value.PaymentId:D}",value);
            }));
        return endpoints;
    }
    private static async Task<IResult> Execute(Func<Task<IResult>> action){try{return await action();}catch(ReceivablesForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(WorkSessionForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(ReceivablesValidationException ex){return Results.Problem(ex.Message,statusCode:400);}catch(ReceivablesConflictException ex){return Results.Problem(ex.Message,statusCode:409);}catch(PortfolioReportPrintException ex){return Results.Problem(ex.Message,statusCode:ex.StatusCode);}}
    private static async Task<IResult> Execute<T>(Func<Task<T>> action,Func<T,IResult> success){try{return success(await action());}catch(ReceivablesForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(ReceivablesValidationException ex){return Results.Problem(ex.Message,statusCode:400);}catch(ReceivablesConflictException ex){return Results.Problem(ex.Message,statusCode:409);}}
}

public static class ReceivablesClaimsPrincipalExtensions
{
    public static ReceivablesUserIdentity ToReceivablesIdentity(this ClaimsPrincipal principal)=>new(Required(principal,ClaimTypes.NameIdentifier),Required(principal,"tenant_id"),Required(principal,"business_id"),principal.FindAll("permission").Select(x=>x.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid Required(ClaimsPrincipal principal,string type)=>Guid.TryParse(principal.FindFirstValue(type),out var value)?value:throw new ReceivablesForbiddenException($"The authenticated identity lacks claim '{type}'.");
}
