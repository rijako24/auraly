using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class SourceOrderPosUploadTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Pos_sale_links_source_order_once_and_releases_its_claim()
    {
        var orderId = Guid.NewGuid();
        var claimId = Guid.NewGuid();
        var ordersWarehouseId = Guid.NewGuid();
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT dbo.Warehouses(
                  WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,
                  IsSystem,UseForSales,UseForGoodsReceipts,IsInventoryVisible,IsActive,CreatedAt)
                VALUES(
                  @OrdersWarehouseId,@BusinessId,@OrdersWarehouseCode,N'Pedidos prueba',0,
                  1,0,0,0,1,SYSDATETIMEOFFSET());

                INSERT dbo.InventoryBalances(
                  BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,
                  InventoryValue,LastProcessingSequence,UpdatedAt)
                VALUES(@BusinessId,@OrdersWarehouseId,@ProductId,1,5000,5000,0,SYSDATETIMEOFFSET());

                INSERT dbo.Orders(
                  OrderId,BusinessId,Source,FulfillmentMode,Status,
                  CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
                  Subtotal,DiscountTotal,Total,CustomerConfirmed,
                  ExternalDocumentNumber,CustomAttributesJson,CreatedAt)
                VALUES(
                  @OrderId,@BusinessId,0,0,2,N'Cliente POS',N'900100200',N'COP',
                  10000,0,10000,1,@OrderNumber,@Attributes,SYSUTCDATETIME());

                INSERT dbo.OrderItems(
                  OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
                  ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
                  DiscountAmount,LineTotal,CreatedAt)
                VALUES(
                  NEWID(),@OrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
                  N'Producto pedido POS',N'EA',1,10000,0,10000,SYSUTCDATETIME());

                INSERT dbo.OrderClaims(
                  OrderClaimId,BusinessId,WarehouseId,OrderId,WorkSessionId,DeviceId,UserId,
                  ClaimedAt,ExpiresAt,ReleasedAt)
                VALUES(
                  @ClaimId,@BusinessId,@WarehouseId,@OrderId,@WorkSessionId,@DeviceId,@UserId,
                  SYSDATETIMEOFFSET(),DATEADD(minute,10,SYSDATETIMEOFFSET()),NULL);
                """;
            seed.Parameters.AddWithValue("@OrderId", orderId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            seed.Parameters.AddWithValue("@WorkSessionId", fixture.WorkSessionId);
            seed.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            seed.Parameters.AddWithValue("@OrderNumber", $"PED-POS-{orderId:N}");
            seed.Parameters.AddWithValue("@ClaimId", claimId);
            seed.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
            seed.Parameters.AddWithValue("@OrdersWarehouseCode", $"PED-{ordersWarehouseId:N}"[..32]);
            seed.Parameters.AddWithValue("@Attributes", System.Text.Json.JsonSerializer.Serialize(new { ordersWarehouseId }));
            seed.Parameters.AddWithValue("@DeviceId", fixture.DeviceId);
            seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            await seed.ExecuteNonQueryAsync();
        }

        var sale = fixture.CreateValidRequest(181) with { SourceOrderId = orderId };
        using var client = fixture.CreateClient();
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await client.SendAsync(upload))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
            Assert.NotNull(receipt);
            Assert.Equal(PosSaleRemoteStatuses.FiscalVerified, receipt.Status);
        }

        using (var duplicate = fixture.CreateUploadMessage(sale))
        using (var response = await client.SendAsync(duplicate))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var receipt = await response.Content.ReadFromJsonAsync<PosSaleUploadResponse>();
            Assert.NotNull(receipt);
            Assert.Equal(PosSaleRemoteStatuses.AlreadyProcessed, receipt.Status);
        }

        await using var verifyConnection = new SqlConnection(fixture.ConnectionString);
        await verifyConnection.OpenAsync();
        await using var verify = verifyConnection.CreateCommand();
        verify.CommandText = """
            SELECT
              (SELECT COUNT(*) FROM dbo.OrderInvoiceLinks
               WHERE OrderId=@OrderId AND DocumentId=@DocumentId),
              (SELECT COUNT(*) FROM dbo.OrderClaims
               WHERE OrderClaimId=@ClaimId AND ReleasedAt IS NOT NULL),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId=@DocumentId AND MovementType IN (N'TransferOut',N'TransferIn',N'Sale')),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId=@DocumentId AND MovementType=N'TransferOut' AND WarehouseId=@OrdersWarehouseId),
              (SELECT COUNT(*) FROM dbo.InventoryMovements
               WHERE DocumentId=@DocumentId AND MovementType IN (N'TransferIn',N'Sale') AND WarehouseId=@SalesWarehouseId);
            """;
        verify.Parameters.AddWithValue("@OrderId", orderId);
        verify.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
        verify.Parameters.AddWithValue("@ClaimId", claimId);
        verify.Parameters.AddWithValue("@OrdersWarehouseId", ordersWarehouseId);
        verify.Parameters.AddWithValue("@SalesWarehouseId", fixture.WarehouseId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal(3, reader.GetInt32(2));
        Assert.Equal(1, reader.GetInt32(3));
        Assert.Equal(2, reader.GetInt32(4));
    }
}
