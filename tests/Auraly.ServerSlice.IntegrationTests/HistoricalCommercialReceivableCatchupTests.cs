using System.Data;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ReceivablesVerticalSliceTests
{
    [Fact]
    public async Task Commercial_only_receivable_with_posted_and_pending_collections_is_reconciled_without_reapplying_old_money()
    {
        var (customerId, userId, _) = await ConfigureAsync();
        using var client = fixture.CreateUserClient(userId, ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.RegisterPayment, AccountingPermissionCodes.Retry);
        var positionId = Guid.NewGuid();
        var sourceAt = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var openingAt = sourceAt.AddDays(1);
        var paymentAt = sourceAt.AddDays(2);
        var receivableAccountId = await AccountIdByCodeAsync("130505");
        var revenueAccountId = await AccountIdByCodeAsync("413595");
        var partyId = await ScalarAsync<Guid>(
            "SELECT PartyId FROM dbo.Customers WHERE CustomerId=@Id", customerId);
        var import = new ImportPreexistingReceivablesRequest(fixture.BusinessId,
            [new(positionId, customerId, null, null, $"HIST-CATCHUP-{positionId:N}", sourceAt,
                sourceAt.AddDays(30), 168_778.51m, revenueAccountId, null)]);
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import", import))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

        foreach (var amount in new[] { 10_000m, 58_779m, 10_000m })
        {
            var previous = new ConfirmCustomerPaymentRequest(Guid.NewGuid(), fixture.BusinessId,
                customerId, null, paymentAt, "COP", "Recaudo previo", [new(positionId, amount)],
                [new("Cash", amount, amount)]);
            using var response = await SendAsync(client, "/api/commerce/v1/receivable-payments/confirm",
                previous, previous.PaymentId.ToString("N"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }
        Assert.Equal(89_999.51m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", positionId));

        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var eraseOriginalJournal = new SqlCommand("""
                DECLARE @EntryId uniqueidentifier=(SELECT OpeningEntryId FROM dbo.Receivables WHERE ReceivableId=@Id);
                UPDATE dbo.Receivables SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE ReceivableId=@Id;
                DELETE dbo.AccountingEntryLines WHERE EntryId=@EntryId;
                DELETE dbo.AccountingEntries WHERE EntryId=@EntryId;
                UPDATE source SET AccountingEntryRequired=0 FROM dbo.AccountingSourceDocuments source
                  JOIN dbo.Receivables balance ON balance.SourceDocumentId=source.SourceDocumentId
                    AND balance.SourceDocumentType=source.SourceDocumentType WHERE balance.ReceivableId=@Id;
                UPDATE job SET AccountingEntryRequired=0,Status=N'CommercialEffectsApplied'
                FROM dbo.AccountingPostingJobs job JOIN dbo.Receivables balance
                  ON balance.SourceDocumentId=job.SourceDocumentId AND balance.SourceDocumentType=job.SourceDocumentType
                WHERE balance.ReceivableId=@Id;
                """, connection);
            eraseOriginalJournal.Parameters.AddWithValue("@Id", positionId);
            await eraseOriginalJournal.ExecuteNonQueryAsync();
        }

        var pendingIds = new List<Guid>();
        for (var index = 0; index < 2; index++)
        {
            var pending = new ConfirmCustomerPaymentRequest(Guid.NewGuid(), fixture.BusinessId,
                customerId, null, paymentAt.AddDays(index + 1), "COP", "Recaudo pendiente",
                [new(positionId, 20_000m)], [new("Cash", 20_000m, 20_000m)]);
            using var response = await SendAsync(client, "/api/commerce/v1/receivable-payments/confirm",
                pending, pending.PaymentId.ToString("N"));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            pendingIds.Add(pending.PaymentId);
        }
        Assert.Equal(89_999.51m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", positionId));

        var batchId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var payload = new AccountingReconciledOpeningPayload(batchId, fixture.BusinessId, openingAt,
            "OPENING_BALANCE", "Regularización de cartera comercial histórica",
            [new(1, receivableAccountId, partyId, null, "Cartera original", 168_778.51m, 0m,
                lineId, new(AccountingSubledgerKinds.Receivable, positionId)),
             new(2, revenueAccountId, null, null, "Ingreso de la venta original", 0m,
                168_778.51m, Guid.NewGuid(), null)], HistoricalCatchup: true);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using (var batch = new SqlCommand("""
                INSERT dbo.AccountingOpeningBalanceBatches
                (BatchId,TenantId,BusinessId,EffectiveOn,CurrencyCode,Description,Status,
                 CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt,ApprovedByUserId,ApprovedAt)
                VALUES(@BatchId,@TenantId,@BusinessId,@EffectiveOn,N'COP',N'Regularización de cartera histórica',
                  N'Approved',@UserId,SYSDATETIMEOFFSET(),@UserId,SYSDATETIMEOFFSET(),@UserId,SYSDATETIMEOFFSET());
                INSERT dbo.AccountingOpeningBalanceLines
                (BatchId,LineNumber,AccountId,PartyId,Description,Debit,Credit,LineId,ExistingReceivableId)
                VALUES(@BatchId,1,@ReceivableAccountId,@PartyId,N'Cartera original',168778.51,0,@LineId,@PositionId),
                      (@BatchId,2,@RevenueAccountId,NULL,N'Ingreso de la venta original',0,168778.51,@OtherLineId,NULL);
                """, connection, transaction))
            {
                batch.Parameters.AddWithValue("@BatchId", batchId);
                batch.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                batch.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                batch.Parameters.AddWithValue("@EffectiveOn", openingAt.Date);
                batch.Parameters.AddWithValue("@UserId", userId);
                batch.Parameters.AddWithValue("@ReceivableAccountId", receivableAccountId);
                batch.Parameters.AddWithValue("@RevenueAccountId", revenueAccountId);
                batch.Parameters.AddWithValue("@PartyId", partyId);
                batch.Parameters.AddWithValue("@LineId", lineId);
                batch.Parameters.AddWithValue("@OtherLineId", payload.Lines[1].LineId!.Value);
                batch.Parameters.AddWithValue("@PositionId", positionId);
                await batch.ExecuteNonQueryAsync();
            }
            using var scope = fixture.CreateScope();
            await SqlAccountingPostingJobWriter.InsertSourceAsync(connection, transaction,
                fixture.TenantId, fixture.BusinessId, batchId, AccountingManualDocumentTypes.OpeningBalance,
                JsonSerializer.Serialize(payload), openingAt,
                scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(), TimeProvider.System,
                CancellationToken.None);
            await transaction.CommitAsync();
        }
        using (var scope = fixture.CreateScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
            await processor.ProcessAsync(batchId, AccountingManualDocumentTypes.OpeningBalance,
                fixture.BusinessId, CancellationToken.None);
            await processor.ProcessAsync(batchId, AccountingManualDocumentTypes.OpeningBalance,
                fixture.BusinessId, CancellationToken.None);
        }
        var catchupStatus = await ScalarAsync<string>("""
            SELECT CASE WHEN Status=N'Posted' THEN N'Posted'
              ELSE CONCAT(Status,N': ',LastErrorCode,N': ',LastErrorMessage) END
            FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id;
            """, batchId);
        Assert.Equal("Posted", catchupStatus);
        Assert.Equal(1, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Receivables balance
            JOIN dbo.AccountingEntryLines line ON line.EntryId=balance.OpeningEntryId
              AND line.LineNumber=balance.OpeningEntryLineNumber
            WHERE balance.ReceivableId=@Id AND line.Debit=balance.OriginalAmount;
            """, positionId));
        Assert.Equal(89_999.51m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", positionId));
        foreach (var paymentId in pendingIds)
        {
            using var response = await client.PostAsync($"/api/commerce/v1/accounting/postings/{paymentId}/retry", null);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", paymentId));
            Assert.Equal(1, await CountAsync("ReceivableTransactions", "SourceDocumentId", paymentId));
        }
        Assert.Equal(49_999.51m, await ScalarAsync<decimal>(
            "SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", positionId));
    }
}
