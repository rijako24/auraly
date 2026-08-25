using System.Security.Claims;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;

namespace Auraly.Api;

public static class SalesReportingApi
{
    public static IEndpointRouteBuilder MapSalesReportingApi(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/commerce/v1/sales-reports")
            .RequireAuthorization("sales-reporting.user");
        group.MapGet("/today",async(HttpContext context,SalesReportingService service,
            CancellationToken token)=>await Execute(
                ()=>service.GetTodayAsync(Identity(context.User),token),Results.Ok));
        group.MapGet("/summary",async(HttpContext context,DateOnly from,DateOnly to,
            DateOnly? comparisonFrom,DateOnly? comparisonTo,Guid? customerId,Guid? sellerId,
            Guid? supplierId,Guid? productId,Guid? categoryId,Guid? warehouseId,string? documentType,
            SalesReportingService service,CancellationToken token)=>await Execute(()=>service.GetSummaryAsync(
                Identity(context.User),Filter(from,to,customerId,sellerId,supplierId,productId,categoryId,warehouseId,documentType),
                comparisonFrom,comparisonTo,token),Results.Ok));
        group.MapGet("/breakdown",async(HttpContext context,DateOnly from,DateOnly to,string dimension,int? limit,
            Guid? customerId,Guid? sellerId,Guid? supplierId,Guid? productId,Guid? categoryId,
            Guid? warehouseId,string? documentType,SalesReportingService service,CancellationToken token)=>
            await Execute(()=>service.GetBreakdownAsync(Identity(context.User),
                Filter(from,to,customerId,sellerId,supplierId,productId,categoryId,warehouseId,documentType),
                dimension,limit??50,token),Results.Ok));
        group.MapGet("/documents",async(HttpContext context,DateOnly from,DateOnly to,int? page,int? pageSize,
            string? search,Guid? customerId,Guid? sellerId,Guid? supplierId,Guid? productId,
            Guid? categoryId,Guid? warehouseId,string? documentType,SalesReportingService service,CancellationToken token)=>
            await Execute(()=>service.ListDocumentsAsync(Identity(context.User),
                Filter(from,to,customerId,sellerId,supplierId,productId,categoryId,warehouseId,documentType),
                page??1,pageSize??50,search,token),Results.Ok));
        group.MapGet("/documents/{documentId:guid}",async(HttpContext context,Guid documentId,
            SalesReportingService service,CancellationToken token)=>await Execute(async()=>
            {
                var value=await service.GetDocumentAsync(Identity(context.User),documentId,token);
                return value is null?Results.NotFound():Results.Ok(value);
            }));
        group.MapGet("/visits",async(HttpContext context,DateOnly from,DateOnly to,Guid? sellerId,
            Guid? routeId,string? status,bool? hasOrder,int? page,int? pageSize,
            SalesReportingService service,CancellationToken token)=>await Execute(
                ()=>service.ListVisitsAsync(Identity(context.User),from,to,sellerId,routeId,status,
                    hasOrder,page??1,pageSize??50,token),Results.Ok));
        group.MapGet("/seller-orders",async(HttpContext context,DateOnly from,DateOnly to,
            SalesReportingService service,CancellationToken token)=>await Execute(
                ()=>service.ListSellerOrdersAsync(Identity(context.User),from,to,token),Results.Ok));
        group.MapGet("/seller-performance",async(HttpContext context,DateOnly from,DateOnly to,
            SalesReportingService service,CancellationToken token)=>await Execute(
                ()=>service.GetSellerPerformanceAsync(Identity(context.User),from,to,token),Results.Ok));
        group.MapGet("/coverage",async(HttpContext context,DateOnly from,DateOnly to,
            SalesReportingService service,CancellationToken token)=>await Execute(
                ()=>service.GetCoverageAsync(Identity(context.User),from,to,token),Results.Ok));
        group.MapGet("/supplier-impact",async(HttpContext context,DateOnly from,DateOnly to,
            SalesReportingService service,CancellationToken token)=>await Execute(
                ()=>service.GetSupplierImpactAsync(Identity(context.User),from,to,token),Results.Ok));
        return endpoints;
    }

    private static SalesReportFilter Filter(DateOnly from,DateOnly to,Guid? customerId,Guid? sellerId,
        Guid? supplierId,Guid? productId,Guid? categoryId,Guid? warehouseId,string? documentType)=>
        new(from,to,customerId,sellerId,supplierId,productId,categoryId,warehouseId,documentType);
    private static SalesReportingUserIdentity Identity(ClaimsPrincipal principal)=>new(
        Required(principal,ClaimTypes.NameIdentifier),Required(principal,"tenant_id"),Required(principal,"business_id"),
        principal.FindAll("permission").Concat(principal.FindAll(PosAuthenticationDefaults.PermissionClaim))
            .Select(x=>x.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid Required(ClaimsPrincipal principal,string type)=>
        Guid.TryParse(principal.FindFirstValue(type),out var id)?id:
            throw new SalesReportingForbiddenException($"The authenticated identity lacks claim '{type}'.");
    private static async Task<IResult> Execute<T>(Func<Task<T>> action,Func<T,IResult> success)
    {try{return success(await action());}catch(SalesReportingForbiddenException e){return Results.Problem(e.Message,statusCode:403);}catch(SalesReportingValidationException e){return Results.Problem(e.Message,statusCode:400);}}
    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {try{return await action();}catch(SalesReportingForbiddenException e){return Results.Problem(e.Message,statusCode:403);}catch(SalesReportingValidationException e){return Results.Problem(e.Message,statusCode:400);}}
}
