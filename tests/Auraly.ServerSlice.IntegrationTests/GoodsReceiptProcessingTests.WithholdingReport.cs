using System.Diagnostics;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptProcessingTests
{
    private async Task AssertManualWithholdingReportVolumeAsync(Guid originalDocumentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var prefix = "WHV-" + Guid.NewGuid().ToString("N") + "-";
        var at = new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.FromHours(-5));
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<WithholdingService>();
        var user = new TaxationUserIdentity(TenantId: fixture.TenantId, BusinessId: fixture.BusinessId, UserId: fixture.UserId,
            Permissions: new HashSet<string> { TaxationPermissionCodes.ViewWithholdingRules });
        await using var seed = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Seeds TABLE(Id uniqueidentifier PRIMARY KEY,Number nvarchar(64));
            INSERT @Seeds SELECT TOP(10000) NEWID(),@Prefix+CONVERT(nvarchar(12),ROW_NUMBER() OVER(ORDER BY sourceA.object_id,sourceB.object_id))
              FROM sys.all_objects sourceA CROSS JOIN sys.all_objects sourceB;
            INSERT dbo.FiscalDocuments(DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
              AuralyDocumentNumber,FiscalNumber,UniqueCodeType,IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            SELECT input.Id,original.BusinessId,original.SourceDocumentType,original.FiscalDocumentType,
              input.Number,input.Number,original.UniqueCodeType,@At,N'PendingGeneration',@At,@At
            FROM @Seeds input CROSS JOIN dbo.FiscalDocuments original WHERE original.DocumentId=@Original;
            INSERT fiscal.PurchaseSupportFiscalSnapshots(DocumentId,SnapshotJson,Environment,CreatedAt)
            SELECT input.Id,JSON_MODIFY(JSON_MODIFY(JSON_MODIFY(original.SnapshotJson,'$.fiscalNumber',input.Number),
              '$.manualAcquisition.documentId',CONVERT(nvarchar(36),input.Id)),'$.manualAcquisition.issuedAt',@IssuedAt),
              original.Environment,@At
            FROM @Seeds input CROSS JOIN fiscal.PurchaseSupportFiscalSnapshots original WHERE original.DocumentId=@Original;
            INSERT dbo.DocumentWithholdingSnapshots(DocumentId,DocumentType,BusinessId,GrossAmount,WithholdingTotal,NetAmount,RecognizedAt)
            SELECT input.Id,original.DocumentType,original.BusinessId,original.GrossAmount,original.WithholdingTotal,original.NetAmount,@At
            FROM @Seeds input CROSS JOIN dbo.DocumentWithholdingSnapshots original WHERE original.DocumentId=@Original;
            INSERT dbo.DocumentWithholdingLines(DocumentId,DocumentType,LineNumber,RuleId,RuleVersion,AccountId,ManualLineId,
              RuleCode,Name,Kind,BaseKind,TaxableBase,Rate,Amount,JurisdictionCode)
            SELECT input.Id,original.DocumentType,original.LineNumber,original.RuleId,original.RuleVersion,original.AccountId,original.ManualLineId,
              original.RuleCode,original.Name,original.Kind,original.BaseKind,original.TaxableBase,original.Rate,original.Amount,original.JurisdictionCode
            FROM @Seeds input CROSS JOIN dbo.DocumentWithholdingLines original WHERE original.DocumentId=@Original;
            """, connection);
        seed.Parameters.AddWithValue("@Prefix", prefix);
        seed.Parameters.AddWithValue("@Original", originalDocumentId);
        seed.Parameters.AddWithValue("@At", at);
        seed.Parameters.AddWithValue("@IssuedAt", at.ToString("O"));
        seed.CommandTimeout = 90;
        try
        {
            await seed.ExecuteNonQueryAsync();
            var firstIds = new HashSet<Guid>();
            for (var page = 1; page <= 2; page++)
            {
                var samples = new List<double>();
                for (var round = 0; round <= 20; round++)
                {
                    using var activity = new Activity("WithholdingReport.PageBudget").Start();
                    using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database, activity.TraceId);
                    var watch = Stopwatch.StartNew();
                    var result = await service.ListAppliedAsync(user, new(2026, 9, 29), new(2026, 9, 29), page, 25);
                    watch.Stop();
                    Assert.Equal(1, counter.Count);
                    Assert.Equal(10000, result.TotalCount);
                    Assert.Equal(2_500_000m, result.IncomeTaxTotal);
                    Assert.Equal(25, result.Items.Count);
                    Assert.All(result.Items, item => {
                        Assert.StartsWith(prefix, item.DocumentNumber);
                        Assert.False(string.IsNullOrWhiteSpace(item.SupplierName));
                        Assert.False(string.IsNullOrWhiteSpace(item.DocumentTypeLabel));
                        Assert.False(string.IsNullOrWhiteSpace(item.KindLabel));
                    });
                    if (round == 0)
                    {
                        if (page == 1) firstIds.UnionWith(result.Items.Select(item => item.DocumentId));
                        else Assert.DoesNotContain(result.Items, item => firstIds.Contains(item.DocumentId));
                    }
                    else samples.Add(watch.Elapsed.TotalMilliseconds);
                }
                var p95 = samples.Order().ElementAt(18);
                output.WriteLine("Withholding report, 10,000 supports/page {0}: 1 SQL, 20 samples, p95 {1:F1} ms.", page, p95);
                Assert.True(p95 < 1500, $"Withholding page {page} exceeded 1.5 s: {p95:F1} ms.");
            }
            var outside = await service.ListAppliedAsync(user with { TenantId = Guid.NewGuid() },
                new(2026, 9, 29), new(2026, 9, 29), 1, 25);
            Assert.Equal(0, outside.TotalCount);
            Assert.Empty(outside.Items);
            var last = await service.ListAppliedAsync(user, new(2026, 9, 29), new(2026, 9, 29), 401, 25);
            Assert.Equal(10000, last.TotalCount);
            Assert.Empty(last.Items);
            await using (var incomplete = new SqlCommand("""
                DELETE snapshot FROM fiscal.PurchaseSupportFiscalSnapshots snapshot
                JOIN dbo.FiscalDocuments document ON document.DocumentId=snapshot.DocumentId
                WHERE snapshot.DocumentId=@Id AND document.BusinessId=@Business AND document.FiscalNumber LIKE @Prefix+N'%';
                """, connection))
            {
                incomplete.Parameters.AddWithValue("@Id", firstIds.First());
                incomplete.Parameters.AddWithValue("@Business", fixture.BusinessId);
                incomplete.Parameters.AddWithValue("@Prefix", prefix);
                Assert.Equal(1, await incomplete.ExecuteNonQueryAsync());
            }
            var missing = await Assert.ThrowsAsync<TaxationValidationException>(() => service.ListAppliedAsync(user,
                new(2026, 9, 29), new(2026, 9, 29), 1, 25));
            Assert.Contains("integridad", missing.Message);
        }
        finally
        {
            await using var cleanupConnection = new SqlConnection(fixture.ConnectionString);
            await cleanupConnection.OpenAsync();
            await using var cleanup = new SqlCommand("""
                DECLARE @Owned TABLE(Id uniqueidentifier PRIMARY KEY);
                INSERT @Owned SELECT DocumentId FROM dbo.FiscalDocuments
                  WHERE BusinessId=@Business AND SourceDocumentType=N'ManualAccountingVoucher' AND FiscalNumber LIKE @Prefix+N'%';
                DELETE line FROM dbo.DocumentWithholdingLines line JOIN @Owned owned ON owned.Id=line.DocumentId;
                DELETE snapshot FROM dbo.DocumentWithholdingSnapshots snapshot JOIN @Owned owned ON owned.Id=snapshot.DocumentId;
                DELETE snapshot FROM fiscal.PurchaseSupportFiscalSnapshots snapshot JOIN @Owned owned ON owned.Id=snapshot.DocumentId;
                DELETE document FROM dbo.FiscalDocuments document JOIN @Owned owned ON owned.Id=document.DocumentId;
                """, cleanupConnection);
            cleanup.Parameters.AddWithValue("@Business", fixture.BusinessId);
            cleanup.Parameters.AddWithValue("@Prefix", prefix);
            cleanup.CommandTimeout = 90;
            await cleanup.ExecuteNonQueryAsync();
        }
    }
}
