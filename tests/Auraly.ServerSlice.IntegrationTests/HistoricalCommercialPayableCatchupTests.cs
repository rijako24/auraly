using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commercial_only_payable_reconciliation_proves_old_payments_before_applying_pending_money(bool corruptOldJournal)
    {
        var productId = Guid.NewGuid();
        await ConfigureSeriesAndAccountingAsync(productId);
        using var client = fixture.CreateAdminClient(PurchasingPermissionCodes.CreateGoodsReceipts,
            PurchasingPermissionCodes.ConfirmGoodsReceipts, PayablesPermissionCodes.Read,
            PayablesPermissionCodes.RegisterPayment, AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Retry, CatalogPermissionCodes.Read, CatalogPermissionCodes.Update);
        var at = new DateTimeOffset(2026, 9, corruptOldJournal ? 10 : 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var receipt = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
            fixture.SupplierId, $"HIST-CXP-{Guid.NewGuid():N}", at, at, true, at.AddDays(30), "COP", null,
            [new(1, productId, "Obligación histórica", 2_000, 1_000, 0, "00", 0, PurchasingTaxTreatments.NotApplicable)]);
        using (var response = await SendAsync(client, "/api/commerce/v1/goods-receipts/confirm", receipt, receipt.DocumentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var receiptState = await ScalarAsync<string>("""
            SELECT (SELECT Status,LastError FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id FOR JSON PATH) Operations,
              (SELECT Status,LastErrorCode,LastErrorMessage FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id FOR JSON PATH) Accounting
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER
            """, receipt.DocumentId);
        Assert.True(await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId) == 1, receiptState);
        Assert.Equal("Posted", await ScalarAsync<string>("""
            SELECT CASE WHEN Status=N'Posted' THEN Status ELSE CONCAT(Status,N': ',LastErrorCode,N': ',LastErrorMessage) END
            FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id
            """, receipt.DocumentId));
        var positionId = await ScalarAsync<Guid>("SELECT PayableId FROM dbo.Payables WHERE SourceDocumentId=@Id", receipt.DocumentId);
        var partyId = await ScalarAsync<Guid>("SELECT PartyId FROM dbo.Suppliers WHERE SupplierId=@Id", fixture.SupplierId);
        var accountId = await ScalarAsync<Guid>("""
            SELECT line.AccountId FROM dbo.Payables position JOIN dbo.AccountingEntryLines line
              ON line.EntryId=position.OpeningEntryId AND line.LineNumber=position.OpeningEntryLineNumber
            WHERE position.PayableId=@Id
            """, positionId);
        var counterpartId = await ScalarAsync<Guid>("""
            SELECT TOP(1) line.AccountId FROM dbo.Payables position JOIN dbo.AccountingEntryLines line
              ON line.EntryId=position.OpeningEntryId WHERE position.PayableId=@Id AND line.Debit>0
            ORDER BY line.LineNumber
            """, positionId);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var types = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!;
        var formRequest = new SavePaymentMethodRequest(Guid.NewGuid(), $"HIST-{Guid.NewGuid():N}", "Caja histórica",
            types.Single(type => type.Code == "Cash").Id, cash.AccountId, false, false, true, false, true, 10);
        using var saved = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{formRequest.PaymentMethodId}", formRequest);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var form = (await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method;
        ConfirmSupplierPaymentRequest Payment(decimal amount) => new(Guid.NewGuid(), fixture.BusinessId,
            fixture.SupplierId, at.AddDays(3), "COP", null, [new(positionId, amount)],
            [new("Cash", amount, amount, PaymentMethodId: form.PaymentMethodId, PaymentMethodVersion: form.Version)]);
        var oldPayment = Payment(500_000);
        using (var response = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", oldPayment, oldPayment.PaymentId.ToString("N")))
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("Posted", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", oldPayment.PaymentId));
        var oldJournal = await ScalarAsync<string>("""
            SELECT (SELECT line.* FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
              WHERE entry.SourceDocumentId=@Id ORDER BY line.LineNumber FOR JSON PATH)
            """, oldPayment.PaymentId);

        // Isolated fixture: reproduce a commercial-only import without its original
        // journal, preserving the real, already posted payment and stock movement.
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var setup = new SqlCommand("""
            DECLARE @Entry uniqueidentifier=(SELECT OpeningEntryId FROM dbo.Payables WHERE PayableId=@Id);
            UPDATE dbo.Payables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE PayableId=@Id;
            DELETE FROM dbo.AccountingEntryLines WHERE EntryId=@Entry;
            DELETE FROM dbo.AccountingEntries WHERE EntryId=@Entry;
            UPDATE dbo.AccountingSourceDocuments SET AccountingEntryRequired=0 WHERE SourceDocumentId=@Source;
            UPDATE dbo.AccountingPostingJobs SET AccountingEntryRequired=0,Status=N'CommercialEffectsApplied' WHERE SourceDocumentId=@Source;
            """, connection))
        {
            setup.Parameters.AddWithValue("@Id", positionId);
            setup.Parameters.AddWithValue("@Source", receipt.DocumentId);
            await setup.ExecuteNonQueryAsync();
        }
        var pending = new[] { Payment(100_000), Payment(50_000) };
        foreach (var payment in pending)
        {
            using var response = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment, payment.PaymentId.ToString("N"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("OriginalPositionAccountMissing", await ScalarAsync<string>(
                "SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", payment.PaymentId));
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", payment.PaymentId));
        }
        Assert.Equal(1_500_000, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", positionId));
        if (corruptOldJournal)
        {
            await using var corrupt = new SqlCommand("""
                UPDATE line SET Debit=CASE WHEN Debit>0 THEN Debit+1 ELSE Debit END,
                  Credit=CASE WHEN Credit>0 THEN Credit+1 ELSE Credit END
                FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                WHERE entry.SourceDocumentId=@Id
                """, connection);
            corrupt.Parameters.AddWithValue("@Id", oldPayment.PaymentId);
            Assert.Equal(2, await corrupt.ExecuteNonQueryAsync());
        }
        var batchId = Guid.NewGuid();
        var payload = new AccountingReconciledOpeningPayload(batchId, fixture.BusinessId, at.AddDays(1),
            "OPENING_BALANCE", "Conciliación explícita de obligación histórica",
            [new(1, accountId, partyId, null, "Obligación original", 0, 2_000_000, Guid.NewGuid(),
                new(AccountingSubledgerKinds.Payable, positionId)),
             new(2, counterpartId, null, null, "Contrapartida aprobada", 2_000_000, 0, Guid.NewGuid(), null)],
            HistoricalCatchup: true);
        using var scope = fixture.CreateScope();
        await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            await using var batch = new SqlCommand("""
                INSERT dbo.AccountingOpeningBalanceBatches
                  (BatchId,TenantId,BusinessId,EffectiveOn,CurrencyCode,Description,Status,
                   CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt,ApprovedByUserId,ApprovedAt)
                VALUES(@Batch,@Tenant,@Business,@Effective,N'COP',N'Conciliación histórica explícita',N'Approved',
                  @User,SYSDATETIMEOFFSET(),@User,SYSDATETIMEOFFSET(),@User,SYSDATETIMEOFFSET());
                INSERT dbo.AccountingOpeningBalanceLines
                  (BatchId,LineNumber,AccountId,PartyId,Description,Debit,Credit,LineId,ExistingPayableId)
                SELECT @Batch,LineNumber,AccountId,PartyId,Description,Debit,Credit,LineId,PositionId
                FROM OPENJSON(@Lines) WITH(LineNumber int,AccountId uniqueidentifier,PartyId uniqueidentifier,
                  Description nvarchar(300),Debit decimal(19,4),Credit decimal(19,4),LineId uniqueidentifier,
                  PositionId uniqueidentifier '$.ExistingPosition.SubledgerId');
                """, connection, transaction);
            batch.Parameters.AddWithValue("@Batch", batchId);
            batch.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            batch.Parameters.AddWithValue("@Business", fixture.BusinessId);
            batch.Parameters.AddWithValue("@Effective", payload.OccurredAt.Date);
            batch.Parameters.AddWithValue("@User", fixture.UserId);
            batch.Parameters.AddWithValue("@Lines", JsonSerializer.Serialize(payload.Lines));
            await batch.ExecuteNonQueryAsync();
            await SqlAccountingPostingJobWriter.InsertSourceAsync(connection, transaction, fixture.TenantId,
                fixture.BusinessId, batchId, AccountingManualDocumentTypes.OpeningBalance, JsonSerializer.Serialize(payload),
                payload.OccurredAt, scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(), TimeProvider.System,
                CancellationToken.None);
            await transaction.CommitAsync();
        }
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        using (var reads = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("accounting.OpeningPositionsValidate", StringComparison.Ordinal)))
        {
            await processor.ProcessAsync(batchId, AccountingManualDocumentTypes.OpeningBalance, fixture.BusinessId, CancellationToken.None);
            Assert.Equal(1, reads.Count);
        }
        if (corruptOldJournal)
        {
            Assert.Equal("OpeningReconciliationRequired", await ScalarAsync<string>(
                "SELECT LastErrorCode FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", batchId));
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", batchId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE PayableId=@Id AND OpeningEntryId IS NOT NULL", positionId));
            Assert.Equal(1_500_000, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", positionId));
            return;
        }
        Assert.Equal("Posted", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", batchId));
        await processor.ProcessAsync(batchId, AccountingManualDocumentTypes.OpeningBalance, fixture.BusinessId, CancellationToken.None);
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", batchId));
        Assert.Equal(1_500_000, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", positionId));
        foreach (var payment in pending)
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{payment.PaymentId}/retry", null);
                Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
            }
            Assert.Equal("Posted", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", payment.PaymentId));
            Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", payment.PaymentId));
            Assert.Equal(1, await CountAsync("PayableTransactions", "SourceDocumentId", payment.PaymentId));
        }
        Assert.Equal(1_350_000, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", positionId));
        Assert.Equal(oldJournal, await ScalarAsync<string>("""
            SELECT (SELECT line.* FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
              WHERE entry.SourceDocumentId=@Id ORDER BY line.LineNumber FOR JSON PATH)
            """, oldPayment.PaymentId));
        Assert.Equal(0m, await ScalarAsync<decimal>("""
            SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Id
            """, batchId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id", receipt.DocumentId));
    }
}
