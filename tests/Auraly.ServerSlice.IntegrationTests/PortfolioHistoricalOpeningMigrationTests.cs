using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ReceivablesVerticalSliceTests
{
    [Fact]
    public async Task Historical_link_migration_rolls_back_when_the_original_account_is_not_postable()
    {
        var (customerId, userId, _) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId, ReceivablesPermissionCodes.ManageCredit);
        var positionId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var import = new ImportPreexistingReceivablesRequest(fixture.BusinessId,
            [new(positionId, customerId, null, null, $"HIST-INVALID-{positionId:N}", at,
                at.AddDays(30), 10_000m, await AccountIdByCodeAsync("413595"), null)]);
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import", import))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var accountId = await ScalarAsync<Guid>("""
            SELECT line.AccountId FROM dbo.Receivables balance
            JOIN dbo.AccountingEntryLines line ON line.EntryId=balance.OpeningEntryId
              AND line.LineNumber=balance.OpeningEntryLineNumber
            WHERE balance.ReceivableId=@Id;
            """, positionId);
        var openingEntryId = await ScalarAsync<Guid>(
            "SELECT OpeningEntryId FROM dbo.Receivables WHERE ReceivableId=@Id", positionId);
        var openingLineNumber = await ScalarAsync<int>(
            "SELECT OpeningEntryLineNumber FROM dbo.Receivables WHERE ReceivableId=@Id", positionId);
        await using (var arrange = new SqlCommand("""
            UPDATE dbo.Receivables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE ReceivableId=@Id;
            UPDATE dbo.AccountingAccounts SET IsActive=0 WHERE AccountId=@AccountId;
            """, connection))
        {
            arrange.Parameters.AddWithValue("@Id", positionId);
            arrange.Parameters.AddWithValue("@AccountId", accountId);
            await arrange.ExecuteNonQueryAsync();
        }
        try
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => fixture.ApplyMigrationAsync(
                "20261027_LinkPostedPortfolioOpenings.sql", connection));
            Assert.Equal(51871, error.Number);
            Assert.Equal(0, await ScalarAsync<int>(
                "SELECT COUNT(*) FROM dbo.Receivables WHERE ReceivableId=@Id AND OpeningEntryId IS NOT NULL", positionId));
        }
        finally
        {
            await using var restore = new SqlCommand("""
                UPDATE dbo.AccountingAccounts SET IsActive=1 WHERE AccountId=@AccountId;
                UPDATE dbo.Receivables SET OpeningEntryId=@EntryId,OpeningEntryLineNumber=@LineNumber
                WHERE ReceivableId=@Id;
                """, connection);
            restore.Parameters.AddWithValue("@AccountId", accountId);
            restore.Parameters.AddWithValue("@EntryId", openingEntryId);
            restore.Parameters.AddWithValue("@LineNumber", openingLineNumber);
            restore.Parameters.AddWithValue("@Id", positionId);
            await restore.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Historical_posted_receivable_is_linked_once_and_collected_by_the_existing_processor()
    {
        var (customerId, userId, _) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId, ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.RegisterPayment);
        var positionId = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var import = new ImportPreexistingReceivablesRequest(fixture.BusinessId,
            [new(positionId, customerId, null, null, $"HIST-{positionId:N}", at,
                at.AddDays(30), 10_000m, await AccountIdByCodeAsync("413595"), null)]);
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import", import))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using (var clear = new SqlCommand("""
                UPDATE dbo.Receivables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL
                WHERE ReceivableId=@Id;
                """, connection))
            {
                clear.Parameters.AddWithValue("@Id", positionId);
                Assert.Equal(1, await clear.ExecuteNonQueryAsync());
            }
            await fixture.ApplyMigrationAsync("20261027_LinkPostedPortfolioOpenings.sql", connection);
            await fixture.ApplyMigrationAsync("20261027_LinkPostedPortfolioOpenings.sql", connection);
        }
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Receivables balance
            JOIN dbo.AccountingEntryLines line ON line.EntryId=balance.OpeningEntryId
              AND line.LineNumber=balance.OpeningEntryLineNumber
            WHERE balance.ReceivableId=@Id AND line.Debit=balance.OriginalAmount;
            """, positionId));

        var payment = new ConfirmCustomerPaymentRequest(Guid.NewGuid(), fixture.BusinessId,
            customerId, null, at.AddDays(1), "COP", "Abono a cartera histórica",
            [new(positionId, 2_000m)], [new("Cash", 2_000m, 2_000m)]);
        using (var response = await SendAsync(client, "/api/commerce/v1/receivable-payments/confirm",
                   payment, payment.PaymentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(8_000m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", positionId));
        Assert.Equal(1, await CountAsync("ReceivableTransactions", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", payment.PaymentId));
    }
}

public sealed partial class PayablesVerticalSliceTests
{
    [Fact]
    public async Task Historical_posted_payable_is_linked_once_and_paid_by_the_existing_processor()
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.RegisterPayment);
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.WarehouseId, fixture.SupplierId, $"HIST-{Guid.NewGuid():N}", at, at,
            true, at.AddDays(30), "COP", null,
            [new(1, productId, "Compra histórica", 1m, 10_000m, 0m, "00", 0m,
                PurchasingTaxTreatments.NotApplicable)]);
        using (var response = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm",
                   receipt, receipt.DocumentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var positionId = await ScalarAsync<Guid>(
            "SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using (var clear = new SqlCommand("""
                UPDATE dbo.Payables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL
                WHERE PayableId=@Id;
                """, connection))
            {
                clear.Parameters.AddWithValue("@Id", positionId);
                Assert.Equal(1, await clear.ExecuteNonQueryAsync());
            }
            await fixture.ApplyMigrationAsync("20261027_LinkPostedPortfolioOpenings.sql", connection);
            await fixture.ApplyMigrationAsync("20261027_LinkPostedPortfolioOpenings.sql", connection);
        }
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Payables balance
            JOIN dbo.AccountingEntryLines line ON line.EntryId=balance.OpeningEntryId
              AND line.LineNumber=balance.OpeningEntryLineNumber
            WHERE balance.PayableId=@Id AND line.Credit=balance.OriginalAmount;
            """, positionId));

        var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId,
            fixture.SupplierId, at.AddDays(1), "COP", "Pago a obligación histórica",
            [new(positionId, 2_000m)], [new(SupplierPaymentMethods.Cash, 2_000m, 2_000m)]);
        using (var response = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm",
                   payment, payment.PaymentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(8_000m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", positionId));
        Assert.Equal(1, await CountAsync("PayableTransactions", "SourceDocumentId", payment.PaymentId));
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", payment.PaymentId));
    }
}
