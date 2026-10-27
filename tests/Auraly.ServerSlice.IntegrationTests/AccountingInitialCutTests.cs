using System.Net;
using System.Net.Http.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountingInitialCutCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly accounting initial cut";
}

[Collection(AccountingInitialCutCollection.Name)]
public sealed class AccountingInitialCutTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Zero_opening_cannot_hide_an_existing_commercial_purchase()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Activate, PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, AccountingPermissionCodes.ManualCreate,
            AccountingPermissionCodes.ManualSend);
        var date = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var accounts = await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts")
            ?? throw new InvalidOperationException("The initial-cut fixture has no account response.");
        var journal = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId, date,
            "MANUAL_VOUCHER", "Saldo del libro antes del corte", [
                new(accounts.Single(account => account.Code == "519595").AccountId, null, null, "Gasto", 100m, 0m),
                new(accounts.Single(account => account.Code == "111005").AccountId, null, null, "Banco", 0m, 100m)]);
        using (var posted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", journal))
            Assert.True(posted.StatusCode == HttpStatusCode.Accepted, await posted.Content.ReadAsStringAsync());
        using (var replay = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new DateOnly(2026, 1, 1), "COP", "ZeroDeclared")))
        {
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.Equal(AccountingActivationStatuses.Ready, (await replay.Content.ReadFromJsonAsync<AccountingReadinessView>())!.Status);
        }
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            UPDATE dbo.AccountingTenantSettings SET Status=N'Disabled',EffectiveFrom=NULL,OpeningBalanceMode=NULL,
              ActivatedAt=NULL,ActivatedByUserId=NULL WHERE TenantId=@TenantId;
            """, connection);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        using (var existingBook = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new DateOnly(2026, 8, 3), "COP", "ZeroDeclared")))
        {
            Assert.Equal(HttpStatusCode.Conflict, existingBook.StatusCode);
            Assert.Contains("saldos contables", await existingBook.Content.ReadAsStringAsync());
        }
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, "CORTE-COMERCIAL", date, date, true, date.AddDays(30), "COP", null,
            [new(1, fixture.ProductId, "Compra antes de activar el libro", 1m, 10_000m, 0m, "00", 0m,
                PurchasingTaxTreatments.NotApplicable)]);
        fixture.PauseDocumentProcessing();
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm")
                { Content = JsonContent.Create(receipt) };
            message.Headers.Add("Idempotency-Key", receipt.DocumentId.ToString("N"));
            using var received = await client.SendAsync(message);
            Assert.True(received.StatusCode == HttpStatusCode.Accepted, await received.Content.ReadAsStringAsync());
            using var pendingCut = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
                new ActivateAccountingRequest(new DateOnly(2026, 8, 3), "COP", "ZeroDeclared"));
            Assert.Equal(HttpStatusCode.Conflict, pendingCut.StatusCode);
            Assert.Contains("pendientes de procesar", await pendingCut.Content.ReadAsStringAsync());
        }
        finally { fixture.ResumeDocumentProcessing(); }
        var signal = Assert.Single(fixture.DrainDocumentSignals());
        using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>().ProcessOneAsync(signal, CancellationToken.None);
        using var rejected = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new DateOnly(2026, 8, 3), "COP", "ZeroDeclared"));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("apertura en cero", await rejected.Content.ReadAsStringAsync());
        var readiness = await client.GetFromJsonAsync<AccountingReadinessView>(
            "/api/commerce/v1/accounting/readiness?effectiveFrom=2026-08-03&openingBalanceMode=ZeroDeclared");
        Assert.Equal(AccountingActivationStatuses.Disabled, readiness!.Status);
        Assert.Contains(readiness.BlockingIssues, issue => issue.Contains("apertura en cero", StringComparison.Ordinal));
        command.CommandText = """
            SELECT job.AccountingEntryRequired,job.Status,balance.OutstandingAmount,
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@DocumentId)
            FROM dbo.AccountingPostingJobs job JOIN dbo.Payables balance ON balance.SourceDocumentId=job.SourceDocumentId
            WHERE job.SourceDocumentId=@DocumentId AND job.TenantId=@TenantId;
            """;
        command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.False(reader.GetBoolean(0));
        Assert.Equal(AccountingPostingStatuses.CommercialEffectsApplied, reader.GetString(1));
        Assert.Equal(10_000m, reader.GetDecimal(2));
        Assert.Equal(0, reader.GetInt32(3));
    }
}
