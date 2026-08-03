using System.Security.Claims;
using Auraly.Application.Receivables;
using Auraly.Contracts.Receivables;

namespace Auraly.Api;

public static class ReceivablesApi
{
    public static IEndpointRouteBuilder MapReceivablesApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/commerce/v1/receivables",async(HttpContext context,int page,int pageSize,string? search,Guid? customerId,string? status,bool? overdue,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.ListAsync(context.User.ToReceivablesIdentity(),new(page,pageSize,search,customerId,status,overdue),token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/receivables/{receivableId:guid}",async(HttpContext context,Guid receivableId,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.GetAsync(context.User.ToReceivablesIdentity(),receivableId,token);return value is null?Results.NotFound():Results.Ok(value);})).RequireAuthorization("receivables.user");
        endpoints.MapGet("/api/commerce/v1/customers/{customerId:guid}/credit",async(HttpContext context,Guid customerId,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.GetCreditProfileAsync(context.User.ToReceivablesIdentity(),customerId,token);return value is null?Results.NotFound():Results.Ok(value);})).RequireAuthorization("receivables.user");
        endpoints.MapPut("/api/commerce/v1/customers/{customerId:guid}/credit",async(HttpContext context,Guid customerId,UpdateCustomerCreditProfileRequest request,ReceivablesService service,CancellationToken token)=>
            await Execute(()=>service.UpdateCreditProfileAsync(context.User.ToReceivablesIdentity(),customerId,request,token),Results.Ok)).RequireAuthorization("receivables.user");
        endpoints.MapPost("/api/commerce/v1/receivable-payments/confirm",async(HttpContext context,ConfirmCustomerPaymentRequest request,ReceivablesService service,CancellationToken token)=>
            await Execute(async()=>{var value=await service.ConfirmPaymentAsync(context.User.ToReceivablesIdentity(),context.Request.Headers["Idempotency-Key"].ToString(),request,token);return Results.Accepted($"/api/commerce/v1/receivable-payments/{value.PaymentId:D}",value);})).RequireAuthorization("receivables.user");
        return endpoints;
    }
    private static async Task<IResult> Execute(Func<Task<IResult>> action){try{return await action();}catch(ReceivablesForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(ReceivablesValidationException ex){return Results.Problem(ex.Message,statusCode:400);}catch(ReceivablesConflictException ex){return Results.Problem(ex.Message,statusCode:409);}}
    private static async Task<IResult> Execute<T>(Func<Task<T>> action,Func<T,IResult> success){try{return success(await action());}catch(ReceivablesForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}catch(ReceivablesValidationException ex){return Results.Problem(ex.Message,statusCode:400);}catch(ReceivablesConflictException ex){return Results.Problem(ex.Message,statusCode:409);}}
}

public static class ReceivablesClaimsPrincipalExtensions
{
    public static ReceivablesUserIdentity ToReceivablesIdentity(this ClaimsPrincipal principal)=>new(Required(principal,ClaimTypes.NameIdentifier),Required(principal,"tenant_id"),Required(principal,"business_id"),principal.FindAll("permission").Select(x=>x.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid Required(ClaimsPrincipal principal,string type)=>Guid.TryParse(principal.FindFirstValue(type),out var value)?value:throw new ReceivablesForbiddenException($"The authenticated identity lacks claim '{type}'.");
}
