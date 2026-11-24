using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PaymentReportingCorrectionsCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly payment reporting corrections";
}

[Collection(PaymentReportingCorrectionsCollection.Name)]
[Trait("EngineCertification", "Reporting")]
public sealed class ReportingPaymentCorrectionsTests(ServerSliceFixture fixture, Xunit.Abstractions.ITestOutputHelper output)
{
    [Fact]
    public async Task Uncorrected_sale_reporting_reads_only_selected_documents_with_large_history()
    {
        var sale=fixture.CreateUniqueValidRequest();
        using(var upload=fixture.CreateUploadMessage(sale))
        using(var response=await fixture.CreateClient().SendAsync(upload)) response.EnsureSuccessStatusCode();
        var marker=$"RP{Guid.NewGuid():N}"[..8];
        var history=Enumerable.Range(1,5000).Select(index=>new{Id=Guid.NewGuid(),Index=index}).ToArray();
        await using var connection=new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed=connection.CreateCommand();
        seed.CommandText="""
            DECLARE @Rows TABLE(Id uniqueidentifier PRIMARY KEY,[Index] int);
            INSERT @Rows SELECT Id,[Index] FROM OPENJSON(@Json) WITH(Id uniqueidentifier,[Index] int);
            INSERT dbo.SalesDocuments(DocumentId,BusinessId,WarehouseId,SourceMode,DocumentSeriesId,
              DocumentNumber,DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,DocumentType,IdempotencyKey,
              PayloadHash,IssuedAt,CustomerIdentification,CustomerNameSnapshot,UntaxedAmount,TaxAmount,
              PayableAmount,ProcessingStatus,ReceivedAt)
            SELECT Id,@Business,@Warehouse,N'Online',@Series,CONCAT(@Marker,N'-',[Index]),@Marker,N'QA',[Index],
              N'SalesReceipt',CONVERT(nvarchar(36),Id),HASHBYTES('SHA2_256',CONVERT(nvarchar(36),Id)),
              SYSDATETIMEOFFSET(),N'222222222222',REPLICATE(N'Historia sin relación ',10),10000,0,10000,
              N'Completed',SYSDATETIMEOFFSET() FROM @Rows;
            """;
        seed.Parameters.AddWithValue("@Json",JsonSerializer.Serialize(history));
        seed.Parameters.AddWithValue("@Business",fixture.BusinessId);
        seed.Parameters.AddWithValue("@Warehouse",fixture.WarehouseId);
        seed.Parameters.AddWithValue("@Series",fixture.OnlineSalesReceiptSeriesId);
        seed.Parameters.AddWithValue("@Marker",marker);
        try
        {
            // Synthetic read-model history is never passed through an engine.
            await seed.ExecuteNonQueryAsync();
            var reads=new List<string>();
            connection.InfoMessage+=(_,args)=>reads.Add(args.Message);
            await using(var statistics=connection.CreateCommand())
            {statistics.CommandText="SET STATISTICS IO ON";await statistics.ExecuteNonQueryAsync();}
            await using var transaction=(SqlTransaction)await connection.BeginTransactionAsync();
            using var scope=fixture.CreateScope();
            var writer=scope.ServiceProvider.GetRequiredService<SqlSalesReportingProjectionWriter>();
            var database=new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
            for(var attempt=0;attempt<2;attempt++)
            {
                reads.Clear();
                using var counter=new InventoryBalanceProcessingTests.CommandCounter(database);
                var watch=Stopwatch.StartNew();
                await writer.ProjectPaymentCorrectionAsync(new(connection,transaction),fixture.TenantId,
                    fixture.BusinessId,Guid.NewGuid(),"WorkSessionClosureReconciliation",[sale.DocumentId],CancellationToken.None);
                watch.Stop();
                var saleReads=reads.SelectMany(message=>Regex.Matches(message,
                    @"Table 'SalesDocuments'\. Scan count \d+, logical reads (\d+)").Cast<Match>())
                    .Sum(match=>int.Parse(match.Groups[1].Value));
                Assert.Contains(reads,message=>message.Contains("Table 'SalesDocuments'",StringComparison.Ordinal));
                output.WriteLine($"Uncorrected sale/5000 unrelated sales: attempt={attempt+1},SalesDocuments logical reads={saleReads},commands={counter.Count},elapsed={watch.Elapsed.TotalMilliseconds:F2}ms;budget=40reads,2commands,2000ms.");
                Assert.InRange(saleReads,1,40);
                Assert.Equal(2,counter.Count);
                Assert.True(watch.Elapsed.TotalMilliseconds<2000,"Unrelated history must not delay an uncorrected sale's projection.");
            }
            await transaction.RollbackAsync();
        }
        finally
        {
            await using var cleanup=connection.CreateCommand();
            cleanup.CommandText="DELETE dbo.SalesDocuments WHERE BusinessId=@Business AND DocumentPrefix=@Marker AND DocumentType=N'SalesReceipt'";
            cleanup.Parameters.AddWithValue("@Business",fixture.BusinessId);cleanup.Parameters.AddWithValue("@Marker",marker);
            await cleanup.ExecuteNonQueryAsync();
        }
    }

    [Theory]
    [InlineData("Cash", "Transfer")]
    [InlineData("Cash", "DebitCard")]
    [InlineData("DebitCard", "CreditCard")]
    public async Task Confirmed_cash_closure_projects_effective_tender_once(string original, string target)
    {
        var flow = new AccountingVerticalSliceTests(fixture, output);
        await flow.InitializeAsync();
        try
        {
            await flow.Closure_native_form_correction_preserves_invoice_and_uses_actual_accounts_once(
                original, target, false, false, false, false, verifyReporting: true);
        }
        finally { await flow.DisposeAsync(); }
    }

    [Theory]
    [InlineData("Transfer", "Cash", "None", true, true)]
    [InlineData("DebitCard", "Cash", "None", true, false)]
    [InlineData("CreditCard", "Cash", "None", true, false)]
    [InlineData("Cash", "Cash", "None", true, false)]
    public async Task Confirmed_dispatch_refreshes_only_actual_tenders_and_survives_replay_and_rebuild(
        string method, string original, string returned, bool before, bool rebuild)
    {
        var flow = new AccountingVerticalSliceTests(fixture, output);
        await flow.InitializeAsync();
        try
        {
            await flow.Order_dispatch_collection_uses_one_session_and_reclassifies_only_changed_accounts(
                "SalesReceipt", method, method == original, false, returnCase: returned,
                originalMethod: original, verifyReporting: true, projectBeforeSettlement: before, rebuildReporting: rebuild);
        }
        finally { await flow.DisposeAsync(); }
    }
}
