using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Auraly.Infrastructure.Persistence;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Auraly.Contracts.WorkSessions;
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
    public async Task Sale_projects_before_closing_and_a_blind_closure_does_not_change_its_projection()
    {
        var user = await fixture.CreateWorkSessionUserAsync("report-without-closure");
        var identity = new WorkSessionIdentity(user, fixture.TenantId,
            new HashSet<string> { WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close }, fixture.BusinessId);
        using var scope = fixture.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<Auraly.Application.WorkSessions.WorkSessionService>();
        var session = Guid.NewGuid();
        await sessions.RegisterDeviceOpenAsync(identity,
            new(user, session, fixture.BusinessId, new(2026, 7, 27, 10, 0, 0, TimeSpan.Zero)), fixture.DeviceId);
        var sale = fixture.CreateUniqueValidRequest() with { WorkSessionId = session, SoldByUserId = user };
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var response = await fixture.CreateClient().SendAsync(upload))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var processor = scope.ServiceProvider.GetRequiredService<SqlSalesReportingProcessor>();
        await processor.ProcessAsync(sale.DocumentId, sale.CommercialSnapshot.DocumentType, fixture.BusinessId, 1, default);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var snapshot = connection.CreateCommand();
        snapshot.CommandText = """
            SELECT (SELECT (SELECT * FROM reporting.SalesReportDocuments WHERE DocumentId=@Document FOR JSON PATH,INCLUDE_NULL_VALUES) Documents,
              (SELECT * FROM reporting.SalesReportLineFacts WHERE SourceDocumentId=@Document ORDER BY FactId FOR JSON PATH,INCLUDE_NULL_VALUES) Lines,
              (SELECT * FROM reporting.SalesReportPaymentFacts WHERE SourceDocumentId=@Document ORDER BY PaymentNumber FOR JSON PATH,INCLUDE_NULL_VALUES) Payments,
              (SELECT * FROM reporting.SalesReportDailyTotals WHERE BusinessId=@Business ORDER BY BusinessLocalDate,CurrencyCode FOR JSON PATH,INCLUDE_NULL_VALUES) Daily
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """;
        snapshot.Parameters.AddWithValue("@Document", sale.DocumentId);
        snapshot.Parameters.AddWithValue("@Business", fixture.BusinessId);
        var before = (string)(await snapshot.ExecuteScalarAsync())!;
        using var projected = JsonDocument.Parse(before);
        Assert.Equal(1, projected.RootElement.GetProperty("Documents").GetArrayLength());
        var closure = await sessions.CloseFromDeviceAsync(identity, session, Guid.NewGuid().ToString("N"),
            new(0, "Declaración ciega sin corregir ventas", fixture.UserId, [new("Cash", 0), new("Transfer", 0), new("Card", 0)]));
        Assert.NotEqual(Guid.Empty, closure.WorkSessionClosureId);
        await processor.ProcessAsync(sale.DocumentId, sale.CommercialSnapshot.DocumentType, fixture.BusinessId, 1, default);
        Assert.Equal(before, (string)(await snapshot.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData("Pending", "Pending", false)]
    [InlineData("AccountingPendingConfiguration", "Failed", false)]
    [InlineData("Posted", "Pending", true)]
    [InlineData("CommercialEffectsApplied", "Processing", true)]
    [InlineData("Posted", "Failed", true)]
    [InlineData("Posted", "Projected", false)]
    public async Task Pending_projection_counts_only_financial_corrections_that_have_committed(string financialStatus, string reportingStatus, bool eligible)
    {
        var sale = fixture.CreateUniqueValidRequest();
        using (var upload = fixture.CreateUploadMessage(sale))
        using (var accepted = await fixture.CreateClient().SendAsync(upload))
            Assert.True(accepted.IsSuccessStatusCode, await accepted.Content.ReadAsStringAsync());
        using var scope = fixture.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ISalesReportingStore>();
        var identity = new SalesReportingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { SalesReportingPermissionCodes.Read, SalesReportingPermissionCodes.ReadAll });
        var before = await store.GetContextAsync(identity, default);
        var document = Guid.NewGuid(); var financialJob = Guid.NewGuid();
        var payload = JsonSerializer.Serialize(new WorkSessionClosureReconciliationPayload(
            document, Guid.NewGuid(), fixture.TenantId, fixture.BusinessId, fixture.UserId,
            sale.CommercialSnapshot.IssuedAt, [], []), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = connection.CreateCommand();
        seed.CommandText = """
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            VALUES(@Document,N'WorkSessionClosureReconciliation',@Tenant,@Business,@Payload,@Hash,SYSUTCDATETIME(),SYSUTCDATETIME(),0);
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,CreatedAt)
            VALUES(@Job,@Tenant,@Business,@Document,N'WorkSessionClosureReconciliation',@Hash,SYSUTCDATETIME(),0,@Status,SYSUTCDATETIME());
            INSERT reporting.SalesReportingJobs(SalesReportingJobId,BusinessId,SourceDocumentId,SourceDocumentType,SourceVersion,SourcePayloadHash,SourcePayloadJson,Status,CreatedAt)
            VALUES(NEWID(),@Business,@Document,N'WorkSessionClosureReconciliation',1,@Hash,@Payload,@ReportingStatus,SYSUTCDATETIME());
            """;
        seed.Parameters.AddWithValue("@Document", document); seed.Parameters.AddWithValue("@Job", financialJob);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId); seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
        seed.Parameters.AddWithValue("@Payload", payload); seed.Parameters.AddWithValue("@Hash", hash);
        seed.Parameters.AddWithValue("@Status", financialStatus);
        seed.Parameters.AddWithValue("@ReportingStatus", reportingStatus);
        try
        {
            await seed.ExecuteNonQueryAsync();
            using (var reads = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
            {
                var context = await store.GetContextAsync(identity, default);
                Assert.Equal(before.PendingSourceCount + (eligible && reportingStatus is "Pending" or "Processing" ? 1 : 0), context.PendingSourceCount);
                Assert.Equal(before.FailedSourceCount + (eligible && reportingStatus == "Failed" ? 1 : 0), context.FailedSourceCount);
                Assert.Equal(context.PendingSourceCount > 0, context.HasPendingSources);
                Assert.Equal(context.FailedSourceCount > 0, context.HasFailedSources);
                Assert.Equal(2, reads.Count);
            }
            Assert.NotNull(await store.GetDocumentAsync(identity, sale.DocumentId, default));
        }
        finally
        {
            seed.CommandText = "DELETE reporting.SalesReportingJobs WHERE SourceDocumentId=@Document; DELETE dbo.AccountingPostingJobs WHERE SourceDocumentId=@Document; DELETE dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Document;";
            await seed.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Confirmed_correction_reports_its_tender_without_inventing_historical_account_bindings()
    {
        var flow=new AccountingVerticalSliceTests(fixture,output);
        await flow.InitializeAsync();
        try
        {
            await flow.Closure_native_form_correction_preserves_invoice_and_uses_actual_accounts_once(
                "DebitCard","Cash",false,false,false,false,verifyReporting:true,missingHistoricalBindings:true);
        }
        finally{await flow.DisposeAsync();}
    }

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
