using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PurchaseReturnProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Return_flows_once_through_inventory_payable_accounting_job_and_outbox()
    {
        var receipt=await CreateReceiptAsync(10m);
        var beforeQuantity=await InventoryAsync(receipt.ProductId,"QuantityOnHand");
        var beforeValue=await InventoryAsync(receipt.ProductId,"InventoryValue");
        var request=CreateReturn(receipt.Acceptance.DocumentId,4m);
        using var client=CreateReturnClient();
        using(var response=await SendAsync(client,request,$"return-{request.ReturnId:N}"))
        {
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
            var accepted=await response.Content.ReadFromJsonAsync<PurchaseReturnAcceptance>();
            Assert.NotNull(accepted);Assert.StartsWith("DCP00-",accepted.DocumentNumber);
            Assert.False(accepted.IdempotentReplay);
        }
        Assert.Equal("Processed",await ScalarAsync<string>(
            "SELECT Status FROM dbo.PurchaseReturns WHERE PurchaseReturnId=@Id",request.ReturnId));
        Assert.Equal(beforeQuantity-4m,await InventoryAsync(receipt.ProductId,"QuantityOnHand"));
        Assert.Equal(beforeValue-20_000m,await InventoryAsync(receipt.ProductId,"InventoryValue"));
        Assert.Equal(35_700m,await ScalarAsync<decimal>("""
            SELECT OutstandingAmount FROM dbo.Payables
            WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'
            """,receipt.Acceptance.DocumentId));
        Assert.Equal(23_800m,await ScalarAsync<decimal>("""
            SELECT Amount FROM dbo.PayableTransactions
            WHERE SourceDocumentId=@Id AND TransactionType=N'Credit'
            """,request.ReturnId));
        Assert.Equal(0,await CountAsync("SupplierCredits","SourcePurchaseReturnId",request.ReturnId));
        Assert.Equal(1,await CountAsync("InventoryMovements","DocumentId",request.ReturnId));
        Assert.Equal(1,await CountAsync("PurchaseReturnFinancialEffects","PurchaseReturnId",request.ReturnId));
        Assert.Equal(1,await CountAsync("AccountingPostingJobs","SourceDocumentId",request.ReturnId));
        Assert.Equal(1,await CountAsync("ServerOutboxMessages","DocumentId",request.ReturnId));

        using(var replayResponse=await SendAsync(client,request,$"return-{request.ReturnId:N}"))
        {
            Assert.Equal(HttpStatusCode.Accepted,replayResponse.StatusCode);
            var replay=await replayResponse.Content.ReadFromJsonAsync<PurchaseReturnAcceptance>();
            Assert.NotNull(replay);Assert.True(replay.IdempotentReplay);
        }
        Assert.Equal(1,await CountAsync("InventoryMovements","DocumentId",request.ReturnId));
        Assert.Equal(1,await CountAsync("PayableTransactions","SourceDocumentId",request.ReturnId));
        Assert.Equal(1,await CountAsync("ServerOutboxMessages","DocumentId",request.ReturnId));

        using var list=await client.GetAsync("/api/commerce/v1/purchase-returns/receipts?page=1&pageSize=25");
        Assert.Equal(HttpStatusCode.OK,list.StatusCode);
        var page=await list.Content.ReadFromJsonAsync<ReturnableGoodsReceiptPage>();
        Assert.NotNull(page);Assert.Contains(page.Items,
            item=>item.GoodsReceiptId==receipt.Acceptance.DocumentId);
        using var detail=await client.GetAsync(
            $"/api/commerce/v1/purchase-returns/receipts/{receipt.Acceptance.DocumentId:D}");
        Assert.Equal(HttpStatusCode.OK,detail.StatusCode);
        var original=await detail.Content.ReadFromJsonAsync<ReturnableGoodsReceipt>();
        Assert.NotNull(original);Assert.Equal(6m,original.Lines.Single().AvailableQuantity);
    }

    [Fact]
    public async Task Paid_receipt_creates_supplier_credit_and_excess_is_rejected()
    {
        var receipt=await CreateReceiptAsync(5m);
        await ExecuteAsync("""
            UPDATE dbo.Payables SET OutstandingAmount=0,Status=N'Paid'
            WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt';
            """,receipt.Acceptance.DocumentId);
        var request=CreateReturn(receipt.Acceptance.DocumentId,2m);
        using var client=CreateReturnClient();
        using(var response=await SendAsync(client,request,$"credit-{request.ReturnId:N}"))
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
        Assert.Equal(11_900m,await ScalarAsync<decimal>(
            "SELECT AvailableAmount FROM dbo.SupplierCredits WHERE SourcePurchaseReturnId=@Id",
            request.ReturnId));
        Assert.Equal(0m,await ScalarAsync<decimal>(
            "SELECT PayableCreditAmount FROM dbo.PurchaseReturnFinancialEffects WHERE PurchaseReturnId=@Id",
            request.ReturnId));
        Assert.Equal(11_900m,await ScalarAsync<decimal>(
            "SELECT SupplierCreditAmount FROM dbo.PurchaseReturnFinancialEffects WHERE PurchaseReturnId=@Id",
            request.ReturnId));

        var excess=CreateReturn(receipt.Acceptance.DocumentId,4m);
        using var rejected=await SendAsync(client,excess,$"excess-{excess.ReturnId:N}");
        Assert.Equal(HttpStatusCode.Conflict,rejected.StatusCode);
        Assert.Equal(0,await CountAsync("PurchaseReturns","PurchaseReturnId",excess.ReturnId));
        Assert.Equal(0,await CountAsync("DocumentProcessingJobs","DocumentId",excess.ReturnId));
    }

    [Fact]
    public async Task Return_requires_backend_permissions_and_authenticated_business()
    {
        var receipt=await CreateReceiptAsync(2m);
        var request=CreateReturn(receipt.Acceptance.DocumentId,1m);
        using var denied=fixture.CreateAdminClient(PurchasingPermissionCodes.ReadPurchaseReturns);
        using(var response=await SendAsync(denied,request,$"denied-{request.ReturnId:N}"))
            Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using var allowed=CreateReturnClient();
        var wrong=request with { ReturnId=Guid.NewGuid(),BusinessId=Guid.NewGuid() };
        using var scoped=await SendAsync(allowed,wrong,$"scope-{wrong.ReturnId:N}");
        Assert.Equal(HttpStatusCode.Forbidden,scoped.StatusCode);
    }

    private HttpClient CreateReturnClient()=>fixture.CreateAdminClient(
        PurchasingPermissionCodes.ReadPurchaseReturns,
        PurchasingPermissionCodes.CreatePurchaseReturns,
        PurchasingPermissionCodes.ConfirmPurchaseReturns);

    private async Task<ReceiptContext> CreateReceiptAsync(decimal quantity)
    {
        var productId=Guid.NewGuid();await CreateProductAsync(productId);
        var now=new DateTimeOffset(2026,8,1,10,0,0,TimeSpan.FromHours(-5));
        var request=new ConfirmGoodsReceiptRequest(Guid.NewGuid(),fixture.BusinessId,
            fixture.WarehouseId,fixture.SupplierId,$"FC-{Guid.NewGuid():N}",now.AddDays(-1),
            now,true,now.AddDays(30),"COP","Entrada para devolución",
            [new GoodsReceiptLineRequest(1,productId,"Producto devolución",quantity,
                6_000m,quantity*1_000m,"01",19m,
                PurchasingTaxTreatments.DeductibleInputVat)]);
        using var client=fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using var message=new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/goods-receipts/confirm") { Content=JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key",$"receipt-return-{request.DocumentId:N}");
        using var response=await client.SendAsync(message);response.EnsureSuccessStatusCode();
        var acceptance=await response.Content.ReadFromJsonAsync<GoodsReceiptAcceptance>()
            ?? throw new InvalidOperationException("The goods receipt acceptance is empty.");
        return new ReceiptContext(acceptance,productId);
    }

    private async Task CreateProductAsync(Guid productId)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText="""
            INSERT dbo.Products
              (ProductId,BusinessId,Source,Sku,Name,UnitPrice,Currency,
               ManageStock,IsActive,CreatedAt)
            VALUES(@ProductId,@BusinessId,0,@Sku,N'Producto devolución',10000,
               N'COP',1,1,SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,
               ValidFrom,TargetMarginPercent,RoundingIncrement,RoundingMode,
               IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ProductId,10000,N'COP','2026-01-01',
               20,1,N'Nearest',1,SYSDATETIMEOFFSET());
            INSERT dbo.SupplierProducts
              (SupplierProductId,BusinessId,ProductId,SupplierId,SupplierProductCode,
               IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ProductId,@SupplierId,@Sku,1,1,
               SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@ProductId",productId);
        command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);
        command.Parameters.AddWithValue("@SupplierId",fixture.SupplierId);
        command.Parameters.AddWithValue("@Sku",$"RET-{productId:N}");
        await command.ExecuteNonQueryAsync();
    }

    private ConfirmPurchaseReturnRequest CreateReturn(Guid receiptId,decimal quantity)=>new(
        Guid.NewGuid(),fixture.BusinessId,receiptId,
        new DateTimeOffset(2026,8,2,9,0,0,TimeSpan.FromHours(-5)),
        "QualityIssue","Devolución verificada",[new PurchaseReturnLineRequest(1,quantity)]);

    private static Task<HttpResponseMessage> SendAsync(HttpClient client,
        ConfirmPurchaseReturnRequest request,string key)
    {
        var message=new HttpRequestMessage(HttpMethod.Post,
            "/api/commerce/v1/purchase-returns/confirm") { Content=JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key",key);return client.SendAsync(message);
    }

    private async Task<decimal> InventoryAsync(Guid productId,string column)
    {
        Assert.Contains(column,new[]{"QuantityOnHand","InventoryValue"});
        return await ScalarAsync<decimal>(
            $"SELECT [{column}] FROM dbo.InventoryBalances WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId",
            productId);
    }

    private async Task<T> ScalarAsync<T>(string sql,Guid id)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText=sql;command.Parameters.AddWithValue("@Id",id);
        command.Parameters.AddWithValue("@BusinessId",fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId",fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId",id);
        return (T)Convert.ChangeType(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("Expected scalar missing."),typeof(T));
    }

    private async Task<int> CountAsync(string table,string column,Guid id)
    {
        var allowed=new Dictionary<string,string>(StringComparer.Ordinal)
        {
            ["SupplierCredits"]="SourcePurchaseReturnId",
            ["InventoryMovements"]="DocumentId",
            ["PurchaseReturnFinancialEffects"]="PurchaseReturnId",
            ["AccountingPostingJobs"]="SourceDocumentId",
            ["ServerOutboxMessages"]="DocumentId",
            ["PayableTransactions"]="SourceDocumentId",
            ["PurchaseReturns"]="PurchaseReturnId",
            ["DocumentProcessingJobs"]="DocumentId"
        };
        Assert.True(allowed.TryGetValue(table,out var expected));Assert.Equal(expected,column);
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText=$"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id";
        command.Parameters.AddWithValue("@Id",id);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private async Task ExecuteAsync(string sql,Guid id)
    {
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();await using var command=connection.CreateCommand();
        command.CommandText=sql;command.Parameters.AddWithValue("@Id",id);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record ReceiptContext(GoodsReceiptAcceptance Acceptance,Guid ProductId);
}