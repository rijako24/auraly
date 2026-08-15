using System.Data;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PayablesVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Goods_receipt_to_supplier_payment_is_scoped_idempotent_and_accounted_once()
    {
        var payableProductId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(payableProductId);
        var occurredAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, fixture.SupplierId,
            $"PAYABLE-{Guid.NewGuid():N}", occurredAt.AddDays(-1), occurredAt,
            true, occurredAt.AddDays(30), "COP", "Obligacion E2E",
            [new GoodsReceiptLineRequest(
                1, payableProductId, "Producto de cartera", 10m, 10_000m,
                0m, "00", 0m, PurchasingTaxTreatments.NotApplicable)]);
        using var client = fixture.CreateAdminClient(
            PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts,
            PayablesPermissionCodes.Read,
            PayablesPermissionCodes.RegisterPayment);
        using (var response = await SendAsync(
                   client, "/api/commerce/v1/goods-receipts/confirm", receipt,
                   $"payables-receipt-{receipt.DocumentId:N}"))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var payableId = await ScalarAsync<Guid>(
            "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id",
            receipt.DocumentId);

        using (var pageResponse = await client.GetAsync(
                   "/api/commerce/v1/payables?page=1&pageSize=20&status=Open"))
        {
            Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
            var page = await pageResponse.Content.ReadFromJsonAsync<PayablePage>();
            Assert.NotNull(page);
            var payable = Assert.Single(page.Items.Where(item =>
                item.PayableId == payableId));
            Assert.Equal(100_000m, payable.OutstandingAmount);
            Assert.Equal(fixture.SupplierId, payable.SupplierId);
        }

        using (var detailResponse = await client.GetAsync(
                   $"/api/commerce/v1/payables/{payableId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
            var detail = await detailResponse.Content.ReadFromJsonAsync<PayableDetail>();
            Assert.NotNull(detail);
            Assert.Single(detail.Transactions);
            Assert.Equal("Opening", detail.Transactions[0].Type);
        }

        var payment = new ConfirmSupplierPaymentRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
            occurredAt.AddHours(1), "COP", SupplierPaymentMethods.BankTransfer,
            "TRX-9001", "Abono por transferencia",
            [new SupplierPaymentAllocationRequest(payableId, 40_000m)]);
        var key = $"payables-payment-{payment.PaymentId:N}";
        using (var response = await SendAsync(
                   client, "/api/commerce/v1/payable-payments/confirm", payment, key))
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            var acceptance = await response.Content.ReadFromJsonAsync<SupplierPaymentAcceptance>();
            Assert.NotNull(acceptance);
            Assert.StartsWith("PGP", acceptance.DocumentNumber);
            Assert.False(acceptance.IdempotentReplay);
        }

        Assert.Equal("Processed", await ScalarAsync<string>(
            "SELECT Status FROM dbo.SupplierPayments WHERE PaymentId=@Id", payment.PaymentId));
        Assert.Equal(60_000m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal("PartiallyPaid", await ScalarAsync<string>(
            "SELECT Status FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(1, await CountAsync(
            "SupplierPaymentApplications", "PaymentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "PayableTransactions", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "ServerOutboxMessages", "DocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", payment.PaymentId));
        Assert.True(await PayloadHashMatchesAsync(payment.PaymentId));
        Assert.Equal(40_000m, await AccountAmountAsync(payment.PaymentId, "220505", true));
        Assert.Equal(40_000m, await AccountAmountAsync(payment.PaymentId, "111020", false));

        using (var duplicate = await SendAsync(
                   client, "/api/commerce/v1/payable-payments/confirm", payment, key))
        {
            Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
            var acceptance = await duplicate.Content.ReadFromJsonAsync<SupplierPaymentAcceptance>();
            Assert.NotNull(acceptance);
            Assert.True(acceptance.IdempotentReplay);
        }
        Assert.Equal(1, await CountAsync(
            "PayableTransactions", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync(
            "AccountingEntries", "SourceDocumentId", payment.PaymentId));

        var concurrentA = payment with
        {
            PaymentId = Guid.NewGuid(),
            Allocations = [new SupplierPaymentAllocationRequest(payableId, 40_000m)]
        };
        var concurrentB = concurrentA with { PaymentId = Guid.NewGuid() };
        var concurrentResponses = await Task.WhenAll(
            SendAsync(client, "/api/commerce/v1/payable-payments/confirm", concurrentA,
                $"concurrent-{concurrentA.PaymentId:N}"),
            SendAsync(client, "/api/commerce/v1/payable-payments/confirm", concurrentB,
                $"concurrent-{concurrentB.PaymentId:N}"));
        try
        {
            Assert.Single(concurrentResponses.Where(response =>
                response.StatusCode == HttpStatusCode.Accepted));
            Assert.Single(concurrentResponses.Where(response =>
                response.StatusCode == HttpStatusCode.Conflict));
        }
        finally
        {
            foreach (var response in concurrentResponses) response.Dispose();
        }
        Assert.Equal(20_000m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));

        var overpayment = payment with
        {
            PaymentId = Guid.NewGuid(),
            Allocations = [new SupplierPaymentAllocationRequest(payableId, 20_001m)]
        };
        using (var response = await SendAsync(
                   client, "/api/commerce/v1/payable-payments/confirm", overpayment,
                   $"overpay-{overpayment.PaymentId:N}"))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountAsync(
            "SupplierPayments", "PaymentId", overpayment.PaymentId));
    }

    [Fact]
    public async Task Payables_endpoints_enforce_permissions_and_authenticated_business()
    {
        using var denied = fixture.CreateAdminClient();
        using var list = await denied.GetAsync(
            "/api/commerce/v1/payables?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var request = new ConfirmSupplierPaymentRequest(
            Guid.NewGuid(), Guid.NewGuid(), fixture.SupplierId,
            DateTimeOffset.UtcNow, "COP", SupplierPaymentMethods.Cash,
            null, null, [new SupplierPaymentAllocationRequest(Guid.NewGuid(), 1m)]);
        using var scoped = fixture.CreateAdminClient(PayablesPermissionCodes.RegisterPayment);
        using var response = await SendAsync(
            scoped, "/api/commerce/v1/payable-payments/confirm", request,
            $"wrong-business-{request.PaymentId:N}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var invalid = request with
        {
            BusinessId = fixture.BusinessId,
            CurrencyCode = null!,
            PaymentMethod = null!,
            Allocations = null!
        };
        using var invalidResponse = await SendAsync(
            scoped, "/api/commerce/v1/payable-payments/confirm", invalid,
            $"invalid-body-{invalid.PaymentId:N}");
        Assert.Equal(HttpStatusCode.BadRequest, invalidResponse.StatusCode);
    }

    private async Task ConfigureSeriesAndAccountingAsync(Guid productId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable);
        try
        {
            var seriesId = Guid.NewGuid();
            await using (var series = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=N'PayablePayment' AND IsActive=1)
                  INSERT dbo.DocumentSeries
                    (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,
                     Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                  VALUES(@SeriesId,@BusinessId,NULL,N'PayablePayment',N'PGP',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
                """, connection, transaction))
            {
                series.Parameters.AddWithValue("@SeriesId", seriesId);
                series.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                await series.ExecuteNonQueryAsync();
            }
            await using (var product = new SqlCommand("""
                INSERT dbo.Products
                  (ProductId,BusinessId,Source,Sku,Name,UnitPrice,Currency,ManageStock,IsActive,CreatedAt)
                VALUES(@ProductId,@BusinessId,0,@Sku,N'Producto aislado de cartera',10000,N'COP',1,1,SYSUTCDATETIME());
                INSERT dbo.ProductPrices
                  (ProductPriceId,BusinessId,ProductId,Amount,CurrencyCode,ValidFrom,
                   TargetMarginPercent,RoundingIncrement,RoundingMode,IsActive,CreatedAt)
                VALUES(NEWID(),@BusinessId,@ProductId,10000,N'COP','2026-01-01',
                       20,1,N'Nearest',1,SYSDATETIMEOFFSET());
                INSERT dbo.SupplierProducts
                  (SupplierProductId,BusinessId,ProductId,SupplierId,SupplierProductCode,IsPrimary,IsActive,CreatedAt)
                VALUES(NEWID(),@BusinessId,@ProductId,@SupplierId,@SupplierCode,1,1,SYSDATETIMEOFFSET());
                """, connection, transaction))
            {
                product.Parameters.AddWithValue("@ProductId", productId);
                product.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                product.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
                product.Parameters.AddWithValue("@Sku", $"PAY-{productId:N}");
                product.Parameters.AddWithValue("@SupplierCode", $"PAY-SUP-{productId:N}");
                await product.ExecuteNonQueryAsync();
            }
            await EnsureAccountAsync(connection, transaction, "143505", "Inventarios", "Asset", false);
            await EnsureAccountAsync(connection, transaction, "220505", "Proveedores", "Liability", true);
            await EnsureAccountAsync(connection, transaction, "111020", "Bancos", "Asset", false);
            await using (var period = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.AccountingPeriods WHERE TenantId=@TenantId AND Status=N'Open' AND StartsOn<='2026-08-02' AND EndsOn>='2026-08-02')
                  INSERT dbo.AccountingPeriods(PeriodId,TenantId,Name,StartsOn,EndsOn,Status,CreatedAt)
                  VALUES(NEWID(),@TenantId,N'Periodo Payables 2026','2026-01-01','2026-12-31',N'Open',SYSDATETIMEOFFSET());
                """, connection, transaction))
            {
                period.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                await period.ExecuteNonQueryAsync();
            }
            await EnsureMappingAsync(connection, transaction, AccountingCategories.Inventory, "143505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.AccountsPayable, "220505");
            await EnsureMappingAsync(connection, transaction, AccountingCategories.Bank, "111020");
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task EnsureAccountAsync(
        SqlConnection connection, SqlTransaction transaction, string code,
        string name, string type, bool requiresParty)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccounts WHERE TenantId=@TenantId AND Code=@Code)
              INSERT dbo.AccountingAccounts
                (AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,CreatedAt)
              VALUES(NEWID(),@TenantId,@Code,@Name,@Type,1,@RequiresParty,1,SYSDATETIMEOFFSET());
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Type", type);
        command.Parameters.AddWithValue("@RequiresParty", requiresParty);
        await command.ExecuteNonQueryAsync();
    }

    private async Task EnsureMappingAsync(
        SqlConnection connection, SqlTransaction transaction, string category, string code)
    {
        await using var command = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND BusinessId IS NULL AND Category=@Category AND EffectiveFrom='2026-01-01')
              INSERT dbo.AccountingAccountMappings
                (MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,CreatedAt)
              SELECT NEWID(),@TenantId,NULL,@Category,AccountId,'2026-01-01',SYSDATETIMEOFFSET()
              FROM dbo.AccountingAccounts WHERE TenantId=@TenantId AND Code=@Code;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@Category", category);
        command.Parameters.AddWithValue("@Code", code);
        await command.ExecuteNonQueryAsync();
    }

    private static HttpRequestMessage CreateMessage<T>(string url, T request, string key)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, url)
        { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", key);
        return message;
    }

    private static async Task<HttpResponseMessage> SendAsync<T>(
        HttpClient client, string url, T request, string key)
    {
        using var message = CreateMessage(url, request, key);
        return await client.SendAsync(message);
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }

    private async Task<int> CountAsync(string table, string column, Guid id)
    {
        Assert.Contains($"{table}:{column}", new[]
        {
            "SupplierPaymentApplications:PaymentId",
            "PayableTransactions:SourceDocumentId",
            "ServerOutboxMessages:DocumentId",
            "AccountingEntries:SourceDocumentId",
            "SupplierPayments:PaymentId"
        });
        return await ScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id", id);
    }

    private async Task<decimal> AccountAmountAsync(Guid documentId, string code, bool debit)
    {
        Assert.Contains(code, new[] { "220505", "111020" });
        var column = debit ? "Debit" : "Credit";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand($"""
            SELECT COALESCE(SUM(l.[{column}]),0)
            FROM dbo.AccountingEntries e
            INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            INNER JOIN dbo.AccountingAccounts a ON a.AccountId=l.AccountId
            WHERE e.SourceDocumentId=@Id AND a.Code=@Code;
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@Code", code);
        return Convert.ToDecimal(await command.ExecuteScalarAsync());
    }

    private async Task<bool> PayloadHashMatchesAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT PayloadJson,PayloadHash FROM dbo.DocumentProcessingPayloads
            WHERE DocumentId=@Id AND DocumentType=N'PayablePayment';
            """, connection);
        command.Parameters.AddWithValue("@Id", documentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        var expected = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(reader.GetString(0)));
        return expected.AsSpan().SequenceEqual(reader.GetFieldValue<byte[]>(1));
    }
}
