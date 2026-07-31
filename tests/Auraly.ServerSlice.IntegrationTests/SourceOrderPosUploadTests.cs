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
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT dbo.Orders(
                  OrderId,BusinessId,Source,FulfillmentMode,Status,
                  CustomerNameSnapshot,CustomerDocumentSnapshot,Currency,
                  Subtotal,DiscountTotal,Total,CustomerConfirmed,
                  ExternalDocumentNumber,CreatedAt)
                VALUES(
                  @OrderId,@BusinessId,0,0,2,N'Cliente POS',N'900100200',N'COP',
                  10000,0,10000,1,@OrderNumber,SYSUTCDATETIME());

                INSERT dbo.OrderItems(
                  OrderItemId,OrderId,BusinessId,ProductId,Sku,ProductCodeSnapshot,
                  ProductNameSnapshot,UnitCodeSnapshot,Quantity,UnitPrice,
                  DiscountAmount,LineTotal,CreatedAt)
                VALUES(
                  NEWID(),@OrderId,@BusinessId,@ProductId,N'P-E2E',N'P-E2E',
                  N'Producto pedido POS',N'EA',1,10000,0,10000,SYSUTCDATETIME());

                INSERT dbo.OrderClaims(
                  OrderClaimId,BusinessId,OrderId,RegisterId,DeviceId,UserId,
                  ClaimedAt,ExpiresAt,ReleasedAt)
                VALUES(
                  @ClaimId,@BusinessId,@OrderId,@RegisterId,@DeviceId,@UserId,
                  SYSDATETIMEOFFSET(),DATEADD(minute,10,SYSDATETIMEOFFSET()),NULL);
                """;
            seed.Parameters.AddWithValue("@OrderId", orderId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            seed.Parameters.AddWithValue("@OrderNumber", $"PED-POS-{orderId:N}");
            seed.Parameters.AddWithValue("@ClaimId", claimId);
            seed.Parameters.AddWithValue("@RegisterId", fixture.RegisterId);
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
               WHERE OrderClaimId=@ClaimId AND ReleasedAt IS NOT NULL);
            """;
        verify.Parameters.AddWithValue("@OrderId", orderId);
        verify.Parameters.AddWithValue("@DocumentId", sale.DocumentId);
        verify.Parameters.AddWithValue("@ClaimId", claimId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
    }
}
