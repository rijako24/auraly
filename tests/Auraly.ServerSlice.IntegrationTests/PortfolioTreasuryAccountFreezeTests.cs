using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_cash_payment_keeps_its_account_after_mapping_change_and_replay(bool missingAccount)
    {
        var product = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(product);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.RegisterPayment,
            AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read, AccountingPermissionCodes.Retry);
        var date = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"CASH-FREEZE-{Guid.NewGuid():N}", date, date, true, date.AddDays(30), "COP", null,
            [new(1, product, "Compra para verificar caja aceptada", 1m, 10_000m, 0m, "00", 0m,
                PurchasingTaxTreatments.NotApplicable)]);
        using (var response = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", receipt, receipt.DocumentId.ToString("N")))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var positionId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);
        await PortfolioTreasuryAccountFreezeScenario.AssertAsync(fixture, client, false, positionId, date,
            id => SendAsync(client, "/api/commerce/v1/payable-payments/confirm",
                new ConfirmSupplierPaymentRequest(id, fixture.BusinessId, fixture.SupplierId, date.AddDays(1), "COP",
                    "Pago aceptado antes de cambiar caja", [new(positionId, 100m)], [new("Cash", 100m, 100m)]), id.ToString("N")), missingAccount);
    }
}

public sealed partial class ReceivablesVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_cash_collection_keeps_its_account_after_mapping_change_and_replay(bool missingAccount)
    {
        var (customerId, userId, _) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId, ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.RegisterPayment, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Retry);
        var date = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var positionId = Guid.NewGuid();
        using (var imported = await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import",
            new ImportPreexistingReceivablesRequest(fixture.BusinessId,
                [new(positionId, customerId, null, null, $"CASH-FREEZE-{positionId:N}", date, date.AddDays(30),
                    10_000m, await AccountIdByCodeAsync("413595"), null)])))
            Assert.True(imported.IsSuccessStatusCode, await imported.Content.ReadAsStringAsync());
        await PortfolioTreasuryAccountFreezeScenario.AssertAsync(fixture, client, true, positionId, date,
            id => SendAsync(client, "/api/commerce/v1/receivable-payments/confirm",
                new ConfirmCustomerPaymentRequest(id, fixture.BusinessId, customerId, null, date.AddDays(1), "COP",
                    "Recaudo aceptado antes de cambiar caja", [new(positionId, 100m)], [new("Cash", 100m, 100m)]), id.ToString("N")), missingAccount);
    }
}

internal static class PortfolioTreasuryAccountFreezeScenario
{
    internal static async Task AssertAsync(ServerSliceFixture fixture, HttpClient client, bool receivable,
        Guid positionId, DateTimeOffset date, Func<Guid, Task<HttpResponseMessage>> accept, bool missingAccount)
    {
        var kind = receivable ? "Receivable" : "Payable";
        var table = receivable ? "Receivables" : "Payables";
        var paymentId = Guid.NewGuid();
        var positions = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind={kind}&subledgerId={positionId}&page=1&pageSize=10");
        var position = Assert.Single(positions!.Items);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var mapping = new SqlCommand("""
            SELECT TOP(1) AccountId FROM dbo.AccountingAccountMappings WHERE TenantId=@Tenant AND Category=N'Cash'
              AND (BusinessId=@Business OR BusinessId IS NULL) AND EffectiveFrom<=@Date
              AND (EffectiveTo IS NULL OR EffectiveTo>=@Date)
            ORDER BY CASE WHEN BusinessId=@Business THEN 0 ELSE 1 END,EffectiveFrom DESC;
            """, connection);
        mapping.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        mapping.Parameters.AddWithValue("@Business", fixture.BusinessId);
        mapping.Parameters.AddWithValue("@Date", date.Date);
        var originalCash = (Guid)(await mapping.ExecuteScalarAsync())!;
        var alternate = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        await using var active = new SqlCommand("UPDATE dbo.AccountingAccounts SET IsActive=@Active WHERE AccountId=@Account", connection);
        active.Parameters.AddWithValue("@Active", !missingAccount);
        active.Parameters.AddWithValue("@Account", originalCash);
        await using var binding = new SqlCommand($"UPDATE dbo.{table} SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE {kind}Id=@Id", connection);
        binding.Parameters.AddWithValue("@Id", positionId);
        Assert.Equal(1, await binding.ExecuteNonQueryAsync());
        try
        {
            if (missingAccount) Assert.Equal(1, await active.ExecuteNonQueryAsync());
            using var accepted = await accept(paymentId);
            Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
            await using var pending = new SqlCommand($"""
                SELECT job.Status,position.OutstandingAmount,tender.AccountId,tender.SourceLineId
                FROM dbo.AccountingPostingJobs job CROSS JOIN dbo.{table} position
                JOIN dbo.{(receivable ? "Customer" : "Supplier")}PaymentTenders tender ON tender.PaymentId=@Payment
                WHERE job.SourceDocumentId=@Payment AND position.{kind}Id=@Position;
                """, connection);
            pending.Parameters.AddWithValue("@Payment", paymentId);
            pending.Parameters.AddWithValue("@Position", positionId);
            await using var row = await pending.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            Assert.Equal("AccountingPendingConfiguration", row.GetString(0));
            Assert.Equal(10_000m, row.GetDecimal(1));
            if (missingAccount) Assert.True(row.IsDBNull(2));
            else Assert.Equal(originalCash, row.GetGuid(2));
            Assert.NotEqual(Guid.Empty, row.GetGuid(3));
        }
        finally
        {
            if (missingAccount)
            {
                active.Parameters["@Active"].Value = true;
                Assert.Equal(1, await active.ExecuteNonQueryAsync());
            }
            binding.CommandText = $"UPDATE dbo.{table} SET OpeningEntryId=@Entry,OpeningEntryLineNumber=@Line WHERE {kind}Id=@Id";
            binding.Parameters.AddWithValue("@Entry", position.OpeningEntryId);
            binding.Parameters.AddWithValue("@Line", position.OpeningEntryLineNumber);
            Assert.Equal(1, await binding.ExecuteNonQueryAsync());
        }
        // Change only this test's existing mapping rows, then restore them even on failure.
        await using var remap = new SqlCommand("""
            UPDATE dbo.AccountingAccountMappings SET AccountId=@New WHERE TenantId=@Tenant AND Category=N'Cash' AND AccountId=@Old;
            """, connection);
        remap.Parameters.AddWithValue("@New", alternate.AccountId);
        remap.Parameters.AddWithValue("@Old", originalCash);
        remap.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        Assert.True(await remap.ExecuteNonQueryAsync() > 0);
        try
        {
            var expected = missingAccount ? alternate.AccountId : originalCash;
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{paymentId}/retry", null);
                Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
            }
            await using var actual = new SqlCommand("""
                SELECT line.AccountId FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE entry.SourceDocumentId=@Id AND line.AccountId=@Account;
                """, connection);
            actual.Parameters.AddWithValue("@Id", paymentId);
            actual.Parameters.AddWithValue("@Account", expected);
            Assert.Equal(expected, (Guid)(await actual.ExecuteScalarAsync())!);
            using var replay = await accept(paymentId);
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            await using var final = new SqlCommand($"""
                SELECT OutstandingAmount,(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Payment)
                FROM dbo.{table} WHERE {kind}Id=@Position;
                """, connection);
            final.Parameters.AddWithValue("@Payment", paymentId);
            final.Parameters.AddWithValue("@Position", positionId);
            await using (var row = await final.ExecuteReaderAsync())
            {
                Assert.True(await row.ReadAsync());
                Assert.Equal(9_900m, row.GetDecimal(0));
                Assert.Equal(1, row.GetInt32(1));
            }
            var second = Guid.NewGuid();
            using var fresh = await accept(second);
            Assert.True(fresh.IsSuccessStatusCode, await fresh.Content.ReadAsStringAsync());
            actual.Parameters["@Id"].Value = second;
            actual.Parameters["@Account"].Value = alternate.AccountId;
            Assert.Equal(alternate.AccountId, (Guid)(await actual.ExecuteScalarAsync())!);
        }
        finally
        {
            remap.CommandText = "UPDATE dbo.AccountingAccountMappings SET AccountId=@Old WHERE TenantId=@Tenant AND Category=N'Cash' AND AccountId=@New";
            await remap.ExecuteNonQueryAsync();
        }
    }
}
