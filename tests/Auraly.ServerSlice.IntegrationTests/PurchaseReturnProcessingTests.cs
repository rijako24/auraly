using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Auraly.Application.Payables;
using Auraly.Contracts.Payables;
using Auraly.Domain.Payables;
using Auraly.BuildingBlocks.Domain.Payments;
using Auraly.Commerce.Accounting.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class PurchaseReturnProcessingTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Anticipated_financial_discount_reduces_each_return_settlement_without_creating_a_supplier_credit()
    {
        var receipt = await CreateReceiptAsync(10m, financialDiscount:
            new GoodsReceiptFinancialDiscountRequest(10m, null, true));
        Assert.Equal(54_500m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            receipt.Acceptance.DocumentId));
        using var client = CreateReturnClient();
        var first = CreateReturn(receipt.Acceptance.DocumentId, 4m);
        using (var response = await SendAsync(client, first, first.ReturnId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(2_000m, await ScalarAsync<decimal>(
            "SELECT FinancialDiscountAmount FROM dbo.PurchaseReturnLines WHERE PurchaseReturnId=@Id", first.ReturnId));
        Assert.Equal(21_800m, await ScalarAsync<decimal>(
            "SELECT PayableCreditAmount FROM dbo.PurchaseReturnFinancialEffects WHERE PurchaseReturnId=@Id", first.ReturnId));
        Assert.Equal(21_800m, await ScalarAsync<decimal>(
            "SELECT DebitTotal FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'PurchaseReturn'", first.ReturnId));
        Assert.Equal(32_700m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            receipt.Acceptance.DocumentId));
        Assert.Equal(27_000m, await InventoryAsync(receipt.ProductId, "InventoryValue"));
        using (var replay = await SendAsync(client, first, first.ReturnId.ToString("N")))
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, await CountAsync("PurchaseReturnFinancialEffects", "PurchaseReturnId", first.ReturnId));

        var second = CreateReturn(receipt.Acceptance.DocumentId, 6m);
        using (var response = await SendAsync(client, second, second.ReturnId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(3_000m, await ScalarAsync<decimal>(
            "SELECT FinancialDiscountAmount FROM dbo.PurchaseReturnLines WHERE PurchaseReturnId=@Id", second.ReturnId));
        Assert.Equal(32_700m, await ScalarAsync<decimal>(
            "SELECT PayableCreditAmount FROM dbo.PurchaseReturnFinancialEffects WHERE PurchaseReturnId=@Id", second.ReturnId));
        Assert.Equal(32_700m, await ScalarAsync<decimal>(
            "SELECT CreditTotal FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'PurchaseReturn'", second.ReturnId));
        Assert.Equal(0m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
            receipt.Acceptance.DocumentId));
        Assert.Equal(0m, await InventoryAsync(receipt.ProductId, "InventoryValue"));
        Assert.Equal(0, await CountAsync("SupplierCredits", "SourcePurchaseReturnId", first.ReturnId));
        Assert.Equal(0, await CountAsync("SupplierCredits", "SourcePurchaseReturnId", second.ReturnId));
    }

    [Fact]
    public async Task Invalid_last_batch_line_rolls_back_acceptance_and_the_same_command_can_then_complete_once()
    {
        var receipt = await CreateReceiptAsync(10m, 20);
        var request = CreateReturn(receipt.Acceptance.DocumentId, 4m) with
        {
            Lines = Enumerable.Range(1, 20).Select(number => new PurchaseReturnLineRequest(number, 4m)).ToArray()
        };
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IPurchaseReturnStore>();
        var user = new PurchasingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { PurchasingPermissionCodes.CreatePurchaseReturns, PurchasingPermissionCodes.ConfirmPurchaseReturns });
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(database,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.PurchaseReturns", StringComparison.Ordinal)
                && command.Parameters["@Id"].Value is Guid id && id == request.ReturnId,
            beforeExecute: command =>
            {
                var lines = System.Text.Json.Nodes.JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                lines[^1]!["RecognizedUnitCost"] = -1m;
                command.Parameters["@Lines"].Value = lines.ToJsonString();
            }))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => store.AcceptAsync(user,
                request.ReturnId.ToString("N"), request, default));
            Assert.Equal(547, error.Number);
            Assert.Contains("CK_PurchaseReturnLines_Amounts", error.Message);
            Assert.Equal(1, fault.Count);
        }
        Assert.Equal(0, await CountAsync("PurchaseReturns", "PurchaseReturnId", request.ReturnId));
        Assert.Equal(0, await CountAsync("DocumentProcessingJobs", "DocumentId", request.ReturnId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PurchaseReturnLines WHERE PurchaseReturnId=@Id", request.ReturnId));
        Assert.Equal(200m, await InventoryAsync(receipt.ProductId, "QuantityOnHand"));
        Assert.Equal(1_190_000m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'", receipt.Acceptance.DocumentId));
        using var client = CreateReturnClient();
        using (var accepted = await SendAsync(client, request, request.ReturnId.ToString("N"))) accepted.EnsureSuccessStatusCode();
        using (var replay = await SendAsync(client, request, request.ReturnId.ToString("N"))) replay.EnsureSuccessStatusCode();
        Assert.Equal(120m, await InventoryAsync(receipt.ProductId, "QuantityOnHand"));
        Assert.Equal(20, await CountAsync("InventoryMovements", "DocumentId", request.ReturnId));
        Assert.Equal(1, await CountAsync("PayableTransactions", "SourceDocumentId", request.ReturnId));
    }

    [Fact]
    public async Task One_and_twenty_return_lines_share_one_inventory_batch_and_constant_round_trips()
    {
        var commands = new List<int>();
        foreach (var lineCount in new[] { 1, 20 })
        {
            var receipt = await CreateReceiptAsync(10m, lineCount);
            var request = CreateReturn(receipt.Acceptance.DocumentId, 4m) with
            {
                Lines = Enumerable.Range(1, lineCount).Select(number => new PurchaseReturnLineRequest(number, 4m)).ToArray()
            };
            var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
            using var client = CreateReturnClient();
            using (var count = new InventoryBalanceProcessingTests.CommandCounter(database))
            {
                using var response = await SendAsync(client, request, request.ReturnId.ToString("N"));
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                commands.Add(count.Count);
            }
            Assert.Equal(6m * lineCount, await InventoryAsync(receipt.ProductId, "QuantityOnHand"));
            Assert.Equal(30_000m * lineCount, await InventoryAsync(receipt.ProductId, "InventoryValue"));
            Assert.Equal(35_700m * lineCount, await ScalarAsync<decimal>(
                "SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'GoodsReceipt'",
                receipt.Acceptance.DocumentId));
            Assert.Equal(lineCount, await CountAsync("InventoryMovements", "DocumentId", request.ReturnId));
            Assert.Equal(1, await CountAsync("PayableTransactions", "SourceDocumentId", request.ReturnId));
            Assert.InRange(await ScalarAsync<long>("""
                SELECT DATEDIFF_BIG(microsecond,StartedAt,CompletedAt) FROM dbo.DocumentProcessingJobs
                WHERE DocumentId=@Id AND DocumentType=N'PurchaseReturn';
                """, request.ReturnId), 0L, 2_000_000L);
            using var replayCount = new InventoryBalanceProcessingTests.CommandCounter(database,
                commandFilter: command => command.CommandText.Contains("INSERT dbo.InventoryMovements(", StringComparison.Ordinal));
            using var replay = await SendAsync(client, request, request.ReturnId.ToString("N"));
            replay.EnsureSuccessStatusCode();
            Assert.Equal(0, replayCount.Count);
            Assert.Equal(6m * lineCount, await InventoryAsync(receipt.ProductId, "QuantityOnHand"));
        }
        output.WriteLine("Devolución completa de una/veinte líneas: {0}/{1} comandos SQL.", commands[0], commands[1]);
        Assert.True(commands[0] > 0, "Se requiere evidencia del camino SQL real.");
        Assert.True(commands[1] <= commands[0], $"Una línea: {commands[0]} SQL; veinte: {commands[1]} SQL.");
    }

    [Fact]
    public async Task Return_with_missing_original_account_keeps_the_debt_unchanged_until_explicit_retry()
    {
        var receipt = await CreateReceiptAsync(10m);
        var payableId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.Acceptance.DocumentId);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Guid entryId; int lineNumber;
        await using (var original = new SqlCommand("SELECT OpeningEntryId,OpeningEntryLineNumber FROM dbo.Payables WHERE PayableId=@Id", connection))
        {
            original.Parameters.AddWithValue("@Id", payableId);
            await using var reader = await original.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); entryId = reader.GetGuid(0); lineNumber = reader.GetInt32(1);
        }
        var returned = CreateReturn(receipt.Acceptance.DocumentId, 4m);
        try
        {
            await ExecuteAsync("UPDATE dbo.Payables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE PayableId=@Id", payableId);
            using var client = CreateReturnClient();
            using var response = await SendAsync(client, returned, returned.ReturnId.ToString("N"));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal("OriginalPositionAccountMissing", await ScalarAsync<string>(
                "SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", returned.ReturnId));
            Assert.Contains("No se aplicó la devolución", await ScalarAsync<string>(
                "SELECT LastErrorMessage FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", returned.ReturnId));
            Assert.Equal(59_500m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
            Assert.Equal(0, await CountAsync("PurchaseReturnFinancialEffects", "PurchaseReturnId", returned.ReturnId));
            Assert.Equal(0, await CountAsync("PayableTransactions", "SourceDocumentId", returned.ReturnId));
            Assert.Equal(0, await CountAsync("SupplierCredits", "SourcePurchaseReturnId", returned.ReturnId));
        }
        finally
        {
            await using var restore = new SqlCommand("UPDATE dbo.Payables SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=@Line WHERE PayableId=@Id", connection);
            restore.Parameters.AddWithValue("@Id", payableId); restore.Parameters.AddWithValue("@EntryId", entryId);
            restore.Parameters.AddWithValue("@Line", lineNumber); await restore.ExecuteNonQueryAsync();
        }
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        await processor.ProcessAsync(returned.ReturnId, "PurchaseReturn", fixture.BusinessId, default);
        await processor.ProcessAsync(returned.ReturnId, "PurchaseReturn", fixture.BusinessId, default);
        Assert.Equal(35_700m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(1, await CountAsync("PayableTransactions", "SourceDocumentId", returned.ReturnId));
        Assert.Equal(1, await CountAsync("InventoryMovements", "DocumentId", returned.ReturnId));
        await PortfolioOriginalAccountScenario.AssertReturnOriginalLineAsync(fixture, false,
            receipt.Acceptance.DocumentId, returned.ReturnId, 23_800m);
    }

    [Fact]
    public async Task Return_uses_the_original_payable_auxiliary_after_the_mapping_changes()
    {
        var receipt = await CreateReceiptAsync(10m);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreatePurchaseReturns,
            PurchasingPermissionCodes.ConfirmPurchaseReturns, Auraly.Commerce.Accounting.Contracts.AccountingPermissionCodes.Configure);
        var alternate = await PortfolioOriginalAccountScenario.RemapAsync(fixture, client, false);
        try
        {
            var returned = CreateReturn(receipt.Acceptance.DocumentId, 4m);
            using var response = await SendAsync(client, returned, returned.ReturnId.ToString("N"));
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
            await PortfolioOriginalAccountScenario.AssertReturnOriginalLineAsync(fixture, false,
                receipt.Acceptance.DocumentId, returned.ReturnId, 23_800m);
            using var replay = await SendAsync(client, returned, returned.ReturnId.ToString("N"));
            replay.EnsureSuccessStatusCode();
            await PortfolioOriginalAccountScenario.AssertReturnOriginalLineAsync(fixture, false,
                receipt.Acceptance.DocumentId, returned.ReturnId, 23_800m);
        }
        finally { await PortfolioOriginalAccountScenario.RestoreAsync(fixture, alternate); }
    }

    [Theory]
    [InlineData(0, false, 59_500, 23_800, 19_500, 4_300)]
    [InlineData(10, false, 59_500, 23_800, 19_500, 4_300)]
    [InlineData(10, true, 54_500, 21_800, 14_500, 7_300)]
    public async Task Return_preserves_an_accepted_payment_reservation_and_credits_only_the_available_debt(
        int discountPercent, bool anticipateInCost, int originalPayable, int returnedSettlement,
        int expectedPayableCredit, int expectedSupplierCredit)
    {
        var receipt = await CreateReceiptAsync(10m, financialDiscount: discountPercent == 0 ? null :
            new GoodsReceiptFinancialDiscountRequest(discountPercent, null, anticipateInCost));
        var payableId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.Acceptance.DocumentId);
        Assert.Equal(originalPayable, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        await ExecuteAsync("""
            IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@Id AND DocumentType=N'PayablePayment' AND IsActive=1)
              INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,
                RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@Id,NULL,N'PayablePayment',N'PGP',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """, fixture.BusinessId);
        using var scope = fixture.CreateScope();
        var paymentId = Guid.NewGuid();
        var user = new PayablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { PayablesPermissionCodes.RegisterPayment });
        var payment = new ConfirmSupplierPaymentRequest(paymentId, fixture.BusinessId, fixture.SupplierId,
            new(2026, 8, 2, 10, 0, 0, TimeSpan.FromHours(-5)), "COP", "Abono aceptado antes de la devolución",
            [new(payableId, 40_000m)], [new("Cash", 40_000m, 40_000m)]);
        // Exercise the real native acceptance owner without dispatching posting yet.
        var accepted = await scope.ServiceProvider.GetRequiredService<IPayablesStore>().AcceptPaymentAsync(user,
            paymentId.ToString("N"), payment, PayableSettlement.Create([new(payableId, 40_000m)]),
            PaymentTenderBreakdown.Create([new("Cash", 40_000m, 40_000m)], 40_000m, 1, PortfolioPaymentMethods.Supported), default);
        Assert.Equal("Accepted", accepted.Status);
        Assert.Equal(originalPayable, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));

        var returned = CreateReturn(receipt.Acceptance.DocumentId, 4m);
        using var client = CreateReturnClient();
        using (var response = await SendAsync(client, returned, returned.ReturnId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedPayableCredit, await ScalarAsync<decimal>(
            "SELECT PayableCreditAmount FROM dbo.PurchaseReturnFinancialEffects WHERE PurchaseReturnId=@Id", returned.ReturnId));
        Assert.Equal(expectedSupplierCredit, await ScalarAsync<decimal>(
            "SELECT SupplierCreditAmount FROM dbo.PurchaseReturnFinancialEffects WHERE PurchaseReturnId=@Id", returned.ReturnId));
        Assert.Equal(returnedSettlement, await ScalarAsync<decimal>(
            "SELECT PayableCreditAmount+SupplierCreditAmount FROM dbo.PurchaseReturnFinancialEffects WHERE PurchaseReturnId=@Id", returned.ReturnId));
        Assert.Equal(40_000m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(paymentId, "PayablePayment", fixture.BusinessId, default);
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal("Processed", await ScalarAsync<string>("SELECT Status FROM dbo.SupplierPayments WHERE PaymentId=@Id", paymentId));
        Assert.Equal(expectedSupplierCredit, await ScalarAsync<decimal>("SELECT AvailableAmount FROM dbo.SupplierCredits WHERE SourcePurchaseReturnId=@Id", returned.ReturnId));
        using (var replay = await SendAsync(client, returned, returned.ReturnId.ToString("N"))) replay.EnsureSuccessStatusCode();
        await scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>().ProcessAsync(paymentId, "PayablePayment", fixture.BusinessId, default);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Payment'", paymentId));
        await PortfolioOriginalAccountScenario.AssertSupplierCreditOpeningAsync(fixture, returned.ReturnId, "PurchaseReturn");
    }

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
        Assert.Equal(1,await ScalarAsync<int>(
            "SELECT COUNT(*) FROM reporting.SalesReportingJobs WHERE SourceDocumentId=@Id AND SourceDocumentType=N'PurchaseReturn'",
            request.ReturnId));
        Assert.Equal(1,await ScalarAsync<int>(
            "SELECT COUNT(*) FROM reporting.PurchaseReportDocuments WHERE SourceDocumentId=@Id AND SourceDocumentType=N'PurchaseReturn'",
            request.ReturnId));

        using(var replayResponse=await SendAsync(client,request,$"return-{request.ReturnId:N}"))
        {
            Assert.Equal(HttpStatusCode.Accepted,replayResponse.StatusCode);
            var replay=await replayResponse.Content.ReadFromJsonAsync<PurchaseReturnAcceptance>();
            Assert.NotNull(replay);Assert.True(replay.IdempotentReplay);
        }
        Assert.Equal(1,await CountAsync("InventoryMovements","DocumentId",request.ReturnId));
        Assert.Equal(1,await CountAsync("PayableTransactions","SourceDocumentId",request.ReturnId));
        Assert.Equal(1,await CountAsync("ServerOutboxMessages","DocumentId",request.ReturnId));

        using var list=await client.GetAsync($"/api/commerce/v1/purchase-returns/receipts?page=1&pageSize=25&search={Uri.EscapeDataString(receipt.Acceptance.DocumentNumber)}");
        Assert.Equal(HttpStatusCode.OK,list.StatusCode);
        var page=await list.Content.ReadFromJsonAsync<ReturnableGoodsReceiptPage>();
        Assert.NotNull(page);Assert.Equal(1,page.TotalCount);
        Assert.Equal(receipt.Acceptance.DocumentId,Assert.Single(page.Items).GoodsReceiptId);
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
        await PortfolioOriginalAccountScenario.AssertSupplierCreditOpeningAsync(fixture, request.ReturnId, "PurchaseReturn");
        using (var replay = await SendAsync(client, request, $"credit-{request.ReturnId:N}"))
            Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        await PortfolioOriginalAccountScenario.AssertSupplierCreditOpeningAsync(fixture, request.ReturnId, "PurchaseReturn");
        Assert.Equal(await ScalarAsync<Guid>(
                "SELECT PartySiteId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.Acceptance.DocumentId),
            await ScalarAsync<Guid>(
                "SELECT PartySiteId FROM dbo.SupplierCredits WHERE SourcePurchaseReturnId=@Id", request.ReturnId));
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

    private async Task<ReceiptContext> CreateReceiptAsync(decimal quantity, int lineCount = 1,
        GoodsReceiptFinancialDiscountRequest? financialDiscount = null)
    {
        var productId=Guid.NewGuid();await CreateProductAsync(productId);
        var now=new DateTimeOffset(2026,8,1,10,0,0,TimeSpan.FromHours(-5));
        var request=new ConfirmGoodsReceiptRequest(Guid.NewGuid(),fixture.BusinessId,
            fixture.WarehouseId,fixture.SupplierId,$"FC-{Guid.NewGuid():N}",now.AddDays(-1),
            now,true,now.AddDays(30),"COP","Entrada para devolución",
            Enumerable.Range(1, lineCount).Select(number => new GoodsReceiptLineRequest(number,productId,"Producto devolución",quantity,
                6_000m,quantity*1_000m,"01",19m,
                PurchasingTaxTreatments.DeductibleInputVat)).ToArray()) with
        { FinancialDiscount = financialDiscount };
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
              (ProductId,TenantId,Source,Sku,Name,Currency,
               ManageStock,IsActive,CreatedAt)
            VALUES(@ProductId,@TenantId,0,@Sku,N'Producto devolución',
               N'COP',1,1,SYSDATETIMEOFFSET());
            INSERT dbo.ProductPrices
              (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,
               ValidFrom,TargetMarginPercent,RoundingIncrement,RoundingMode,
               IsActive,CreatedAt)
            VALUES(NEWID(),@BusinessId,@ProductId,10000,N'COP','2026-01-01',
               20,1,N'Nearest',1,SYSDATETIMEOFFSET());
            INSERT dbo.SupplierProducts
              (SupplierProductId,TenantId,ProductId,SupplierId,SupplierProductCode,
               IsPrimary,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@ProductId,@SupplierId,@Sku,1,1,
               SYSDATETIMEOFFSET());
            """;
        command.Parameters.AddWithValue("@ProductId",productId);
        command.Parameters.AddWithValue("@TenantId",fixture.TenantId);
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
