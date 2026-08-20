using Auraly.Application.Parties;
using Auraly.Contracts.Parties;

namespace Auraly.Api;

public static class PartyWorkspaceApi
{
    public static IEndpointRouteBuilder MapPartyWorkspaceApi(this IEndpointRouteBuilder endpoints)
    {
        var parties=endpoints.MapGroup("/api/commerce/v1/parties").RequireAuthorization("parties.user");
        parties.MapGet("/", async(HttpContext context,PartyWorkspaceService service,int? page,int? pageSize,
            string? search,string? role,bool? isActive,bool? isIncomplete,CancellationToken ct)=>
            await Handle(async()=>Results.Ok(await service.PageAsync(context.User.ToPartyUserIdentity(),page??1,
                new PartyWorkspaceQuery(pageSize??25,search,role,isActive,isIncomplete),ct))));
        parties.MapPost("/identity", async(
            HttpContext context,
            PartyWorkspaceService service,
            CreatePartyIdentityRequest request,
            CancellationToken ct) =>
            await Handle(async () => Results.Created("/api/commerce/v1/parties",
                await service.CreateIdentityAsync(context.User.ToPartyUserIdentity(), request, ct))));
        parties.MapGet("/customer-map", async(HttpContext context, PartyWorkspaceService service,
            string? search, Guid? routeId, Guid? sellerId, bool? onlyUnassigned, CancellationToken ct) =>
            await Handle(async() => Results.Ok(await service.CustomerMapAsync(context.User.ToPartyUserIdentity(),
                new CustomerMapQuery(search, routeId, sellerId, onlyUnassigned ?? false), ct))));
        parties.MapGet("/identity", async(
            HttpContext context,
            PartyWorkspaceService service,
            Guid countryId,
            string identificationTypeCode,
            string identification,
            string requestedRole,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.FindIdentityAsync(
                context.User.ToPartyUserIdentity(),
                countryId,
                identificationTypeCode,
                identification,
                requestedRole,
                ct))));
        parties.MapGet("/{partyId:guid}", async(
            HttpContext context,
            PartyWorkspaceService service,
            Guid partyId,
            CancellationToken ct) =>
            await Handle(async () => Results.Ok(await service.GetDetailAsync(
                context.User.ToPartyUserIdentity(), partyId, ct))));
        parties.MapPut("/{partyId:guid}", async(HttpContext context,PartyWorkspaceService service,Guid partyId,
            UpdatePartyRequest request,CancellationToken ct)=>
            await Handle(async()=>Results.Ok(await service.UpdateAsync(context.User.ToPartyUserIdentity(),partyId,request,ct))));
        parties.MapPost("/{partyId:guid}/status", async(HttpContext context,PartyWorkspaceService service,Guid partyId,
            SetPartyBusinessStatusRequest request,CancellationToken ct)=>
            await Handle(async()=>Results.Ok(await service.SetStatusAsync(context.User.ToPartyUserIdentity(),partyId,request,ct))));
        parties.MapPut("/{partyId:guid}/customer-billing", async(HttpContext context,PartyWorkspaceService service,Guid partyId,
            SaveCustomerBillingRequest request,CancellationToken ct)=>
            await Handle(async()=>Results.Ok(await service.SaveCustomerBillingAsync(context.User.ToPartyUserIdentity(),partyId,request,ct))));

        var suppliers=endpoints.MapGroup("/api/commerce/v1/suppliers").RequireAuthorization("parties.user");
        suppliers.MapPost("/", async(HttpContext context,PartyWorkspaceService service,CreateSupplierRequest request,CancellationToken ct)=>
            await Handle(async()=>Results.Created("/api/commerce/v1/parties",
                await service.CreateSupplierAsync(context.User.ToPartyUserIdentity(),request,ct))));
        var sellers=endpoints.MapGroup("/api/commerce/v1/sellers").RequireAuthorization("parties.user");
        sellers.MapPost("/", async(HttpContext context,CommercialPartyRoleService service,CreateSellerRequest request,CancellationToken ct)=>
            await Handle(async()=>Results.Created("/api/commerce/v1/parties",
                await service.CreateSellerAsync(context.User.ToPartyUserIdentity(),request,ct))));

        var carriers=endpoints.MapGroup("/api/commerce/v1/carriers").RequireAuthorization("parties.user");
        carriers.MapPost("/", async(HttpContext context,CommercialPartyRoleService service,CreateCarrierRequest request,CancellationToken ct)=>
            await Handle(async()=>Results.Created("/api/commerce/v1/parties",
                await service.CreateCarrierAsync(context.User.ToPartyUserIdentity(),request,ct))));

        parties.MapGet("/customer-pricing-options", async(HttpContext context,CommercialPartyRoleService service,CancellationToken ct)=>
            await Handle(async()=>Results.Ok(await service.PricingOptionsAsync(context.User.ToPartyUserIdentity(),ct))));
        return endpoints;
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try{return await action();}
        catch(PartyForbiddenException ex){return Results.Problem(ex.Message,statusCode:403);}
        catch(PartyValidationException ex){return Results.Problem(ex.Message,statusCode:400);}
        catch(PartyConflictException ex){return Results.Problem(ex.Message,statusCode:409,title:"PartyConflict");}
    }
}
