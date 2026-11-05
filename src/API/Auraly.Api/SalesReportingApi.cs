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
        group.MapGet("/context",async(HttpContext context,SalesReportingService service,CancellationToken token)=>
            await Execute(()=>service.GetContextAsync(Identity(context.User),token),Results.Ok));
        group.MapPost("/explore",async(HttpContext context,SalesReportExplorerRequest request,SalesReportingService service,CancellationToken token)=>
            await Execute(()=>service.ExploreAsync(Identity(context.User),request,token),Results.Ok));
        group.MapPost("/filter-options",async(HttpContext context,SalesReportFilterOptionsRequest request,SalesReportingService service,CancellationToken token)=>
            await Execute(()=>service.GetFilterOptionsAsync(Identity(context.User),request,token),Results.Ok));
        group.MapPost("/summary",async(HttpContext context,SalesReportSummaryRequest request,SalesReportingService service,CancellationToken token)=>
            await Execute(()=>service.GetSummaryAsync(Identity(context.User),request.Filter,request.ComparisonFrom,request.ComparisonTo,token),Results.Ok));
        group.MapPost("/breakdown",async(HttpContext context,SalesReportBreakdownRequest request,SalesReportingService service,CancellationToken token)=>
            await Execute(()=>service.GetBreakdownAsync(Identity(context.User),request.Filter,request.Dimension,request.Limit,token),Results.Ok));
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
        group.MapGet("/documents/{documentId:guid}",async(HttpContext context,Guid documentId,int? page,int? pageSize,
            SalesReportingService service,CancellationToken token)=>await Execute(async()=>
            {
                var value=await service.GetDocumentAsync(Identity(context.User),documentId,token,page??1,pageSize??200);
                return value is null?Results.NotFound():Results.Ok(value);
            }));
        group.MapGet("/supplier-impact",async(HttpContext context,DateOnly from,DateOnly to,int? page,int? pageSize,string? comparison,
            SalesReportingService service,CancellationToken token)=>await Execute(
                ()=>service.GetSupplierImpactAsync(Identity(context.User),from,to,token,page??1,pageSize??200,comparison??"none"),Results.Ok));
        return endpoints;
    }

    private static SalesReportFilter Filter(DateOnly from,DateOnly to,Guid? customerId,Guid? sellerId,
        Guid? supplierId,Guid? productId,Guid? categoryId,Guid? warehouseId,string? documentType)=>
        new(from,to,customerId,sellerId,supplierId,productId,categoryId,warehouseId,documentType);
    private static SalesReportingUserIdentity Identity(ClaimsPrincipal principal)=>new(
        Required(principal,ClaimTypes.NameIdentifier),Required(principal,"tenant_id"),Required(principal,"business_id"),
        principal.FindAll("permission")
            .Select(x=>x.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid Required(ClaimsPrincipal principal,string type)=>
        Guid.TryParse(principal.FindFirstValue(type),out var id)?id:
            throw new SalesReportingForbiddenException($"The authenticated identity lacks claim '{type}'.");
    private static async Task<IResult> Execute<T>(Func<Task<T>> action,Func<T,IResult> success)
    {try{return success(await action());}catch(SalesReportingForbiddenException e){return Results.Problem(e.Message,statusCode:403);}catch(SalesReportingValidationException e){return Results.Problem(e.Message,statusCode:400);}catch(SalesReportingUnavailableException e){return Results.Problem(e.Message,statusCode:503);}}
    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {try{return await action();}catch(SalesReportingForbiddenException e){return Results.Problem(e.Message,statusCode:403);}catch(SalesReportingValidationException e){return Results.Problem(e.Message,statusCode:400);}catch(SalesReportingUnavailableException e){return Results.Problem(e.Message,statusCode:503);}}
}
