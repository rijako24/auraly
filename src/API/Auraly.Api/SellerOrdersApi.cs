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
    public sealed record SellerOrderLineInput(Guid ProductId, decimal Quantity);
    public sealed record UpdateSellerOrderRequest(string? Notes, string IdempotencyKey, IReadOnlyCollection<SellerOrderLineInput> Lines);
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
    }

    private static SellerOrderActor Actor(ClaimsPrincipal principal) => new(
        Required(principal,ClaimTypes.NameIdentifier),Required(principal,"tenant_id"),Required(principal,"business_id"),
        principal.FindAll("permission").Select(value=>value.Value).ToHashSet(StringComparer.Ordinal));
    private static Guid Required(ClaimsPrincipal principal,string type)=>Guid.TryParse(principal.FindFirstValue(type),out var value)
        ?value:throw new SellerOrderForbiddenException($"The authenticated identity lacks claim '{type}'.");
}

public sealed record SellerOrderActor(Guid UserId,Guid TenantId,Guid BusinessId,IReadOnlySet<string> Permissions);

public sealed class SellerOrderWriter(SqlServerConnectionFactory connections,InventoryOperationService inventory)
{
    public async Task<SellerOrdersApi.SellerOrderResult> UpdateReviewAsync(SellerOrderActor actor, Guid orderId,
        SellerOrdersApi.UpdateSellerOrderRequest request,CancellationToken token)
    {
        Demand(actor,"orders.update");
        if(orderId==Guid.Empty||string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.Lines.Count is <1 or >500||request.Lines.Any(line=>line.ProductId==Guid.Empty||line.Quantity<=0)||request.Notes?.Length>1000)
            throw new SellerOrderValidationException("El pedido requiere productos, cantidades y una clave de actualización válidos.");
        Guid warehouseId,customerId,ordersWarehouseId;string number;
        await using(var lookup=connections.Create())
        {await lookup.OpenAsync(token);await using var command=new SqlCommand("""
          SELECT o.ExternalDocumentNumber,o.CustomerId,o.Status,
                 TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.WarehouseId')),
                 TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.ordersWarehouseId'))
          FROM dbo.Orders o
          WHERE o.OrderId=@Id AND o.BusinessId=@BusinessId AND o.Source=1
            AND TRY_CONVERT(uniqueidentifier,JSON_VALUE(o.CustomAttributesJson,'$.createdBy'))=@UserId
            AND NOT EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks link WHERE link.OrderId=o.OrderId);
        """,lookup);command.Parameters.AddRange([P("@Id",orderId),P("@BusinessId",actor.BusinessId),P("@UserId",actor.UserId)]);await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new SellerOrderConflictException("El pedido no existe, no te pertenece o ya fue facturado.");number=reader.GetString(0);if(reader.IsDBNull(1)||reader.IsDBNull(3)||reader.IsDBNull(4))throw new SellerOrderConflictException("El pedido no conserva la configuración de bodega necesaria para editarlo.");customerId=reader.GetGuid(1);if(reader.GetInt32(2)!=5)throw new SellerOrderConflictException("Solo se puede corregir un pedido que esté en revisión; los pedidos confirmados conservan su reserva de inventario.");warehouseId=reader.GetGuid(3);ordersWarehouseId=reader.GetGuid(4);}
        await using var connection=connections.Create();await connection.OpenAsync(token);await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable,token);
        var requested=request.Lines.GroupBy(line=>line.ProductId).Select(group=>new SellerOrdersApi.SellerOrderLineInput(group.Key,group.Sum(line=>line.Quantity))).ToArray();
        var lines=new List<OrderLine>();var position=0;
        foreach(var input in requested){var line=await ResolveLineAsync(connection,transaction,actor.BusinessId,warehouseId,customerId,input,token);if(line is null)throw new SellerOrderValidationException($"El producto {input.ProductId:D} no está activo o no tiene precio publicado.");if(line.ManageStock&&line.Available<input.Quantity)throw new SellerOrderConflictException($"{line.Code}: solicitadas {input.Quantity:N3}, disponibles {line.Available:N3}.");lines.Add(line with{Position=++position});}
        var total=lines.Sum(line=>decimal.Round(line.UnitPrice*line.Quantity,2,MidpointRounding.AwayFromZero));
        var reservationTransferId=DeterministicGuid($"seller-order-edit-transfer:{orderId:N}:{request.IdempotencyKey.Trim()}");
        await using(var update=new SqlCommand("""
          UPDATE dbo.Orders SET Notes=@Notes,Subtotal=@Total,Total=@Total,Status=5,ExternalStatus=N'InventoryTransferPending',
            CustomAttributesJson=JSON_MODIFY(JSON_MODIFY(CustomAttributesJson,'$.reservationTransferId',CONVERT(nvarchar(36),@TransferId)),'$.requiresStockReview',CAST(0 AS bit)),UpdatedAt=SYSUTCDATETIME()
          WHERE OrderId=@Id AND BusinessId=@BusinessId;
          DELETE dbo.OrderItems WHERE OrderId=@Id;
        """,connection,transaction)){update.Parameters.AddRange([P("@Notes",request.Notes),Money("@Total",total),P("@TransferId",reservationTransferId),P("@Id",orderId),P("@BusinessId",actor.BusinessId)]);await update.ExecuteNonQueryAsync(token);}
        foreach(var line in lines){var lineTotal=decimal.Round(line.UnitPrice*line.Quantity,2,MidpointRounding.AwayFromZero);await using var insert=new SqlCommand("INSERT dbo.OrderItems(OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,ProductNameSnapshot,DescriptionSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,DiscountAmount,TaxAmount,LineTotal,RawPayloadJson,CreatedAt) VALUES(NEWID(),@OrderId,@BusinessId,@ProductId,@Sku,@Code,@Name,@Name,@Unit,@Quantity,@Price,0,0,@Total,@Raw,SYSUTCDATETIME());",connection,transaction);insert.Parameters.AddRange([P("@OrderId",orderId),P("@BusinessId",actor.BusinessId),P("@ProductId",line.ProductId),P("@Sku",line.Code),P("@Code",line.Code),P("@Name",line.Name),P("@Unit",line.UnitCode),Quantity("@Quantity",line.Quantity),Money("@Price",line.UnitPrice),Money("@Total",lineTotal),P("@Raw",JsonSerializer.Serialize(new{line.PriceSource,line.Available}))]);await insert.ExecuteNonQueryAsync(token);}
        await transaction.CommitAsync(token);
        var stockLines=lines.Where(line=>line.ManageStock).Select((line,index)=>new WarehouseTransferLineRequest(index+1,line.ProductId,line.Quantity)).ToArray();
        try{if(stockLines.Length>0){var identity=new InventoryUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,new HashSet<string>{InventoryPermissionCodes.Transfer});await inventory.ConfirmTransferAsync(identity,$"seller-order-edit-reservation:{orderId:N}:{request.IdempotencyKey.Trim()}",new ConfirmWarehouseTransferRequest(reservationTransferId,actor.BusinessId,warehouseId,ordersWarehouseId,DateTimeOffset.UtcNow,"WAREHOUSE_TRANSFER",$"Reserva corregida del pedido {number}",stockLines),token);}await SetOrderStateAsync(orderId,2,stockLines.Length>0?"InventoryTransferAccepted":"Confirmed",token);return new(orderId,number,"Confirmed",total,false,[]);}catch(Exception error){await SetOrderStateAsync(orderId,5,"InventoryTransferReview",token);return new(orderId,number,"InReview",total,true,[error.Message]);}
    }

    public async Task<SellerOrdersApi.SellerCatalogPage> CatalogAsync(SellerOrderActor actor,
        SellerOrdersApi.SellerCatalogRequest request,CancellationToken token)
    {
        Demand(actor,"orders.create");
        if(request.BusinessId!=actor.BusinessId||request.WarehouseId==Guid.Empty||request.CustomerId==Guid.Empty)
            throw new SellerOrderValidationException("Sede, bodega y cliente son obligatorios.");
        var take=Math.Clamp(request.Take,1,500);var search=request.Search?.Trim()??string.Empty;
        await using var connection=connections.Create();await connection.OpenAsync(token);
        await ValidateScopeAsync(connection,actor,request.BusinessId,request.WarehouseId,request.CustomerId,token);
        await using var command=new SqlCommand(CatalogSql,connection);
        command.Parameters.AddRange([P("@BusinessId",request.BusinessId),P("@WarehouseId",request.WarehouseId),P("@CustomerId",request.CustomerId),
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
            await using(var insert=new SqlCommand("""
                INSERT dbo.Orders(OrderId,BusinessId,CustomerId,Source,FulfillmentMode,Status,CustomerNameSnapshot,
                    CustomerEmailSnapshot,CustomerPhoneSnapshot,CustomerDocumentSnapshot,DeliveryAddressSnapshot,Notes,
                    Currency,Subtotal,DiscountTotal,TaxTotal,Total,CustomerConfirmed,ExternalDocumentNumber,ExternalStatus,
                    IdempotencyKey,CustomAttributesJson,CreatedAt,UpdatedAt)
                VALUES(@OrderId,@BusinessId,@CustomerId,1,0,@Status,@CustomerName,@Email,@Phone,@Identification,@Address,@Notes,
                    N'COP',@Total,0,0,@Total,1,@Number,@ExternalStatus,@Key,@Attributes,SYSUTCDATETIME(),SYSUTCDATETIME());
                """,connection,transaction))
            {
                insert.Parameters.AddRange([P("@OrderId",orderId),P("@BusinessId",request.BusinessId),P("@CustomerId",request.CustomerId),P("@Status",review?5:3),
                    P("@CustomerName",context.Name),P("@Email",context.Email),P("@Phone",context.Phone),P("@Identification",context.Identification),P("@Address",context.Address),
                    P("@Notes",request.Notes),Money("@Total",total),P("@Number",number),P("@ExternalStatus",review?"StockReview":"InventoryTransferPending"),P("@Key",request.IdempotencyKey.Trim()),P("@Attributes",attributes)]);
                await insert.ExecuteNonQueryAsync(token);
            }
            foreach(var line in lines)
            {
                var lineTotal=decimal.Round(line.UnitPrice*line.Quantity,2,MidpointRounding.AwayFromZero);
                var tax=line.TaxRate<=0?0:decimal.Round(lineTotal*line.TaxRate/(100+line.TaxRate),2,MidpointRounding.AwayFromZero);
                await using var insert=new SqlCommand("""
                    INSERT dbo.OrderItems(OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,ProductNameSnapshot,
                        DescriptionSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,DiscountAmount,TaxAmount,LineTotal,RawPayloadJson,CreatedAt)
                    VALUES(NEWID(),@OrderId,@BusinessId,@ProductId,@Sku,@Code,@Name,@Name,@Unit,@Quantity,@Price,0,@Tax,@Total,@Raw,SYSUTCDATETIME());
                    """,connection,transaction);
                insert.Parameters.AddRange([P("@OrderId",orderId),P("@BusinessId",request.BusinessId),P("@ProductId",line.ProductId),P("@Sku",line.Code),P("@Code",line.Code),P("@Name",line.Name),P("@Unit",line.UnitCode),Quantity("@Quantity",line.Quantity),Money("@Price",line.UnitPrice),Money("@Tax",tax),Money("@Total",lineTotal),P("@Raw",JsonSerializer.Serialize(new{line.PriceSource,line.Available}))]);
                await insert.ExecuteNonQueryAsync(token);
            }
            await transaction.CommitAsync(token);
            if(review)return new(orderId,number,"InReview",total,true,warnings);

            var stockLines=lines.Where(line=>line.ManageStock).Select((line,index)=>new WarehouseTransferLineRequest(index+1,line.ProductId,line.Quantity)).ToArray();
            if(stockLines.Length>0)
            {
                try
                {
                    var identity=new InventoryUserIdentity(actor.UserId,actor.TenantId,actor.BusinessId,new HashSet<string>{InventoryPermissionCodes.Transfer});
                    await inventory.ConfirmTransferAsync(identity,$"seller-order-reservation:{orderId:N}",new ConfirmWarehouseTransferRequest(
                        reservationTransferId,request.BusinessId,request.WarehouseId,context.OrdersWarehouseId,
                        DateTimeOffset.UtcNow,"WAREHOUSE_TRANSFER",$"Reserva del pedido {number}",stockLines),token);
                    await SetOrderStateAsync(orderId,2,"InventoryTransferAccepted",token);
                }
                catch(Exception error)
                {
                    await SetOrderStateAsync(orderId,5,"InventoryTransferReview",token);
                    return new(orderId,number,"InReview",total,true,[error.Message]);
                }
            }
            else await SetOrderStateAsync(orderId,2,"Confirmed",token);
            return new(orderId,number,"Confirmed",total,false,[]);
        }
        catch{if(transaction.Connection is not null)await transaction.RollbackAsync(token);throw;}
    }

    private async Task SetOrderStateAsync(Guid orderId,int status,string externalStatus,CancellationToken token)
    {await using var connection=connections.Create();await connection.OpenAsync(token);await using var command=new SqlCommand("UPDATE dbo.Orders SET Status=@Status,ExternalStatus=@External,UpdatedAt=SYSUTCDATETIME() WHERE OrderId=@Id",connection);command.Parameters.AddRange([P("@Status",status),P("@External",externalStatus),P("@Id",orderId)]);await command.ExecuteNonQueryAsync(token);}

    private static async Task<SellerOrdersApi.SellerOrderResult?> ReplayAsync(SqlConnection connection,SqlTransaction transaction,Guid businessId,string key,CancellationToken token)
    {await using var command=new SqlCommand("SELECT OrderId,ExternalDocumentNumber,Status,Total,ExternalStatus FROM dbo.Orders WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key",connection,transaction);command.Parameters.AddRange([P("@BusinessId",businessId),P("@Key",key.Trim())]);await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))return null;var review=reader.GetInt32(2)==5;return new(reader.GetGuid(0),reader.GetString(1),review?"InReview":"Confirmed",reader.GetDecimal(3),review,review?[reader.IsDBNull(4)?"Requiere revisión.":reader.GetString(4)]:[]);}

    private static async Task<CustomerContext> LoadContextAsync(SqlConnection connection,SqlTransaction transaction,SellerOrderActor actor,SellerOrdersApi.CreateSellerOrderRequest request,CancellationToken token)
    {await using var command=new SqlCommand("""
        SELECT COALESCE(p.DisplayName,p.LegalName,CONCAT(p.FirstName,N' ',p.LastName)),p.Identification,email.Value,phone.Value,
               COALESCE(site.AddressLine,N''),orders.WarehouseId
        FROM dbo.Customers customer INNER JOIN dbo.Parties p ON p.PartyId=customer.PartyId
        OUTER APPLY(SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact WHERE contact.PartyId=p.PartyId AND contact.ContactType=N'Email' AND contact.IsActive=1 ORDER BY contact.IsPrimary DESC,contact.CreatedAt) email
        OUTER APPLY(SELECT TOP(1) contact.Value FROM dbo.PartyContacts contact WHERE contact.PartyId=p.PartyId AND contact.ContactType=N'Phone' AND contact.IsActive=1 ORDER BY contact.IsPrimary DESC,contact.CreatedAt) phone
        LEFT JOIN dbo.PartySites site ON site.PartySiteId=@SiteId AND site.PartyId=p.PartyId AND site.IsActive=1
        CROSS APPLY(SELECT TOP(1) WarehouseId FROM dbo.Warehouses WHERE BusinessId=@BusinessId AND Code=N'PED' AND IsActive=1 ORDER BY CreatedAt) orders
        INNER JOIN dbo.Businesses business ON business.BusinessId=customer.BusinessId AND business.TenantId=@TenantId
        WHERE customer.CustomerId=@CustomerId AND customer.BusinessId=@BusinessId AND customer.IsActive=1;
        """,connection,transaction);command.Parameters.AddRange([P("@SiteId",request.PartySiteId),P("@BusinessId",request.BusinessId),P("@TenantId",actor.TenantId),P("@CustomerId",request.CustomerId)]);await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new SellerOrderValidationException("El cliente, su sede o la bodega de pedidos no están disponibles.");return new(reader.GetString(0),reader.IsDBNull(1)?null:reader.GetString(1),reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3),reader.GetString(4),reader.GetGuid(5));}

    private static async Task<OrderLine?> ResolveLineAsync(SqlConnection connection,SqlTransaction transaction,Guid businessId,Guid warehouseId,Guid customerId,SellerOrdersApi.SellerOrderLineInput input,CancellationToken token)
    {await using var command=new SqlCommand(LineSql,connection,transaction);command.Parameters.AddRange([P("@BusinessId",businessId),P("@WarehouseId",warehouseId),P("@CustomerId",customerId),P("@ProductId",input.ProductId),Quantity("@Quantity",input.Quantity)]);await using var reader=await command.ExecuteReaderAsync(token);return await reader.ReadAsync(token)?new(input.ProductId,reader.GetString(0),reader.GetString(1),reader.GetString(2),input.Quantity,reader.GetDecimal(3),reader.GetString(4),reader.GetDecimal(5),reader.GetBoolean(6),reader.GetDecimal(7),0):null;}
    private static async Task ValidateScopeAsync(SqlConnection connection,SellerOrderActor actor,Guid businessId,Guid warehouseId,Guid customerId,CancellationToken token)
    {await using var command=new SqlCommand("SELECT COUNT(*) FROM dbo.Businesses b JOIN dbo.Warehouses w ON w.BusinessId=b.BusinessId JOIN dbo.Customers c ON c.BusinessId=b.BusinessId WHERE b.BusinessId=@BusinessId AND b.TenantId=@TenantId AND w.WarehouseId=@WarehouseId AND w.IsActive=1 AND w.UseForSales=1 AND c.CustomerId=@CustomerId AND c.IsActive=1",connection);command.Parameters.AddRange([P("@BusinessId",businessId),P("@TenantId",actor.TenantId),P("@WarehouseId",warehouseId),P("@CustomerId",customerId)]);if(Convert.ToInt32(await command.ExecuteScalarAsync(token))!=1)throw new SellerOrderValidationException("Selecciona una bodega de venta válida.");}
    private static void Validate(SellerOrderActor actor,SellerOrdersApi.CreateSellerOrderRequest request){if(request.BusinessId!=actor.BusinessId||request.WarehouseId==Guid.Empty||request.CustomerId==Guid.Empty)throw new SellerOrderValidationException("Sede, bodega y cliente son obligatorios.");if(string.IsNullOrWhiteSpace(request.IdempotencyKey)||request.IdempotencyKey.Trim().Length>160)throw new SellerOrderValidationException("La clave idempotente es obligatoria.");if(request.Lines.Count is <1 or >500||request.Lines.Any(line=>line.ProductId==Guid.Empty||line.Quantity<=0))throw new SellerOrderValidationException("El pedido requiere productos y cantidades válidas.");if(request.Notes?.Length>1000)throw new SellerOrderValidationException("Las notas superan 1000 caracteres.");}
    private static void Demand(SellerOrderActor actor,string permission){if(!actor.Permissions.Contains(permission))throw new SellerOrderForbiddenException($"Permission '{permission}' is required.");}
    public static Guid DeterministicDocumentId(string value)=>DeterministicGuid(value);
    private static Guid DeterministicGuid(string value){var hash=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));return new Guid(hash.AsSpan(0,16));}
    private static SqlParameter P(string name,object? value)=>new(name,value??DBNull.Value);
    private static SqlParameter Money(string name,decimal value)=>new(name,SqlDbType.Decimal){Precision=19,Scale=4,Value=value};
    private static SqlParameter Quantity(string name,decimal value)=>new(name,SqlDbType.Decimal){Precision=19,Scale=6,Value=value};
    private sealed record CustomerContext(string Name,string? Identification,string? Email,string? Phone,string Address,Guid OrdersWarehouseId);
    private sealed record OrderLine(Guid ProductId,string Code,string Name,string UnitCode,decimal Quantity,decimal UnitPrice,string PriceSource,decimal Available,bool ManageStock,decimal TaxRate,int Position);

    private const string LineSql="""
        SELECT COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),p.Name,COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
               COALESCE(listPrice.Amount,channelPrice.Amount,basePrice.Amount),
               CASE WHEN listPrice.Amount IS NOT NULL THEN N'PriceList' WHEN channelPrice.Amount IS NOT NULL THEN N'PriceChannel' ELSE N'Public' END,
               COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=p.BusinessId AND m.WarehouseId=@WarehouseId AND m.ProductId=p.ProductId),0),
               p.ManageStock,COALESCE(tax.Rate,0)
        FROM dbo.Products p
        LEFT JOIN dbo.TaxProfiles tax ON tax.TaxProfileId=p.TaxProfileId AND tax.IsActive=1
        CROSS APPLY(SELECT TOP(1) pp.Amount FROM dbo.ProductPrices pp WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId AND pp.IsActive=1 AND pp.ValidFrom<=SYSDATETIMEOFFSET() AND(pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY pp.ValidFrom DESC)basePrice
        LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=@CustomerId
        OUTER APPLY(SELECT TOP(1)i.Amount FROM dbo.PriceListItems i JOIN dbo.PriceLists l ON l.PriceListId=i.PriceListId WHERE i.PriceListId=setting.PriceListId AND l.BusinessId=@BusinessId AND l.IsActive=1 AND i.ProductId=p.ProductId AND i.IsActive=1 AND i.MinimumQuantity<=@Quantity AND i.ValidFrom<=SYSDATETIMEOFFSET() AND(i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY i.MinimumQuantity DESC,i.ValidFrom DESC)listPrice
        OUTER APPLY(SELECT TOP(1)i.Amount FROM dbo.ResolvedPriceChannelItems i JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId WHERE i.PriceChannelId=setting.PriceChannelId AND c.BusinessId=@BusinessId AND c.IsActive=1 AND i.ProductId=p.ProductId AND i.IsActive=1 AND i.ValidFrom<=SYSDATETIMEOFFSET() AND(i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET()) AND NOT EXISTS(SELECT 1 FROM dbo.PriceChannelExclusions e WHERE e.PriceChannelId=i.PriceChannelId AND e.ProductId=i.ProductId))channelPrice
        WHERE p.BusinessId=@BusinessId AND p.ProductId=@ProductId AND p.IsActive=1;
        """;
    private const string CatalogSql="""
        SELECT p.ProductId,COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),p.Name,COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
               COALESCE(listPrice.Amount,channelPrice.Amount,basePrice.Amount),CASE WHEN listPrice.Amount IS NOT NULL THEN N'PriceList' WHEN channelPrice.Amount IS NOT NULL THEN N'PriceChannel' ELSE N'Public' END,
               COALESCE((SELECT SUM(m.QuantityChange) FROM dbo.InventoryMovements m WHERE m.BusinessId=p.BusinessId AND m.WarehouseId=@WarehouseId AND m.ProductId=p.ProductId),0),p.ManageStock
        FROM dbo.Products p CROSS APPLY(SELECT TOP(1)pp.Amount FROM dbo.ProductPrices pp WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId AND pp.IsActive=1 AND pp.ValidFrom<=SYSDATETIMEOFFSET() AND(pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY pp.ValidFrom DESC)basePrice
        LEFT JOIN dbo.CustomerPricingSettings setting ON setting.CustomerId=@CustomerId
        OUTER APPLY(SELECT TOP(1)i.Amount FROM dbo.PriceListItems i JOIN dbo.PriceLists l ON l.PriceListId=i.PriceListId WHERE i.PriceListId=setting.PriceListId AND l.BusinessId=@BusinessId AND l.IsActive=1 AND i.ProductId=p.ProductId AND i.IsActive=1 AND i.MinimumQuantity<=1 AND i.ValidFrom<=SYSDATETIMEOFFSET() AND(i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET()) ORDER BY i.MinimumQuantity DESC,i.ValidFrom DESC)listPrice
        OUTER APPLY(SELECT TOP(1)i.Amount FROM dbo.ResolvedPriceChannelItems i JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId WHERE i.PriceChannelId=setting.PriceChannelId AND c.BusinessId=@BusinessId AND c.IsActive=1 AND i.ProductId=p.ProductId AND i.IsActive=1 AND i.ValidFrom<=SYSDATETIMEOFFSET() AND(i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET()) AND NOT EXISTS(SELECT 1 FROM dbo.PriceChannelExclusions e WHERE e.PriceChannelId=i.PriceChannelId AND e.ProductId=i.ProductId))channelPrice
        WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND(@Search=N''
          OR p.Name COLLATE Latin1_General_100_CI_AI LIKE @Contains COLLATE Latin1_General_100_CI_AI
          OR p.ProductCode COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI
          OR p.Sku COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI
          OR p.Reference COLLATE Latin1_General_100_CI_AI LIKE @Prefix COLLATE Latin1_General_100_CI_AI)
        ORDER BY CASE WHEN p.ProductCode=@Search OR p.Sku=@Search THEN 0 ELSE 1 END,p.Name,p.ProductId OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
        """;
}

public sealed class SellerOrderForbiddenException(string message):Exception(message);
public sealed class SellerOrderValidationException(string message):Exception(message);
public sealed class SellerOrderConflictException(string message):Exception(message);
