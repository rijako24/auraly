using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Auraly.Application.Inventory;
using Auraly.Application.Sales;
using Auraly.Contracts.Inventory;
using Auraly.Domain.Inventory;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public static class SellerOrdersApi
{
    public sealed record SellerOrderLineInput(
        Guid ProductId,
        decimal Quantity,
        decimal? UnitPrice = null,
        decimal DiscountAmount = 0m,
        string? PriceSource = null);
    public sealed record UpdateSellerOrderRequest(Guid CustomerId, string? Notes, string IdempotencyKey, IReadOnlyCollection<SellerOrderLineInput> Lines,
        Guid? WorkSessionId = null);
    public sealed record CreateSellerOrderRequest(Guid BusinessId, Guid WarehouseId, Guid CustomerId,
        Guid? PartySiteId, Guid? RouteId, Guid? RouteStopId, bool CapturedOffline, string? Notes,
        string IdempotencyKey, IReadOnlyCollection<SellerOrderLineInput> Lines);
    public sealed record SellerCatalogRequest(Guid BusinessId, Guid WarehouseId, Guid CustomerId,
        string? Search, int Skip = 0, int Take = 100);
    public sealed record SellerCatalogItem(Guid ProductId, string ProductCode, string Name, string UnitCode,
        decimal UnitPrice, string PriceSource, decimal QuantityOnHand, bool ManageStock);
    public sealed record SellerCatalogPage(IReadOnlyList<SellerCatalogItem> Items, bool HasMore, int? NextOffset);
    public sealed record SellerOrderResult(Guid OrderId, string OrderNumber, string Status,
        decimal Total, bool RequiresReview, IReadOnlyList<string> Warnings);

    public static IEndpointRouteBuilder MapSellerOrdersApi(this IEndpointRouteBuilder endpoints)
    {
        var group=endpoints.MapGroup("/api/commerce/v1/seller-orders").RequireAuthorization();
        group.MapPost("/catalog", async (ClaimsPrincipal principal, SellerCatalogRequest request,
            SellerOrderWriter writer, CancellationToken token) =>
            await Execute(() => writer.CatalogAsync(Actor(principal), request, token)));
        group.MapPost("", async (ClaimsPrincipal principal, CreateSellerOrderRequest request,
            SellerOrderWriter writer, CancellationToken token) =>
            await Execute(() => writer.CreateAsync(Actor(principal), request, token)));
        group.MapPut("/{orderId:guid}", async (ClaimsPrincipal principal, Guid orderId, UpdateSellerOrderRequest request,
            SellerOrderWriter writer, CancellationToken token) =>
            await Execute(() => writer.UpdateReviewAsync(Actor(principal), orderId, request, token)));
        return endpoints;
    }

    private static async Task<IResult> Execute<T>(Func<Task<T>> action)
    {
        try { return Results.Ok(await action()); }
        catch (SellerOrderForbiddenException error) { return Results.Problem(error.Message,statusCode:403); }
        catch (SellerOrderValidationException error) { return Results.Problem(error.Message,statusCode:400); }
        catch (SellerOrderConflictException error) { return Results.Problem(error.Message,statusCode:409); }
        catch (SqlException error) when (error.Number is >= 51300 and <= 51304)
        { return Results.Problem(error.Message,statusCode:error.Number==51300?400:409); }
    }

    private static SellerOrderActor Actor(ClaimsPrincipal principal) => new(
        Required(principal,ClaimTypes.NameIdentifier),Required(principal,"tenant_id"),Required(principal,"business_id"),
        principal.FindAll("permission").Select(value=>value.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid Required(ClaimsPrincipal principal,string type)=>Guid.TryParse(principal.FindFirstValue(type),out var value)
        ?value:throw new SellerOrderForbiddenException($"The authenticated identity lacks claim '{type}'.");
}

public sealed record SellerOrderActor(Guid UserId,Guid TenantId,Guid BusinessId,IReadOnlySet<string> Permissions);

public sealed class SellerOrderWriter(SqlServerConnectionFactory connections,SqlInventoryOperationStore inventory,
    SalesReportingProcessingCoordinator reporting,SqlSellerOrderReportingJobWriter reportingJobs)
{
    public async Task<SellerOrdersApi.SellerOrderResult> UpdateReviewAsync(SellerOrderActor actor, Guid orderId,
        SellerOrdersApi.UpdateSellerOrderRequest request,CancellationToken token)
    {
        var canFullyEdit=actor.Permissions.Contains("orders.update");
        var canResolveReview=actor.Permissions.Contains("orders.review");
        if(!canFullyEdit&&!canResolveReview)
            throw new SellerOrderForbiddenException("Permission 'orders.update' or 'orders.review' is required.");
        if(orderId==Guid.Empty||request.CustomerId==Guid.Empty||string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.Lines.Count is <1 or >500||request.Lines.Any(line=>line.ProductId==Guid.Empty||line.Quantity<=0||line.UnitPrice is null or <=0||line.DiscountAmount<0)||request.Notes?.Length>1000)
            throw new SellerOrderValidationException("El pedido requiere productos, cantidades y una clave de actualización válidos.");
        await using var connection=connections.Create();
        await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var editable=await SellerOrderReviewPersistence.FindEditableAsync(connection,transaction,orderId,actor.BusinessId,actor.UserId,request.WorkSessionId,token)
            ?? throw new SellerOrderConflictException("El pedido no existe, no te pertenece, ya fue facturado o no conserva su configuración de bodega.");
        if(editable.Status is not (2 or 5))throw new SellerOrderConflictException("Solo se puede editar un pedido disponible o en revisión que todavía no haya sido facturado.");
        var number=editable.Number;var customerId=request.CustomerId;var warehouseId=editable.WarehouseId;var ordersWarehouseId=editable.OrdersWarehouseId;
        var requested=NormalizeOrderLines(request.Lines);
        if(!canFullyEdit)
        {
            if(editable.Status!=5)
            {
                var unchanged=requested.Length==editable.Lines.Count&&requested.All(input=>
                    editable.Lines.TryGetValue(input.ProductId,out var original)&&input.Quantity==original.Quantity);
                if(!unchanged)
                    throw new SellerOrderConflictException("El permiso de revisión solo permite resolver pedidos con faltantes de inventario.");
                var replayTotal=editable.Lines.Values.Sum(line=>decimal.Round(line.Quantity*line.UnitPrice-line.DiscountAmount,2,MidpointRounding.AwayFromZero));
                await transaction.CommitAsync(token);
                return new(orderId,editable.Number,"Confirmed",replayTotal,false,[]);
            }
            if(requested.Any(input=>!editable.Lines.ContainsKey(input.ProductId)))
                throw new SellerOrderConflictException("Durante la revisión no se pueden agregar productos nuevos.");
            foreach(var original in editable.Lines.Values)
            {
                var submitted=requested.SingleOrDefault(input=>input.ProductId==original.ProductId);
                var hasShortage=original.ManageStock&&original.ReservedQuantity<original.Quantity;
                if(!hasShortage&&(submitted is null||submitted.Quantity!=original.Quantity))
                    throw new SellerOrderConflictException("Solo se pueden ajustar o eliminar los productos con existencia pendiente.");
                if(hasShortage&&submitted is not null&&submitted.Quantity>original.Quantity)
                    throw new SellerOrderConflictException("La revisión permite reducir la cantidad pendiente, no aumentarla.");
            }
        }
        var lines=new List<OrderLine>();var position=0;
        try
        {
            var context=await LoadContextAsync(connection,transaction,actor,actor.BusinessId,customerId,null,token);
            if(context.OrdersWarehouseId!=ordersWarehouseId)throw new SellerOrderConflictException("El cliente seleccionado no comparte la bodega de pedidos configurada para este pedido.");
            var factsInput=requested.Concat(editable.Lines.Values
                .Where(original=>requested.All(input=>input.ProductId!=original.ProductId))
                .Select(original=>new SellerOrdersApi.SellerOrderLineInput(
                    original.ProductId,original.Quantity,original.UnitPrice,
                    original.DiscountAmount,original.PriceSource)))
                .ToArray();
            var resolvedLines=await ResolveProductFactsAsync(connection,transaction,actor.BusinessId,warehouseId,customerId,factsInput,token);
            foreach(var input in requested)
            {
                var line=resolvedLines[input.ProductId];
                editable.Lines.TryGetValue(input.ProductId,out var original);
                var preserveCommercialValues=!canFullyEdit&&original is not null;
                var unitPrice=preserveCommercialValues?original!.UnitPrice:input.UnitPrice!.Value;
                var discountAmount=preserveCommercialValues?original!.DiscountAmount:input.DiscountAmount;
                var priceSource=preserveCommercialValues?original!.PriceSource:NormalizePriceSource(input.PriceSource);
                var gross=decimal.Round(unitPrice*input.Quantity,2,MidpointRounding.AwayFromZero);
                if(discountAmount>gross)throw new SellerOrderValidationException($"El descuento de {line.Code} supera el valor bruto de la línea.");
                lines.Add(line with{UnitPrice=unitPrice,PriceSource=priceSource,DiscountAmount=discountAmount,Position=++position,CanReserve=true});
            }
            var desiredDemand=InventoryDemandResolver.Resolve(lines.Select(line=>new InventoryDemandLine(
                line.ProductId,line.ProductId,line.InventoryProductId,line.InventoryFactor,
                line.Quantity,line.ManageStock))).ToDictionary(value=>value.InventoryProductId);
            var previousDemand=InventoryDemandResolver.Resolve(editable.Lines.Values
                .Where(line=>line.ReservedQuantity>0)
                .Select(line=>
                {
                    var fact=resolvedLines[line.ProductId];
                    return new InventoryDemandLine(
                        line.ProductId,line.ProductId,fact.InventoryProductId,fact.InventoryFactor,
                        line.ReservedQuantity,fact.ManageStock);
                })).ToDictionary(value=>value.InventoryProductId);
            foreach(var demand in desiredDemand.Values)
            {
                var previouslyReserved=previousDemand.GetValueOrDefault(demand.InventoryProductId)?.RequiredInventoryQuantity??0m;
                var additional=Math.Max(0m,demand.RequiredInventoryQuantity-previouslyReserved);
                var available=resolvedLines.Values.First(line=>line.InventoryProductId==demand.InventoryProductId).Available;
                if(available<additional)
                    throw new SellerOrderConflictException($"Inventario adicional requerido: {additional:N3}; disponible: {available:N3} en la unidad del producto padre.");
            }
            var total=lines.Sum(line=>line.LineTotal);
            var desired=lines.Where(line=>line.ManageStock).ToDictionary(line=>line.ProductId,line=>line.Quantity);
            var previous=editable.Lines.Values.Where(line=>line.ReservedQuantity>0).ToDictionary(line=>line.ProductId,line=>line.ReservedQuantity);
            var increases=desired.Select(pair=>new{pair.Key,Quantity=pair.Value-(previous.TryGetValue(pair.Key,out var prior)?prior:0)}).Where(line=>line.Quantity>0).ToArray();
            var releases=previous.Select(pair=>new{pair.Key,Quantity=pair.Value-(desired.TryGetValue(pair.Key,out var next)?next:0)}).Where(line=>line.Quantity>0).ToArray();
            var identity=new InventoryUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,new HashSet<string>{InventoryPermissionCodes.DispatchTransfer,"inventory.system-warehouses.use"});
            var key=request.IdempotencyKey.Trim();
            if(releases.Length>0)await TransferAsync(identity,$"seller-order-edit-release:{orderId:N}:{key}",DeterministicGuid($"seller-order-edit-release:{orderId:N}:{key}"),ordersWarehouseId,warehouseId,$"Liberación de reserva del pedido {number}",releases.Select(line=>(line.Key,line.Quantity)).ToArray(),connection,transaction,token);
            if(increases.Length>0)await TransferAsync(identity,$"seller-order-edit-increase:{orderId:N}:{key}",DeterministicGuid($"seller-order-edit-increase:{orderId:N}:{key}"),warehouseId,ordersWarehouseId,$"Aumento de reserva del pedido {number}",increases.Select(line=>(line.Key,line.Quantity)).ToArray(),connection,transaction,token);
            await SellerOrderReviewPersistence.ReplaceAsync(connection,transaction,orderId,actor.BusinessId,customerId,context.Name,context.Identification,context.Email,context.Phone,context.Address,request.Notes,total,DeterministicGuid($"seller-order-edit:{orderId:N}:{key}"),
                lines.Select(line=>new SellerOrderReplacementLine(line.ProductId,line.Code,line.Name,line.UnitCode,line.Quantity,line.UnitPrice,line.DiscountAmount,line.LineTotal,JsonSerializer.Serialize(new{line.PriceSource,Available=InventoryDemandResolver.InProductUnits(line.Available,line.InventoryFactor),ReservedQuantity=line.ManageStock?line.Quantity:0m}))).ToArray(),token);
            var reportingVersion=await reportingJobs.EnsureAsync(connection,transaction,actor.TenantId,actor.BusinessId,orderId,token);
            await transaction.CommitAsync(token);
            await reporting.RequestProjectionAsync(actor.BusinessId,orderId,"SellerOrder",token,reportingVersion);
            return new(orderId,number,"Confirmed",total,false,[]);
        }
        catch(Exception error)
        {
            if(transaction.Connection is not null)await transaction.RollbackAsync(CancellationToken.None);
            throw new SellerOrderConflictException($"No fue posible editar el pedido: {error.Message}");
        }
    }

    private async Task TransferAsync(InventoryUserIdentity identity,string idempotencyKey,Guid transferId,Guid source,Guid destination,string notes,IReadOnlyList<(Guid ProductId,decimal Quantity)> values,SqlConnection connection,SqlTransaction transaction,CancellationToken token)
    {var transferLines=values.Select((line,index)=>new WarehouseTransferLineRequest(index+1,line.ProductId,line.Quantity)).ToArray();await inventory.ConfirmSystemTransferAtomicallyAsync(identity,idempotencyKey,new DispatchWarehouseTransferRequest(transferId,identity.BusinessId,source,destination,DateTimeOffset.UtcNow,"WAREHOUSE_TRANSFER",notes,transferLines),connection,transaction,token);}

    public async Task<SellerOrdersApi.SellerCatalogPage> CatalogAsync(SellerOrderActor actor,
        SellerOrdersApi.SellerCatalogRequest request,CancellationToken token)
    {
        Demand(actor,"orders.create");
        if(request.BusinessId!=actor.BusinessId||request.WarehouseId==Guid.Empty||request.CustomerId==Guid.Empty)
            throw new SellerOrderValidationException("Sede, bodega y cliente son obligatorios.");
        var take=Math.Clamp(request.Take,1,500);var search=request.Search?.Trim()??string.Empty;
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted,token);
        await using var command=Procedure("dbo.SellerOrderCatalogGet",connection,transaction);
        command.Parameters.AddRange([P("@TenantId",actor.TenantId),P("@BusinessId",request.BusinessId),P("@WarehouseId",request.WarehouseId),P("@CustomerId",request.CustomerId),
            P("@Search",search),P("@Contains",$"%{search}%"),P("@Prefix",$"{search}%"),P("@Skip",request.Skip),P("@Take",take+1)]);
        var candidates=new List<SellerCatalogCandidate>();
        await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))candidates.Add(new(
            reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),
            reader.GetDecimal(4),reader.GetBoolean(5)));
        await reader.DisposeAsync();
        var more=candidates.Count>take;
        if(more)candidates.RemoveAt(candidates.Count-1);
        var prices=await SqlOnlineSalesDraftStore.ResolveCommercePricesAsync(
            connection,transaction,request.BusinessId,request.WarehouseId,request.CustomerId,
            candidates.Select(value=>new CommercePriceRequest(
                value.ProductId.ToString("D"),value.ProductId,1m)).ToArray(),token,
            independentLines:true);
        await transaction.CommitAsync(token);
        var values=candidates.Select(value=>
        {
            var price=prices[value.ProductId.ToString("D")];
            return new SellerOrdersApi.SellerCatalogItem(
                value.ProductId,value.ProductCode,value.Name,value.UnitCode,price.UnitPrice,
                price.PriceSource,value.QuantityOnHand,value.ManageStock);
        }).ToList();
        return new(values,more,more?request.Skip+values.Count:null);
    }

    public async Task<SellerOrdersApi.SellerOrderResult> CreateAsync(SellerOrderActor actor,
        SellerOrdersApi.CreateSellerOrderRequest request,CancellationToken token)
    {
        Demand(actor,"orders.create");
        Validate(actor,request);
        var orderId=DeterministicGuid($"seller-order:{actor.BusinessId:N}:{request.IdempotencyKey.Trim()}");
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var replay=await ReplayAsync(connection,transaction,actor.BusinessId,request.IdempotencyKey,token);
            if(replay is not null){var version=await reportingJobs.EnsureAsync(connection,transaction,actor.TenantId,actor.BusinessId,replay.OrderId,token);await transaction.CommitAsync(token);await reporting.RequestProjectionAsync(actor.BusinessId,replay.OrderId,"SellerOrder",token,version);return replay;}
            var context=await LoadContextAsync(connection,transaction,actor,request,token);
            var requested=NormalizeOrderLines(request.Lines);
            var lines=new List<OrderLine>();var warnings=new List<string>();var position=0;
            var resolvedLines=await ResolveProductFactsAsync(connection,transaction,request.BusinessId,request.WarehouseId,request.CustomerId,requested,token);
            var allocations=InventoryDemandResolver.AllocateWholeLines(
                requested.Select(input=>
                {
                    var fact=resolvedLines[input.ProductId];
                    return new InventoryDemandLine(
                        input.ProductId,input.ProductId,fact.InventoryProductId,
                        fact.InventoryFactor,input.Quantity,fact.ManageStock);
                }),
                resolvedLines.Values
                    .GroupBy(line=>line.InventoryProductId)
                    .ToDictionary(group=>group.Key,group=>group.First().Available));
            var allocationByProduct=allocations.ToDictionary(value=>value.Line.ProductId);
            var missingPrices=requested.Where(line=>line.UnitPrice is null).ToArray();
            var legacyPrices=missingPrices.Length==0
                ? new Dictionary<string,CommercePriceResolution>()
                : await SqlOnlineSalesDraftStore.ResolveCommercePricesAsync(
                    connection,transaction,request.BusinessId,request.WarehouseId,request.CustomerId,
                    missingPrices.Select(input=>new CommercePriceRequest(
                        input.ProductId.ToString("D"),input.ProductId,input.Quantity)).ToArray(),token);
            foreach(var input in requested)
            {
                var line=resolvedLines[input.ProductId];
                var legacyPrice=input.UnitPrice is null
                    ? legacyPrices[input.ProductId.ToString("D")]
                    : null;
                var unitPrice=input.UnitPrice??legacyPrice!.UnitPrice;
                var priceSource=input.UnitPrice is null
                    ? legacyPrice!.PriceSource
                    : NormalizePriceSource(input.PriceSource);
                var gross=decimal.Round(unitPrice*input.Quantity,2,MidpointRounding.AwayFromZero);
                if(input.DiscountAmount>gross)
                    throw new SellerOrderValidationException($"El descuento de {line.Code} supera el valor bruto de la línea.");
                if(input.UnitPrice is null)
                    warnings.Add($"{line.Code}: el cliente anterior no envió el precio capturado; revisa el valor antes de confirmar.");
                var allocation=allocationByProduct[input.ProductId];
                if(line.ManageStock&&!allocation.CanReserve)
                    warnings.Add($"{line.Code}: solicitadas {input.Quantity:N3}, disponibles {InventoryDemandResolver.InProductUnits(allocation.AvailableInventoryQuantity,line.InventoryFactor):N3}.");
                lines.Add(line with{UnitPrice=unitPrice,PriceSource=priceSource,DiscountAmount=input.DiscountAmount,Position=++position,CanReserve=allocation.CanReserve});
            }
            var review=warnings.Count>0;var number=$"PED-{DateTime.UtcNow:yyyyMMdd}-{orderId.ToString("N")[..8].ToUpperInvariant()}";
            var total=lines.Sum(line=>line.LineTotal);
            var reservationTransferId=DeterministicGuid($"seller-order-transfer:{orderId:N}");
            var reservableLines=lines.Where(line=>line.ManageStock&&line.CanReserve).ToArray();
            var persistedLines=lines.Select(line=>{var lineTotal=line.LineTotal;var tax=line.TaxRate<=0?0:decimal.Round(lineTotal*line.TaxRate/(100+line.TaxRate),2,MidpointRounding.AwayFromZero);var reservedQuantity=line.ManageStock&&line.CanReserve?line.Quantity:0m;return new{productId=line.ProductId,code=line.Code,name=line.Name,unitCode=line.UnitCode,quantity=line.Quantity,unitPrice=line.UnitPrice,discountAmount=line.DiscountAmount,taxAmount=tax,lineTotal,rawPayloadJson=JsonSerializer.Serialize(new{line.PriceSource,Available=InventoryDemandResolver.InProductUnits(line.Available,line.InventoryFactor),ReservedQuantity=reservedQuantity})};});
            await using(var insert=Procedure("dbo.SellerOrderCreate",connection,transaction))
            {insert.Parameters.AddRange([P("@OrderId",orderId),P("@BusinessId",request.BusinessId),P("@CustomerId",request.CustomerId),P("@WarehouseId",request.WarehouseId),P("@OrdersWarehouseId",context.OrdersWarehouseId),P("@ReservationTransferId",reservationTransferId),P("@RouteId",request.RouteId),P("@RouteStopId",request.RouteStopId),P("@PartySiteId",request.PartySiteId),P("@CapturedByUserId",actor.UserId),P("@CapturedOffline",request.CapturedOffline),P("@RequiresStockReview",review),P("@Status",review?5:3),P("@CustomerName",context.Name),P("@Email",context.Email),P("@Phone",context.Phone),P("@Identification",context.Identification),P("@Address",context.Address),P("@Notes",request.Notes),Money("@Total",total),P("@Number",number),P("@ExternalStatus",review?"StockReview":"InventoryTransferPending"),P("@IdempotencyKey",request.IdempotencyKey.Trim()),P("@LinesJson",JsonSerializer.Serialize(persistedLines))]);await insert.ExecuteNonQueryAsync(token);}
            var stockLines=reservableLines.Select((line,index)=>new WarehouseTransferLineRequest(index+1,line.ProductId,line.Quantity)).ToArray();
            if(stockLines.Length>0)
            {
                var identity=new InventoryUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,new HashSet<string>{InventoryPermissionCodes.DispatchTransfer,"inventory.system-warehouses.use"});
                await inventory.ConfirmSystemTransferAtomicallyAsync(identity,$"seller-order-reservation:{orderId:N}",new DispatchWarehouseTransferRequest(
                    reservationTransferId,request.BusinessId,request.WarehouseId,context.OrdersWarehouseId,
                    DateTimeOffset.UtcNow,"WAREHOUSE_TRANSFER",$"Reserva del pedido {number}",stockLines),connection,transaction,token);
            }
            if(review)
            {
                var reportingVersion=await reportingJobs.EnsureAsync(connection,transaction,actor.TenantId,actor.BusinessId,orderId,token);
                await transaction.CommitAsync(token);
                await reporting.RequestProjectionAsync(actor.BusinessId,orderId,"SellerOrder",token,reportingVersion);
                return new(orderId,number,"InReview",total,true,warnings);
            }

            await using(var confirm=Procedure("dbo.SellerOrderConfirm",connection,transaction))
            {confirm.Parameters.AddRange([P("@ExternalStatus",stockLines.Length>0?"InventoryTransferProcessed":"Confirmed"),P("@OrderId",orderId),P("@BusinessId",request.BusinessId)]);await confirm.ExecuteNonQueryAsync(token);}
            var finalReportingVersion=await reportingJobs.EnsureAsync(connection,transaction,actor.TenantId,actor.BusinessId,orderId,token);
            await transaction.CommitAsync(token);
            await reporting.RequestProjectionAsync(actor.BusinessId,orderId,"SellerOrder",token,finalReportingVersion);
            return new(orderId,number,"Confirmed",total,false,[]);
        }
        catch{if(transaction.Connection is not null)await transaction.RollbackAsync(token);throw;}
    }

    private static async Task<SellerOrdersApi.SellerOrderResult?> ReplayAsync(SqlConnection connection,SqlTransaction transaction,Guid businessId,string key,CancellationToken token)
    {await using var command=Procedure("dbo.SellerOrderReplay",connection,transaction);command.Parameters.AddRange([P("@BusinessId",businessId),P("@IdempotencyKey",key.Trim())]);await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;var review=reader.GetInt32(2)==5;return new(reader.GetGuid(0),reader.GetString(1),review?"InReview":"Confirmed",reader.GetDecimal(3),review,review?[reader.IsDBNull(4)?"Requiere revisión.":reader.GetString(4)]:[]);}

    private static Task<CustomerContext> LoadContextAsync(SqlConnection connection,SqlTransaction transaction,SellerOrderActor actor,SellerOrdersApi.CreateSellerOrderRequest request,CancellationToken token)
        =>LoadContextAsync(connection,transaction,actor,request.BusinessId,request.CustomerId,request.PartySiteId,token);
    private static async Task<CustomerContext> LoadContextAsync(SqlConnection connection,SqlTransaction transaction,SellerOrderActor actor,Guid businessId,Guid customerId,Guid? partySiteId,CancellationToken token)
    {await using var command=Procedure("dbo.SellerOrderContextGet",connection,transaction);command.Parameters.AddRange([P("@SiteId",partySiteId),P("@BusinessId",businessId),P("@TenantId",actor.TenantId),P("@CustomerId",customerId)]);await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new SellerOrderValidationException("El cliente, su sede o la bodega de pedidos no están disponibles.");return new(reader.GetString(0),reader.IsDBNull(1)?null:reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.GetString(4),reader.GetGuid(5));}

    private static async Task<IReadOnlyDictionary<Guid,OrderLine>> ResolveProductFactsAsync(
        SqlConnection connection,SqlTransaction transaction,Guid businessId,Guid warehouseId,
        Guid customerId,IReadOnlyCollection<SellerOrdersApi.SellerOrderLineInput> inputs,
        CancellationToken token)
    {
        var lines=new Dictionary<Guid,OrderLine>();
        var inputByProductId=inputs.ToDictionary(input=>input.ProductId);
        await using(var command=Procedure("dbo.SellerOrderProductResolve",connection,transaction))
        {
            command.Parameters.AddRange([
                P("@BusinessId",businessId),P("@WarehouseId",warehouseId),P("@CustomerId",customerId),
                P("@LinesJson",JsonSerializer.Serialize(inputs.Select(input=>new{productId=input.ProductId,quantity=input.Quantity}))) ]);
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))
            {
                var productId=reader.GetGuid(0);
                var input=inputByProductId[productId];
                lines.Add(productId,new(
                    productId,reader.GetString(1),reader.GetString(2),reader.GetString(3),input.Quantity,0,
                    "Captured",reader.GetDecimal(4),reader.GetBoolean(5),reader.GetDecimal(6),0,
                    0m,reader.GetGuid(7),reader.GetDecimal(8)));
            }
        }
        var unresolved=inputs.Where(input=>!lines.ContainsKey(input.ProductId)).Select(input=>input.ProductId).ToArray();
        if(unresolved.Length>0)
            throw new SellerOrderValidationException(
                $"Los productos {string.Join(", ",unresolved.Select(value=>value.ToString("D")))} no están activos o no tienen precio publicado.");
        return lines;
    }
    private static SellerOrdersApi.SellerOrderLineInput[] NormalizeOrderLines(IReadOnlyCollection<SellerOrdersApi.SellerOrderLineInput> lines)
    {
        var result=new List<SellerOrdersApi.SellerOrderLineInput>();
        foreach(var group in lines.GroupBy(line=>line.ProductId))
        {
            var prices=group.Where(line=>line.UnitPrice.HasValue).Select(line=>line.UnitPrice!.Value).Distinct().ToArray();
            if(prices.Length>1)throw new SellerOrderValidationException("Un mismo producto no puede guardarse con precios unitarios diferentes.");
            var priceSources=group.Select(line=>NormalizePriceSource(line.PriceSource)).Distinct(StringComparer.Ordinal).ToArray();
            result.Add(new(group.Key,group.Sum(line=>line.Quantity),prices.Length==0?null:prices[0],group.Sum(line=>line.DiscountAmount),priceSources.Length==1?priceSources[0]:"Captured"));
        }
        return result.ToArray();
    }
    private static string NormalizePriceSource(string? value)=>value switch
    {
        "Base" or "Public" or "PriceChannel" or "Promotion" or "Promotion+PriceChannel" or "Manual"=>value,
        _=>"Captured"
    };
    private static void Validate(SellerOrderActor actor,SellerOrdersApi.CreateSellerOrderRequest request){if(request.BusinessId!=actor.BusinessId||request.WarehouseId==Guid.Empty||request.CustomerId==Guid.Empty)throw new SellerOrderValidationException("Sede, bodega y cliente son obligatorios.");if(string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.IdempotencyKey.Trim().Length>160)throw new SellerOrderValidationException("La clave idempotente es obligatoria.");if(request.Lines.Count is <1 or >500||request.Lines.Any(line=>line.ProductId==Guid.Empty||line.Quantity<=0))throw new SellerOrderValidationException("El pedido requiere productos y cantidades válidas.");if(request.Notes?.Length>1000)throw new SellerOrderValidationException("Las notas superan 1000 caracteres.");}
    private static void Demand(SellerOrderActor actor,string permission){if(!actor.Permissions.Contains(permission))throw new SellerOrderForbiddenException($"Permission '{permission}' is required.");}
    public static Guid DeterministicDocumentId(string value)=>DeterministicGuid(value);
    private static Guid DeterministicGuid(string value){var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));return new Guid(hash.AsSpan(0,16));}
    private static SqlParameter P(string name,object? value)=>new(name,value??DBNull.Value);
    private static SqlParameter Money(string name,decimal value)=>new(name,SqlDbType.Decimal){Precision=19,Scale=4,Value=value};
    private static SqlCommand Procedure(string name,SqlConnection connection,SqlTransaction? transaction=null)=>new(name,connection,transaction){CommandType=CommandType.StoredProcedure};
    private sealed record CustomerContext(string Name,string? Identification,string? Email,string? Phone,string Address,Guid OrdersWarehouseId);
    private sealed record OrderLine(Guid ProductId,string Code,string Name,string UnitCode,decimal Quantity,decimal UnitPrice,string PriceSource,decimal Available,bool ManageStock,decimal TaxRate,int Position,decimal DiscountAmount=0m,Guid InventoryProductId=default,decimal InventoryFactor=1m,bool CanReserve=false)
    {
        public decimal LineTotal=>decimal.Round(UnitPrice*Quantity-DiscountAmount,2,MidpointRounding.AwayFromZero);
    }
    private sealed record SellerCatalogCandidate(
        Guid ProductId,string ProductCode,string Name,string UnitCode,
        decimal QuantityOnHand,bool ManageStock);

}

public sealed class SellerOrderForbiddenException(string message):Exception(message);
public sealed class SellerOrderValidationException(string message):Exception(message);
public sealed class SellerOrderConflictException(string message):Exception(message);
