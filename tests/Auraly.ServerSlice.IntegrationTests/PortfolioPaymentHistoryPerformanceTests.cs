using System.Diagnostics;
using Auraly.Application.Payables;
using Auraly.Application.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(PortfolioManualPaymentPerformanceCollection.Name)]
[Trait("Performance", "PortfolioPaymentHistory")]
public sealed class PortfolioPaymentHistoryPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task History_pages_filter_before_loading_details_with_ten_thousand_payments(bool supplier)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var prefix = "HIST-" + Guid.NewGuid().ToString("N")[..8] + "-";
        var roleId = supplier ? fixture.SupplierId : Guid.NewGuid();
        Guid siteId;
        var role = supplier ? "Supplier" : "Customer";
        var position = supplier ? "Payable" : "Receivable";
        var otherRoleColumns = supplier ? "" : ",PartyId";
        var otherRoleValues = supplier ? "" : ",NULL";
        // Synthetic read-volume fixtures: one immutable opening and one accepted
        // native payment/application per invoice. No live or production database.
        await using (var seed = new SqlCommand($"""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @Site uniqueidentifier=(SELECT PartySiteId FROM dbo.PartySites WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1);
            IF @Supplier=0
            BEGIN
              DECLARE @CustomerParty uniqueidentifier=NEWID(),@CustomerSite uniqueidentifier=NEWID();
              INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
                VALUES(@CustomerParty,@TenantId,N'Organization',N'Cliente de volumen de historial',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
              INSERT dbo.Customers(CustomerId,PartyId,TenantId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
                VALUES(@RoleId,@CustomerParty,@TenantId,0,1,@UserId,SYSDATETIMEOFFSET());
              INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
                SELECT @CustomerSite,@CustomerParty,N'PRINCIPAL',N'Principal',CountryId,AdministrativeDivisionId,CityId,AddressLine,1,1,@UserId,SYSDATETIMEOFFSET()
                FROM dbo.PartySites WHERE PartySiteId=@Site;
              SET @Site=@CustomerSite;
            END;
            DECLARE @Seed TABLE(Number int PRIMARY KEY,PositionId uniqueidentifier,SourceId uniqueidentifier,JobId uniqueidentifier,PaymentId uniqueidentifier);
            INSERT @Seed SELECT TOP(10000) ROW_NUMBER() OVER(ORDER BY a.object_id,b.object_id),NEWID(),NEWID(),NEWID(),NEWID()
              FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            DECLARE @At datetimeoffset(7)='2026-08-01T12:00:00+00:00',@Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'[]'));
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT SourceId,N'HistoryVolumeFixture',@TenantId,@BusinessId,N'[]',@Hash,@At,@At,0 FROM @Seed;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,
              OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
            SELECT JobId,@TenantId,@BusinessId,SourceId,N'HistoryVolumeFixture',@Hash,@At,0,N'Posted',1,@At,@At FROM @Seed;
            INSERT dbo.{position}s({position}Id,BusinessId,{role}Id,PartySiteId,SourceDocumentId,SourceDocumentType,
              DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            SELECT PositionId,@BusinessId,@RoleId,@Site,SourceId,N'HistoryVolumeFixture',CONCAT(@Prefix,Number),N'COP',100,100,
              DATEADD(day,30,@At),N'Open',@At FROM @Seed;
            INSERT dbo.{role}Payments(PaymentId,BusinessId,{role}Id{otherRoleColumns},DocumentSeriesId,DocumentNumber,DocumentPrefix,DocumentSeriesCode,
              DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,TotalAmount,Status,ConfirmedByUserId,AcceptedAt)
            SELECT PaymentId,@BusinessId,@RoleId{otherRoleValues},@SeriesId,CONCAT(@Prefix,Number),N'HIST',N'HIST',Number,
              CONCAT(@Prefix,Number),@Hash,DATEADD(day,CASE WHEN Number>9900 THEN 1 ELSE 0 END,@At),N'COP',1,N'Accepted',@UserId,@At FROM @Seed;
            INSERT dbo.{role}PaymentTenders(PaymentId,LineNumber,MethodCode,Amount) SELECT PaymentId,1,N'Cash',1 FROM @Seed;
            INSERT dbo.{role}PaymentApplications(PaymentId,LineNumber,{position}Id,Amount) SELECT PaymentId,1,PositionId,1 FROM @Seed;
            COMMIT; SELECT @Site;
            """, connection) { CommandTimeout = 120 })
        {
            seed.Parameters.AddWithValue("@Supplier", supplier); seed.Parameters.AddWithValue("@RoleId", roleId);
            seed.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId); seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId); seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            seed.Parameters.AddWithValue("@SeriesId", fixture.OnlineDocumentSeriesId); seed.Parameters.AddWithValue("@Prefix", prefix);
            siteId = (Guid)(await seed.ExecuteScalarAsync())!;
        }
        using var scope = fixture.CreateScope();
        var payableStore = scope.ServiceProvider.GetRequiredService<IPayablesStore>();
        var receivableStore = scope.ServiceProvider.GetRequiredService<IReceivablesStore>();
        var permissions = new HashSet<string> { PayablesPermissionCodes.Read, ReceivablesPermissionCodes.Read };
        var payableUser = new PayablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, permissions);
        var receivableUser = new ReceivablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId, permissions);
        foreach (var filtered in new[] { false, true })
        foreach (var pageNumber in new[] { 1, 2 })
        {
            var samples = new List<double>();
            Guid[]? firstPage = null;
            for (var sample = 0; sample < 20; sample++)
            {
                var from = filtered ? new DateOnly(2026, 8, 2) : (DateOnly?)null;
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var timer = Stopwatch.StartNew();
                Guid[] ids;
                if (supplier)
                {
                    var result = await payableStore.ListPaymentsAsync(payableUser,
                        new(pageNumber, 25, prefix, roleId, From: from, PartySiteId: siteId), default);
                    Assert.Equal(filtered ? 100 : 10000, result.TotalCount);
                    Assert.Equal(25, result.Items.Count);
                    Assert.All(result.Items, item => { Assert.Single(item.Payments); Assert.Single(item.Applications); Assert.Equal(1m, item.TotalAmount); });
                    ids = result.Items.Select(item => item.PaymentId).ToArray();
                }
                else
                {
                    var result = await receivableStore.ListPaymentsAsync(receivableUser,
                        new(pageNumber, 25, prefix, roleId, From: from, PartySiteId: siteId), default);
                    Assert.Equal(filtered ? 100 : 10000, result.TotalCount);
                    Assert.Equal(filtered ? 100m : 10000m, result.TotalAmount);
                    Assert.Equal(25, result.Items.Count);
                    Assert.All(result.Items, item => { Assert.Single(item.Payments); Assert.Single(item.Applications); Assert.Equal(1m, item.TotalAmount); });
                    ids = result.Items.Select(item => item.PaymentId).ToArray();
                }
                timer.Stop();
                Assert.Equal(1, counter.Count);
                firstPage ??= ids;
                Assert.Equal(firstPage, ids); // Stable tie breaker despite identical dates.
                samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine("{0} history: page={1}, dateFilter={2}, first={3:F1}ms, p95={4:F1}ms; SQL=1; 10000 payments; 20 samples",
                position, pageNumber, filtered, samples[0], p95);
            Assert.True(p95 < 800, $"History exceeded its budget: {position}, page {pageNumber}, p95 {p95:F1}ms.");
        }

        // Reuse this isolated read-volume dataset for applied totals. Functional
        // manual/native tests exercise acceptance and its actual advance/ledger
        // effects; these synthetic facts exercise paging and historical readers.
        await using (var applied = new SqlCommand($"""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @At datetimeoffset(7)='2026-08-01T12:00:00+00:00',@Later datetimeoffset(7)='2026-08-03T12:00:00+00:00',
              @Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'[]'));
            DECLARE @Rows TABLE(PositionId uniqueidentifier PRIMARY KEY,PaymentId uniqueidentifier,PaidAt datetimeoffset(7),
              OriginalTransactionId uniqueidentifier,UnapplicationId uniqueidentifier,JobId uniqueidentifier,LineId uniqueidentifier);
            INSERT @Rows SELECT application.{position}Id,payment.PaymentId,payment.PaidAt,NEWID(),NEWID(),NEWID(),NEWID()
              FROM dbo.{role}Payments payment JOIN dbo.{role}PaymentApplications application ON application.PaymentId=payment.PaymentId
              WHERE payment.BusinessId=@BusinessId AND payment.IdempotencyKey LIKE @Prefix+N'%';
            IF @@ROWCOUNT<>10000 THROW 51500,'The applied read fixture is incomplete.',1;
            UPDATE payment SET Status=N'Processed',ProcessedAt=payment.PaidAt FROM dbo.{role}Payments payment JOIN @Rows input ON input.PaymentId=payment.PaymentId;
            UPDATE application SET AppliedAt=input.PaidAt FROM dbo.{role}PaymentApplications application JOIN @Rows input ON input.PaymentId=application.PaymentId;
            UPDATE balance SET OutstandingAmount=99.25,Status=N'PartiallyPaid' FROM dbo.{position}s balance JOIN @Rows input ON input.PositionId=balance.{position}Id;
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT UnapplicationId,N'ManualAccountingVoucher',@TenantId,@BusinessId,N'[]',@Hash,@Later,@Later,0 FROM @Rows;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,
              OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
            SELECT JobId,@TenantId,@BusinessId,UnapplicationId,N'ManualAccountingVoucher',@Hash,@Later,0,N'CommercialEffectsApplied',1,@Later,@Later FROM @Rows;
            INSERT dbo.{position}Transactions({position}TransactionId,{position}Id,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT NEWID(),input.PositionId,N'Opening',100,balance.SourceDocumentId,@At,@At FROM @Rows input JOIN dbo.{position}s balance ON balance.{position}Id=input.PositionId;
            INSERT dbo.{position}Transactions({position}TransactionId,{position}Id,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT OriginalTransactionId,PositionId,N'Payment',1,PaymentId,PaidAt,PaidAt FROM @Rows;
            INSERT dbo.{position}Transactions({position}TransactionId,{position}Id,TransactionType,Amount,SourceDocumentId,SourceDocumentType,
              SourceLineId,ReversesTransactionId,OccurredAt,CreatedAt)
            SELECT NEWID(),PositionId,N'Unapplication',0.25,UnapplicationId,N'ManualAccountingVoucher',LineId,OriginalTransactionId,@Later,@Later FROM @Rows;
            COMMIT;
            """, connection) { CommandTimeout = 120 })
        {
            applied.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            applied.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            applied.Parameters.AddWithValue("@Prefix", prefix);
            await applied.ExecuteNonQueryAsync();
        }
        foreach (var scenario in new[] { "invoices-1", "invoices-2", "report-before", "report-after-1", "report-after-2" })
        {
            var samples = new List<double>();
            for (var sample = 0; sample <= 20; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var timer = Stopwatch.StartNew();
                var pageNumber = scenario.EndsWith('2') ? 2 : 1;
                if (scenario.StartsWith("invoices"))
                {
                    if (supplier)
                    {
                        var result = await payableStore.ListAsync(payableUser,
                            new(pageNumber, 25, prefix, roleId, null, null, PartySiteId: siteId), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(992_500m, result.TotalOutstanding);
                        Assert.All(result.Items, item => Assert.Equal(0.75m, item.PaidAmount));
                        Assert.Equal(25, result.Items.Count);
                    }
                    else
                    {
                        var result = await receivableStore.ListAsync(receivableUser,
                            new(pageNumber, 25, prefix, roleId, null, null, PartySiteId: siteId), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(7_500m, result.TotalPaid);
                        Assert.Equal(992_500m, result.TotalOutstanding);
                        Assert.All(result.Items, item => Assert.Equal(0.75m, item.PaidAmount));
                        Assert.Equal(25, result.Items.Count);
                    }
                    Assert.Equal(2, counter.Count);
                }
                else
                {
                    var cutoff = scenario == "report-before" ? new DateOnly(2026, 8, 2) : new(2026, 8, 4);
                    var expected = scenario == "report-before" ? 1m : 0.75m;
                    if (supplier)
                    {
                        var result = await payableStore.ReportAsync(payableUser,
                            new(pageNumber, 25, false, cutoff, roleId, PartySiteId: siteId, Search: prefix), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(expected * 10000, Assert.Single(result.CurrencyTotals).PaidAmount);
                        Assert.All(result.Items, item => { Assert.Equal(expected, item.PaidAmount); Assert.Equal(expected, item.Applications!.Sum(row => row.Amount)); });
                        Assert.Equal(25, result.Items.Count);
                    }
                    else
                    {
                        var result = await receivableStore.ReportAsync(receivableUser,
                            new(pageNumber, 25, false, cutoff, roleId, PartySiteId: siteId, Search: prefix), default);
                        Assert.Equal(10000, result.TotalCount);
                        Assert.Equal(expected * 10000, result.TotalPaid);
                        Assert.All(result.Items, item => { Assert.Equal(expected, item.PaidAmount); Assert.Equal(expected, item.Applications!.Sum(row => row.Amount)); });
                        Assert.Equal(25, result.Items.Count);
                    }
                    // The existing report first resolves the business time zone,
                    // then reads its scoped aggregates/page/details in one command.
                    Assert.Equal(2, counter.Count);
                }
                timer.Stop();
                if (sample > 0) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine("{0} applied totals: {1}, p95={2:F1}ms; SQL=2; 10000 payments/unapplications; 20 samples", position, scenario, p95);
            Assert.True(p95 < 2000, $"Applied totals exceeded their budget: {position}, {scenario}, p95 {p95:F1}ms.");
        }
    }
}
