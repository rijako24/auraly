using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;

namespace Auraly.Api;

public static class FinancialConceptsApi
{
    public static IEndpointRouteBuilder MapFinancialConceptsApi(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/commerce/v1/financial-concepts").RequireAuthorization();
        group.MapGet("/",async(HttpContext context,[AsParameters] FinancialConceptQuery query,FinancialConceptService service,CancellationToken token)=>
        {
            try{return Results.Ok(await service.ListAsync(context.User.ToCatalogUserIdentity(),query,token));}
            catch(CatalogForbiddenException error){return Results.Problem(error.Message,statusCode:403);}
            catch(CatalogValidationException error){return Results.Problem(error.Message,statusCode:400);}
        });
        group.MapPut("/{conceptId:guid}",async(HttpContext context,Guid conceptId,SaveFinancialConceptRequest request,
            [AsParameters] FinancialConceptQuery query,FinancialConceptService service,CancellationToken token)=>
        {
            if(conceptId!=request.FinancialConceptId)return Results.Problem("La identidad no coincide.",statusCode:400);
            try{return Results.Ok(await service.SaveAsync(context.User.ToCatalogUserIdentity(),request,query,token));}
            catch(CatalogForbiddenException error){return Results.Problem(error.Message,statusCode:403);}
            catch(CatalogValidationException error){return Results.Problem(error.Message,statusCode:400);}
            catch(CatalogConflictException error){return Results.Problem(error.Message,statusCode:409);}
        });
        return endpoints;
    }
}
