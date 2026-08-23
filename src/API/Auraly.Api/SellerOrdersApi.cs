using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public static class SellerOrdersApi
{
    public sealed record SellerOrderLineInput(
        Guid ProductId,
        decimal Quantity,
        decimal? UnitPrice = null,
        decimal DiscountAmount = 0m);
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

public sealed class SellerOrderWriter(SqlServerConnectionFactory connections,SqlInventoryOperationStore inventory)
{
    public async Task<SellerOrdersApi.SellerOrderResult> UpdateReviewAsync(SellerOrderActor actor, Guid orderId,
        SellerOrdersApi.UpdateSellerOrderRequest request,CancellationToken token)
    {
        Demand(actor,"orders.update");
        if(orderId==Guid.Empty||request.CustomerId==Guid.Empty||string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.Lines.Count is <1 or >500||request.Lines.Any(line=>line.ProductId==Guid.Empty||line.Quantity<=0||line.UnitPrice is <=0||line.DiscountAmount<0)||request.Notes?.Length>1000)
            throw new SellerOrderValidationException("El pedido requiere productos, cantidades y una clave de actualización válidos.");
        var editable=await SellerOrderReviewPersistence.FindEditableAsync(connections,orderId,actor.BusinessId,actor.UserId,request.WorkSessionId,token)
            ?? throw new SellerOrderConflictException("El pedido no existe, no te pertenece, ya fue facturado o no conserva su configuración de bodega.");
        if(editable.Status is not (2 or 5))throw new SellerOrderConflictException("Solo se puede editar un pedido disponible o en revisión que todavía no haya sido facturado.");
        var number=editable.Number;var customerId=request.CustomerId;var warehouseId=editable.WarehouseId;var ordersWarehouseId=editable.OrdersWarehouseId;
        var requested=NormalizeUpdateLines(request.Lines);
        var lines=new List<OrderLine>();var position=0;
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        try
        {
            var context=await LoadContextAsync(connection,transaction,actor,actor.BusinessId,customerId,null,token);
            if(context.OrdersWarehouseId!=ordersWarehouseId)throw new SellerOrderConflictException("El cliente seleccionado no comparte la bodega de pedidos configurada para este pedido.");
            foreach(var input in requested){var line=await ResolveLineAsync(connection,transaction,actor.BusinessId,warehouseId,customerId,input,token);if(line is null)throw new SellerOrderValidationException($"El producto {input.ProductId:D} no está activo o no tiene precio publicado.");var unitPrice=input.UnitPrice??line.UnitPrice;var gross=decimal.Round(unitPrice*input.Quantity,2,MidpointRounding.AwayFromZero);if(input.DiscountAmount>gross)throw new SellerOrderValidationException($"El descuento de {line.Code} supera el valor bruto de la línea.");var alreadyReserved=editable.Status==2&&editable.ReservedQuantities.TryGetValue(input.ProductId,out var prior)?prior:0;var additional=Math.Max(0,input.Quantity-alreadyReserved);if(line.ManageStock&&line.Available<additional)throw new SellerOrderConflictException($"{line.Code}: adicionales {additional:N3}, disponibles {line.Available:N3}.");lines.Add(line with{UnitPrice=unitPrice,DiscountAmount=input.DiscountAmount,Position=++position});}
            var total=lines.Sum(line=>line.LineTotal);
            var desired=lines.Where(line=>line.ManageStock).ToDictionary(line=>line.ProductId,line=>line.Quantity);
            var previous=editable.Status==2?editable.ReservedQuantities:new Dictionary<Guid,decimal>();
            var increases=desired.Select(pair=>new{pair.Key,Quantity=pair.Value-(previous.TryGetValue(pair.Key,out var prior)?prior:0)}).Where(line=>line.Quantity>0).ToArray();
            var releases=previous.Select(pair=>new{pair.Key,Quantity=pair.Value-(desired.TryGetValue(pair.Key,out var next)?next:0)}).Where(line=>line.Quantity>0).ToArray();
            var identity=new InventoryUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,new HashSet<string>{InventoryPermissionCodes.Transfer,"inventory.system-warehouses.use"});
            var key=request.IdempotencyKey.Trim();
            if(increases.Length>0)await TransferAsync(identity,$"seller-order-edit-increase:{orderId:N}:{key}",DeterministicGuid($"seller-order-edit-increase:{orderId:N}:{key}"),warehouseId,ordersWarehouseId,$"Aumento de reserva del pedido {number}",increases.Select(line=>(line.Key,line.Quantity)).ToArray(),connection,transaction,token);
            if(releases.Length>0)await TransferAsync(identity,$"seller-order-edit-release:{orderId:N}:{key}",DeterministicGuid($"seller-order-edit-release:{orderId:N}:{key}"),ordersWarehouseId,warehouseId,$"Liberación de reserva del pedido {number}",releases.Select(line=>(line.Key,line.Quantity)).ToArray(),connection,transaction,token);
            await SellerOrderReviewPersistence.ReplaceAsync(connection,transaction,orderId,actor.BusinessId,customerId,context.Name,context.Identification,context.Email,context.Phone,context.Address,request.Notes,total,DeterministicGuid($"seller-order-edit:{orderId:N}:{key}"),
                lines.Select(line=>new SellerOrderReplacementLine(line.ProductId,line.Code,line.Name,line.UnitCode,line.Quantity,line.UnitPrice,line.DiscountAmount,line.LineTotal,JsonSerializer.Serialize(new{line.PriceSource,line.Available}))).ToArray(),token);
            await transaction.CommitAsync(token);
            return new(orderId,number,"Confirmed",total,false,[]);
        }
        catch(Exception error)
        {
            if(transaction.Connection is not null)await transaction.RollbackAsync(CancellationToken.None);
            throw new SellerOrderConflictException($"No fue posible editar el pedido: {error.Message}");
        }
    }

    private async Task TransferAsync(InventoryUserIdentity identity,string idempotencyKey,Guid transferId,Guid source,Guid destination,string notes,IReadOnlyList<(Guid ProductId,decimal Quantity)> values,SqlConnection connection,SqlTransaction transaction,CancellationToken token)
    {var transferLines=values.Select((line,index)=>new WarehouseTransferLineRequest(index+1,line.ProductId,line.Quantity)).ToArray();await inventory.ConfirmTransferAtomicallyAsync(identity,idempotencyKey,new ConfirmWarehouseTransferRequest(transferId,identity.BusinessId,source,destination,DateTimeOffset.UtcNow,"WAREHOUSE_TRANSFER",notes,transferLines),connection,transaction,token);}

    public async Task<SellerOrdersApi.SellerCatalogPage> CatalogAsync(SellerOrderActor actor,
        SellerOrdersApi.SellerCatalogRequest request,CancellationToken token)
    {
        Demand(actor,"orders.create");
        if(request.BusinessId!=actor.BusinessId||request.WarehouseId==Guid.Empty||request.CustomerId==Guid.Empty)
            throw new SellerOrderValidationException("Sede, bodega y cliente son obligatorios.");
        var take=Math.Clamp(request.Take,1,500);var search=request.Search?.Trim()??string.Empty;
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await using var command=Procedure("dbo.SellerOrderCatalogGet",connection);
        command.Parameters.AddRange([P("@TenantId",actor.TenantId),P("@BusinessId",request.BusinessId),P("@WarehouseId",request.WarehouseId),P("@CustomerId",request.CustomerId),
            P("@Search",search),P("@Contains",$"%{search}%"),P("@Prefix",$"{search}%"),P("@Skip",request.Skip),P("@Take",take+1)]);
        var values=new List<SellerOrdersApi.SellerCatalogItem>();
        await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetDecimal(4),reader.GetString(5),reader.GetDecimal(6),reader.GetBoolean(7)));
        var more=values.Count>take;if(more)values.RemoveAt(values.Count-1);
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
            if(replay is not null){await transaction.CommitAsync(token);return replay;}
            var context=await LoadContextAsync(connection,transaction,actor,request,token);
            var requested=request.Lines.GroupBy(line=>line.ProductId).Select(group=>new SellerOrdersApi.SellerOrderLineInput(group.Key,group.Sum(line=>line.Quantity))).ToArray();
            var lines=new List<OrderLine>();var warnings=new List<string>();var position=0;
            foreach(var input in requested)
            {
                var line=await ResolveLineAsync(connection,transaction,request.BusinessId,request.WarehouseId,request.CustomerId,input,token);
                if(line is null)throw new SellerOrderValidationException($"El producto {input.ProductId:D} no está activo o no tiene precio publicado.");
                if(line.ManageStock&&line.Available<input.Quantity)warnings.Add($"{line.Code}: solicitadas {input.Quantity:N3}, disponibles {line.Available:N3}.");
                lines.Add(line with{Position=++position});
            }
            if(warnings.Count>0&&!request.CapturedOffline)throw new SellerOrderConflictException("Inventario insuficiente: "+string.Join(" ",warnings));
            var review=warnings.Count>0;var number=$"PED-{DateTime.UtcNow:yyyyMMdd}-{orderId.ToString("N")[..8].ToUpperInvariant()}";
            var total=lines.Sum(line=>decimal.Round(line.UnitPrice*line.Quantity,2,MidpointRounding.AwayFromZero));
            var reservationTransferId=DeterministicGuid($"seller-order-transfer:{orderId:N}");var attributes=JsonSerializer.Serialize(new{request.WarehouseId,ordersWarehouseId=context.OrdersWarehouseId,reservationTransferId,request.RouteId,request.RouteStopId,request.PartySiteId,request.CapturedOffline,requiresStockReview=review,createdBy=actor.UserId});
            var persistedLines=lines.Select(line=>{var lineTotal=decimal.Round(line.UnitPrice*line.Quantity,2,MidpointRounding.AwayFromZero);var tax=line.TaxRate<=0?0:decimal.Round(lineTotal*line.TaxRate/(100+line.TaxRate),2,MidpointRounding.AwayFromZero);return new{productId=line.ProductId,code=line.Code,name=line.Name,unitCode=line.UnitCode,quantity=line.Quantity,unitPrice=line.UnitPrice,taxAmount=tax,lineTotal,rawPayloadJson=JsonSerializer.Serialize(new{line.PriceSource,line.Available})};});
            await using(var insert=Procedure("dbo.SellerOrderCreate",connection,transaction))
            {insert.Parameters.AddRange([P("@OrderId",orderId),P("@BusinessId",request.BusinessId),P("@CustomerId",request.CustomerId),P("@Status",review?5:3),P("@CustomerName",context.Name),P("@Email",context.Email),P("@Phone",context.Phone),P("@Identification",context.Identification),P("@Address",context.Address),P("@Notes",request.Notes),Money("@Total",total),P("@Number",number),P("@ExternalStatus",review?"StockReview":"InventoryTransferPending"),P("@IdempotencyKey",request.IdempotencyKey.Trim()),P("@Attributes",attributes),P("@LinesJson",JsonSerializer.Serialize(persistedLines))]);await insert.ExecuteNonQueryAsync(token);}
            var stockLines=lines.Where(line=>line.ManageStock).Select((line,index)=>new WarehouseTransferLineRequest(index+1,line.ProductId,line.Quantity)).ToArray();
            if(review)
            {
                await transaction.CommitAsync(token);
                return new(orderId,number,"InReview",total,true,warnings);
            }

            if(stockLines.Length>0)
            {
                var identity=new InventoryUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,new HashSet<string>{InventoryPermissionCodes.Transfer,"inventory.system-warehouses.use"});
                await inventory.ConfirmTransferAtomicallyAsync(identity,$"seller-order-reservation:{orderId:N}",new ConfirmWarehouseTransferRequest(
                    reservationTransferId,request.BusinessId,request.WarehouseId,context.OrdersWarehouseId,
                    DateTimeOffset.UtcNow,"WAREHOUSE_TRANSFER",$"Reserva del pedido {number}",stockLines),connection,transaction,token);
            }
            await using(var confirm=Procedure("dbo.SellerOrderConfirm",connection,transaction))
            {confirm.Parameters.AddRange([P("@ExternalStatus",stockLines.Length>0?"InventoryTransferProcessed":"Confirmed"),P("@OrderId",orderId),P("@BusinessId",request.BusinessId)]);await confirm.ExecuteNonQueryAsync(token);}
            await transaction.CommitAsync(token);
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

    private static async Task<OrderLine?> ResolveLineAsync(SqlConnection connection,SqlTransaction transaction,Guid businessId,Guid warehouseId,Guid customerId,SellerOrdersApi.SellerOrderLineInput input,CancellationToken token)
    {await using var command=Procedure("dbo.SellerOrderProductResolve",connection,transaction);command.Parameters.AddRange([P("@BusinessId",businessId),P("@WarehouseId",warehouseId),P("@CustomerId",customerId),P("@ProductId",input.ProductId),Quantity("@Quantity",input.Quantity)]);await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?new(input.ProductId,reader.GetString(0),reader.GetString(1),reader.GetString(2),input.Quantity,reader.GetDecimal(3),reader.GetString(4),reader.GetDecimal(5),reader.GetBoolean(6),reader.GetDecimal(7),0):null;}
    private static SellerOrdersApi.SellerOrderLineInput[] NormalizeUpdateLines(IReadOnlyCollection<SellerOrdersApi.SellerOrderLineInput> lines)
    {
        var result=new List<SellerOrdersApi.SellerOrderLineInput>();
        foreach(var group in lines.GroupBy(line=>line.ProductId))
        {
            var prices=group.Where(line=>line.UnitPrice.HasValue).Select(line=>line.UnitPrice!.Value).Distinct().ToArray();
            if(prices.Length>1)throw new SellerOrderValidationException("Un mismo producto no puede guardarse con precios unitarios diferentes.");
            result.Add(new(group.Key,group.Sum(line=>line.Quantity),prices.Length==0?null:prices[0],group.Sum(line=>line.DiscountAmount)));
        }
        return result.ToArray();
    }
    private static void Validate(SellerOrderActor actor,SellerOrdersApi.CreateSellerOrderRequest request){if(request.BusinessId!=actor.BusinessId||request.WarehouseId==Guid.Empty||request.CustomerId==Guid.Empty)throw new SellerOrderValidationException("Sede, bodega y cliente son obligatorios.");if(string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.IdempotencyKey.Trim().Length>160)throw new SellerOrderValidationException("La clave idempotente es obligatoria.");if(request.Lines.Count is <1 or >500||request.Lines.Any(line=>line.ProductId==Guid.Empty||line.Quantity<=0))throw new SellerOrderValidationException("El pedido requiere productos y cantidades válidas.");if(request.Notes?.Length>1000)throw new SellerOrderValidationException("Las notas superan 1000 caracteres.");}
    private static void Demand(SellerOrderActor actor,string permission){if(!actor.Permissions.Contains(permission))throw new SellerOrderForbiddenException($"Permission '{permission}' is required.");}
    public static Guid DeterministicDocumentId(string value)=>DeterministicGuid(value);
    private static Guid DeterministicGuid(string value){var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));return new Guid(hash.AsSpan(0,16));}
    private static SqlParameter P(string name,object? value)=>new(name,value??DBNull.Value);
    private static SqlParameter Money(string name,decimal value)=>new(name,SqlDbType.Decimal){Precision=19,Scale=4,Value=value};
    private static SqlParameter Quantity(string name,decimal value)=>new(name,SqlDbType.Decimal){Precision=19,Scale=6,Value=value};
    private static SqlCommand Procedure(string name,SqlConnection connection,SqlTransaction? transaction=null)=>new(name,connection,transaction){CommandType=CommandType.StoredProcedure};
    private sealed record CustomerContext(string Name,string? Identification,string? Email,string? Phone,string Address,Guid OrdersWarehouseId);
    private sealed record OrderLine(Guid ProductId,string Code,string Name,string UnitCode,decimal Quantity,decimal UnitPrice,string PriceSource,decimal Available,bool ManageStock,decimal TaxRate,int Position,decimal DiscountAmount=0m)
    {
        public decimal LineTotal=>decimal.Round(UnitPrice*Quantity-DiscountAmount,2,MidpointRounding.AwayFromZero);
    }

}

public sealed class SellerOrderForbiddenException(string message):Exception(message);
public sealed class SellerOrderValidationException(string message):Exception(message);
public sealed class SellerOrderConflictException(string message):Exception(message);
