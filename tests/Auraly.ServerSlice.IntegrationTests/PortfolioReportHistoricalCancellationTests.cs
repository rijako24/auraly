using System.Net.Http.Json;
using Auraly.Contracts.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class PortfolioReportHistoricalCancellationTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Later_cancellation_does_not_reclassify_an_earlier_settled_cutoff(bool manual)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var positionId = Guid.NewGuid();
        var document = $"CUT-{positionId:N}";
        await using var seed = new SqlCommand("""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @Source uniqueidentifier=NEWID(),@Cancellation uniqueidentifier=NEWID(),
              @At datetimeoffset(7)='2026-08-01T12:00:00+00:00',
              @CancelledAt datetimeoffset(7)='2026-09-02T12:00:00+00:00',
              @Type nvarchar(64)=CASE WHEN @Manual=1 THEN N'ManualAccountingVoucher' ELSE N'ExpenseCancellation' END;
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
              PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            VALUES(@Source,N'HistoryVolumeFixture',@Tenant,@Business,N'[]',HASHBYTES('SHA2_256',N'[]'),@At,@At,0),
              (@Cancellation,@Type,@Tenant,@Business,N'{"VoucherTypeCode":"SupportDocumentCancellation"}',
                HASHBYTES('SHA2_256',N'{"VoucherTypeCode":"SupportDocumentCancellation"}'),@CancelledAt,@CancelledAt,0);
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
              SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
            SELECT NEWID(),TenantId,BusinessId,SourceDocumentId,SourceDocumentType,PayloadHash,OccurredAt,0,
              N'CommercialEffectsApplied',1,AcceptedAt,AcceptedAt FROM dbo.AccountingSourceDocuments
            WHERE TenantId=@Tenant AND BusinessId=@Business AND SourceDocumentId IN(@Source,@Cancellation);
            INSERT dbo.Payables(PayableId,BusinessId,SupplierId,SourceDocumentId,SourceDocumentType,
              DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            VALUES(@Position,@Business,@Supplier,@Source,N'HistoryVolumeFixture',@Number,N'COP',100,0,@At,N'Cancelled',@At);
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,
              SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
            VALUES(NEWID(),@Position,N'Opening',100,@Source,NULL,@At,@At),
              (NEWID(),@Position,N'Adjustment',-100,NEWID(),NULL,DATEADD(day,1,@At),DATEADD(day,1,@At)),
              (NEWID(),@Position,N'Adjustment',100,NEWID(),NULL,DATEADD(month,1,@At),DATEADD(month,1,@At)),
              (NEWID(),@Position,N'Credit',100,@Cancellation,CASE WHEN @Manual=1 THEN @Type END,@CancelledAt,@CancelledAt);
            COMMIT;
            """, connection);
        seed.Parameters.AddWithValue("@Manual", manual);
        seed.Parameters.AddWithValue("@Position", positionId);
        seed.Parameters.AddWithValue("@Number", document);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
        seed.Parameters.AddWithValue("@Supplier", fixture.SupplierId);
        await seed.ExecuteNonQueryAsync();
        using var client = fixture.CreateAdminClient(PayablesPermissionCodes.Read);
        var url = $"/api/commerce/v1/payables/report?page=1&pageSize=20&consolidated=false&includeApplications=false&search={document}";
        var before = (await client.GetFromJsonAsync<PayablesReportPage>($"{url}&cutoff=2026-08-15&status=Paid"))!;
        Assert.Equal(positionId, Assert.Single(before.Items).PayableId);
        Assert.Equal(0m, Assert.Single(before.Items).OutstandingAmount);
        var after = (await client.GetFromJsonAsync<PayablesReportPage>($"{url}&cutoff=2026-09-03&status=Cancelled"))!;
        Assert.Equal(positionId, Assert.Single(after.Items).PayableId);
    }
}
