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
        endpoints.MapGet("/api/commerce/v1/receivables/customers",async(HttpContext context,int page,int pageSize,string? search,bool? overdue,Guid? customerId,string? status,DateOnly? from,DateOnly? to,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListCustomersAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,overdue,customerId,status,from,to),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivables",async(HttpContext context,int page,int pageSize,string? search,Guid? customerId,string? status,bool? overdue,bool? outstandingOnly,DateOnly? from,DateOnly? to,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,customerId,status,overdue,OutstandingOnly:outstandingOnly==true,From:from,To:to),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivables/{receivableId:guid}",async(HttpContext context,Guid receivableId,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.GetAsync(context.User.ToReceivablesIdentity(),receivableId,token);return value is null?Results.NotFound():Results.Ok(value);})).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/customers/{customerId:guid}/credit",async(HttpContext context,Guid customerId,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.GetCreditProfileAsync(context.User.ToReceivablesIdentity(),customerId,token);return value is null?Results.NotFound():Results.Ok(value);})).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/customers/{customerId:guid}/payments",async(HttpContext context,Guid customerId,int? page,int? pageSize,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.PaymentHistoryAsync(context.User.ToReceivablesIdentity(),customerId,page??1,pageSize??5,token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivable-payments",async(HttpContext context,int page,int pageSize,string? search,Guid? customerId,string? status,bool? overdue,DateOnly? from,DateOnly? to,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListPaymentsAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,customerId,status,overdue,from,to),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapPut("/api/commerce/v1/customers/{customerId:guid}/credit",async(HttpContext context,Guid customerId,UpdateCustomerCreditProfileRequest request,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.UpdateCreditProfileAsync(context.User.ToReceivablesIdentity(),customerId,request,token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapPost("/api/commerce/v1/receivable-payments/confirm",async(HttpContext context,ConfirmCustomerPaymentRequest request,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.ConfirmPaymentAsync(context.User.ToReceivablesIdentity(),context.Request.Headers["Idempotency-Key"].ToString(),request,token);return Results.Accepted($"/api/commerce/v1/receivable-payments/{value.PaymentId:D}",value);})).RequireAuthorization("receivables.user");
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
                actor.RequirePermission(ReceivablesPermissionCodes.Read);
                return Results.Ok(await service.ListAsync(new ReceivablesUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,
                    actor.Permissions),
                    new(page,pageSize,search,customerId,status,overdue,OutstandingOnly:outstandingOnly==true),token));
            }));
        device.MapPost("/receivable-payments/confirm",async(HttpContext context,ConfirmCustomerPaymentRequest request,ReceivablesService service,WorkSessionService sessions,IUserService users,CancellationToken token)=>
            await Execute(async()=>
            {
                var actor=await PosPortfolioDeviceContext.RequireAsync(context,sessions,users,token);
                actor.RequirePermission(ReceivablesPermissionCodes.RegisterPayment);
                actor.RequirePayment(request.BusinessId,request.WorkSessionId);
                var value=await service.ConfirmPaymentAsync(new ReceivablesUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,
                    actor.Permissions),
                    context.Request.Headers["Idempotency-Key"].ToString(),request,token);
                return Results.Accepted($"/api/commerce/v1/receivable-payments/{value.PaymentId:D}",value);
            }));
        return endpoints;
    }
    private static async Task<IResult> Execute(Func<Task<IResult>> action){try{return await action();}catch(ReceivablesForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(WorkSessionForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(ReceivablesValidationException ex){return Results.Problem(ex.Message,statusCode:400);}catch(ReceivablesConflictException ex){return Results.Problem(ex.Message,statusCode:409);}}
    private static async Task<IResult> Execute<T>(Func<Task<T>> action,Func<T,IResult> success){try{return success(await action());}catch(ReceivablesForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(ReceivablesValidationException ex){return Results.Problem(ex.Message,statusCode:400);}catch(ReceivablesConflictException ex){return Results.Problem(ex.Message,statusCode:409);}}
}

public static class ReceivablesClaimsPrincipalExtensions
{
    public static ReceivablesUserIdentity ToReceivablesIdentity(this ClaimsPrincipal principal)=>new(Required(principal,ClaimTypes.NameIdentifier),Required(principal,"tenant_id"),Required(principal,"business_id"),principal.FindAll("permission").Select(x=>x.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid Required(ClaimsPrincipal principal,string type)=>Guid.TryParse(principal.FindFirstValue(type),out var value)?value:throw new ReceivablesForbiddenException($"The authenticated identity lacks claim '{type}'.");
}
