using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ServerSliceFixture
{
    internal async Task SeedConfirmedOrdersForInvoiceAsync(
        Guid userId,
        Guid workSessionId,
        Guid firstOrderId,
        Guid secondOrderId)
    {
        var ordersWarehouseId = Guid.NewGuid();
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.AppUsers(
              UserId,TenantId,Username,NormalizedUsername,Email,NormalizedEmail,
              FirstName,LastName,IsActive,CreatedAt)
            VALUES(
              @UserId,@TenantId,@Username,UPPER(@Username),
              CONCAT(@Username,N'@test.local'),UPPER(CONCAT(@Username,N'@test.local')),
              N'Lote',N'Pedidos',1,SYSDATETIMEOFFSET());

            INSERT dbo.WorkSessions(
              WorkSessionId,TenantId,BusinessId,WarehouseId,UserId,DeviceId,
              OpenedAt,LastActivityAt,Status)
            VALUES(
              @WorkSessionId,@TenantId,@BusinessId,@WarehouseId,@UserId,NULL,
              SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET(),N'Open');

            INSERT dbo.Warehouses(
              WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
              IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
            VALUES(
              @OrdersWarehouseId,@BusinessId,@OrdersWarehouseCode,N'Pedidos lote',0,
              1,0,0,0,1,SYSDATETIMEOFFSET());

            INSERT dbo.InventoryBalances(
              BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
              InventoryValue,LastProcessingSequence,UpdatedAt)
            VALUES(@BusinessId,@OrdersWarehouseId,@ProductId,3,5000,15000,1,SYSDATETIMEOFFSET());

            UPDATE dbo.InventoryBalances
            SET QuantityOnHand=100,AverageUnitCost=5000,InventoryValue=500000,
                OriginContextId=NULL,OriginSnapshotSequence=NULL,
                UpdatedAt=SYSDATETIMEOFFSET()
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;

            INSERT dbo.Orders(
              OrderId,BusinessId,Source,FulfillmentMode,Status,
              CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
              Subtotal,DiscountTotal,Total,CustomerConfirmed,
              ExternalDocumentNumber,OrdersWarehouseId,CreatedAt,CustomAttributesJson)
            VALUES
              (@FirstOrderId,@BusinessId,0,0,2,N'Cliente uno',N'1001',N'COP',
               10000,0,10000,1,N'PED-LOTE-01',@OrdersWarehouseId,DATEADD(day,-2,SYSUTCDATETIME()),
               CONCAT(N'{"WarehouseId":"',CONVERT(nvarchar(36),@WarehouseId),N'"}')),
              (@SecondOrderId,@BusinessId,0,0,2,N'Cliente dos',N'1002',N'COP',
               20000,0,20000,1,N'PED-LOTE-02',@OrdersWarehouseId,DATEADD(day,-1,SYSUTCDATETIME()),
               CONCAT(N'{"WarehouseId":"',CONVERT(nvarchar(36),@WarehouseId),N'"}'));

            INSERT dbo.OrderItems(
              OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
              ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
              DocumentUnitCost,DiscountAmount,LineTotal,CreatedAt)
            VALUES
              (NEWID(),@FirstOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto lote',N'EA',1,10000,6000,0,10000,DATEADD(day,-2,SYSUTCDATETIME())),
              (NEWID(),@SecondOrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
               N'Producto lote',N'EA',2,10000,6000,0,20000,DATEADD(day,-1,SYSUTCDATETIME()));
            """;
        command.Parameters.AddWithValue("@UserId", userId);
        command.Parameters.AddWithValue("@WorkSessionId", workSessionId);
        command.Parameters.AddWithValue("@WarehouseId", WarehouseId);
        command.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
        command.Parameters.AddWithValue("@OrdersWarehouseCode", $"PED-{ordersWarehouseId:N}"[..32]);
        command.Parameters.AddWithValue("@TenantId", TenantId);
        command.Parameters.AddWithValue("@Username", $"batch-{userId:N}");
        command.Parameters.AddWithValue("@FirstOrderId", firstOrderId);
        command.Parameters.AddWithValue("@SecondOrderId", secondOrderId);
        command.Parameters.AddWithValue("@BusinessId", BusinessId);
        command.Parameters.AddWithValue("@ProductId", ProductId);
        await command.ExecuteNonQueryAsync();
    }
}
