using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class OnlineSalesCheckoutTests
{
    [Fact]
    public async Task A_non_fiscal_sales_return_preserves_the_original_issue_origins()
    {
        var userId=await CreateUserAsync("receipt-origin-return");
        using var client=fixture.CreateUserClient(userId,CommercePermissionCodes.SalesCreate,
            SalesReturnPermissionCodes.Create,SalesReturnPermissionCodes.Read);
        var captured=await CaptureAsync(client,await OpenAsync(client));
        var completed=await CompleteAsync(client,captured.DraftId,new CompleteOnlineSalesDraftRequest(
            captured.Version,[new OnlineSalesPayment("Cash",captured.PayableAmount,null)],
            DocumentType:PosSaleDocumentTypes.Receipt),$"origin-sale-{Guid.NewGuid():N}");
        var returnId=Guid.NewGuid();
        var command=new ConfirmSalesReturnRequest(returnId,fixture.BusinessId,fixture.WarehouseId,
            completed.Receipt.DocumentId,DateTimeOffset.UtcNow,ReturnEconomicResolutions.Refund,
            SalesReturnRefundMethods.Cash,"Devolución con origen del comprobante no electrónico",
            [new(1,captured.Lines[0].Quantity/2m,ReturnInventoryDispositions.Sellable)],
            captured.WorkSessionId,ReasonCode:"Other");
        using var request=new HttpRequestMessage(HttpMethod.Post,"/api/commerce/v1/sales-returns/confirm")
            { Content=JsonContent.Create(command) };
        request.Headers.Add("Idempotency-Key",returnId.ToString("N"));
        using var response=await client.SendAsync(request);
        Assert.True(response.StatusCode==HttpStatusCode.Accepted,await response.Content.ReadAsStringAsync());
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var proof=new SqlCommand("""
            SELECT job.Status,original.DocumentType,SUM(portion.QuantityChange),
              (SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@ReturnId)
            FROM dbo.DocumentProcessingJobs job JOIN dbo.InventoryMovements movement
              ON movement.DocumentId=job.DocumentId AND movement.DocumentType=job.DocumentType
            JOIN inventory.MovementOrigins portion ON portion.InventoryMovementId=movement.InventoryMovementId
            JOIN dbo.InventoryMovements original ON original.InventoryMovementId=portion.ReferencedMovementId
            WHERE job.DocumentId=@ReturnId AND original.DocumentId=@SaleId
            GROUP BY job.Status,original.DocumentType;
            """,connection);
        proof.Parameters.AddWithValue("@ReturnId",returnId);
        proof.Parameters.AddWithValue("@SaleId",completed.Receipt.DocumentId);
        await using var reader=await proof.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("Completed",reader.GetString(0));
        Assert.Equal(PosSaleDocumentTypes.Receipt,reader.GetString(1));
        Assert.Equal(command.Lines.Single().Quantity,reader.GetDecimal(2));
        Assert.Equal(0,reader.GetInt32(3));
        Assert.False(await reader.ReadAsync());
    }
}
