using System.Diagnostics;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(AccountingTraceabilityPerformanceCollection.Name)]
[Trait("Performance","FinancialConcepts")]
public sealed class FinancialConceptPerformanceTests(ServerSliceFixture fixture,ITestOutputHelper output)
{
    [Fact]
    public async Task Concept_pages_and_document_membership_filter_stay_bounded_with_ten_thousand_records()
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure);
        using(var defaults=await client.PutAsync("/api/commerce/v1/accounting/defaults",null))
            Assert.True(defaults.IsSuccessStatusCode,await defaults.Content.ReadAsStringAsync());
        var account=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,"4191","Revenue","General","Credit");
        var business=fixture.BusinessId;var selected=Guid.NewGuid();
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_",connection.Database);
        await using var seed=new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Side uniqueidentifier=(SELECT OptionId FROM reference.Options WHERE CatalogCode=N'accounting-balance-side' AND Code=N'Credit');
            CREATE TABLE #Numbers(N int PRIMARY KEY,ConceptId uniqueidentifier,DocumentId uniqueidentifier);
            WITH digit(N) AS(SELECT value FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(value))
            INSERT #Numbers SELECT a.N+10*b.N+100*c.N+1000*d.N,NEWID(),NEWID()
              FROM digit a CROSS JOIN digit b CROSS JOIN digit c CROSS JOIN digit d;
            INSERT reference.FinancialConcepts WITH(TABLOCK)(FinancialConceptId,TenantId,Code,Name,AccountId,SideOptionId,
              UseForReceipts,UseForSupplierPayments,UseForVouchers,IsActive,SortOrder,CreatedAt,UpdatedAt)
            SELECT ConceptId,@Tenant,CONCAT(N'BENCH-',RIGHT(CONCAT(N'00000',N),5)),CONCAT(N'Concepto ',N),@Account,@Side,
              1,0,1,1,10,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET() FROM #Numbers
            UNION ALL SELECT @Selected,@Tenant,N'TARGET',N'Concepto común',@Account,@Side,1,0,1,1,10,SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET()
              OPTION(LOOP JOIN,MAXDOP 1);
            CREATE TABLE #Batch(DocumentId uniqueidentifier,ConceptId uniqueidentifier,At datetimeoffset(7),Payload nvarchar(max));
            DECLARE @Start int=0;
            WHILE @Start<10000
            BEGIN
              INSERT #Batch
              SELECT DocumentId,CASE WHEN N%10=0 THEN @Selected ELSE ConceptId END,
                DATEADD(second,N,CONVERT(datetimeoffset,'2026-11-01T12:00:00+00:00')),
                CONCAT(N'{"PayloadVersion":4,"VoucherTypeCode":"CashReceipt","Lines":[{"FinancialConceptId":"',
                  CONVERT(nvarchar(36),CASE WHEN N%10=0 THEN @Selected ELSE ConceptId END),
                  N'"}],"FinancialEffects":{"Concepts":[{"FinancialConceptId":"',
                  CONVERT(nvarchar(36),CASE WHEN N%10=0 THEN @Selected ELSE ConceptId END),N'","Code":"BENCH","Name":"Concepto"}]}}')
              FROM #Numbers WHERE N>=@Start AND N<@Start+200;
              -- Bound setup's LOB sort grants, like the existing traceability benchmark.
              INSERT dbo.AccountingSourceDocuments WITH(TABLOCK)(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
                SELECT DocumentId,N'ManualAccountingVoucher',@Tenant,@Business,Payload,HASHBYTES('SHA2_256',CONVERT(varbinary(max),Payload)),At,At FROM #Batch OPTION(LOOP JOIN,MAXDOP 1);
              INSERT dbo.AccountingPostingJobs WITH(TABLOCK)(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
                SELECT NEWID(),@Tenant,@Business,DocumentId,N'ManualAccountingVoucher',HASHBYTES('SHA2_256',CONVERT(varbinary(max),Payload)),At,1,N'Pending',0,At FROM #Batch OPTION(LOOP JOIN,MAXDOP 1);
              INSERT accounting.SourceConceptIndex WITH(TABLOCK)(FinancialConceptId,SourceDocumentId,SourceDocumentType)
                SELECT ConceptId,DocumentId,N'ManualAccountingVoucher' FROM #Batch OPTION(LOOP JOIN,MAXDOP 1);
              TRUNCATE TABLE #Batch;SET @Start+=200;
            END;
            """,connection){CommandTimeout=120};
        seed.Parameters.AddWithValue("@Business",business);seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);
        seed.Parameters.AddWithValue("@Account",account.AccountId);seed.Parameters.AddWithValue("@Selected",selected);
        await seed.ExecuteNonQueryAsync();
        using var scope=fixture.CreateScope();var catalog=scope.ServiceProvider.GetRequiredService<FinancialConceptService>();
        var accounting=scope.ServiceProvider.GetRequiredService<AccountingService>();
        var permissions=new HashSet<string>{AccountingPermissionCodes.Read};
        var user=new CatalogUserIdentity(fixture.UserId,fixture.TenantId,business,permissions);
        var identity=new AccountingUserIdentity(fixture.UserId,fixture.TenantId,business,permissions);
        var cases=new[]{(Name:"master-first",Page:1,Documents:false,Search:(string?)null,Total:10001,Rows:50),
            (Name:"master-next",Page:2,Documents:false,Search:(string?)null,Total:10001,Rows:50),
            (Name:"master-search",Page:1,Documents:false,Search:(string?)"TARGET",Total:1,Rows:1),
            (Name:"receipts-first",Page:1,Documents:true,Search:(string?)null,Total:1000,Rows:50),
            (Name:"receipts-next",Page:2,Documents:true,Search:(string?)null,Total:1000,Rows:50)};
        var failures=new List<string>();string? documentSql=null;
        foreach(var sample in cases)
        {
            var elapsed=new List<double>(20);
            for(var i=0;i<20;i++)
            {
                using var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var timer=Stopwatch.StartNew();
                if(sample.Documents)
                {
                    var result=await accounting.ListDocumentsAsync(identity,new(2026,11,1),new(2026,11,30),null,null,null,
                        sample.Page,50,voucherTypeCode:"CashReceipt",financialConceptId:selected);
                    timer.Stop();Assert.Equal(sample.Total,result.TotalCount);Assert.Equal(sample.Rows,result.Items.Count);
                    Assert.Equal(result.Items.Count,result.Items.Select(item=>item.SourceDocumentId).Distinct().Count());
                }
                else
                {
                    var result=await catalog.ListAsync(user,new(sample.Page,50,sample.Search,Usage:"Receipts"));
                    timer.Stop();Assert.Equal(sample.Total,result.TotalCount);Assert.Equal(sample.Rows,result.Items.Count);
                }
                Assert.Equal(1,counter.Count);elapsed.Add(timer.Elapsed.TotalMilliseconds);
                if(sample.Documents)documentSql=counter.LastCommandText;
            }
            var p95=elapsed.Order().ElementAt(18);
            output.WriteLine($"10001 concepts / 10000 sources, {sample.Name}: first {elapsed[0]:F1} ms, p95 {p95:F1} ms, 1 SQL/page.");
            if(p95>800||elapsed[0]>2000)failures.Add($"{sample.Name}: first {elapsed[0]:F1} ms, p95 {p95:F1} ms exceeds 2000/800 ms.");
            if(p95>800)output.WriteLine("Samples: "+string.Join(", ",elapsed.Select(value=>value.ToString("F1"))));
        }
        if(failures.Count>0&&documentSql is not null)
        {
            // Diagnose only after all timed samples. Never run plan capture in parallel.
            await using var diagnostic=new SqlCommand("SET STATISTICS XML ON;\n"+documentSql+"\nSET STATISTICS XML OFF;",connection);
            diagnostic.Parameters.AddWithValue("@TenantId",fixture.TenantId);diagnostic.Parameters.AddWithValue("@BusinessId",business);
            diagnostic.Parameters.AddWithValue("@From",new DateTime(2026,11,1));diagnostic.Parameters.AddWithValue("@To",new DateTime(2026,11,30));
            foreach(var name in new[]{"@DocumentType","@Status","@Search","@PartyId"})diagnostic.Parameters.AddWithValue(name,DBNull.Value);
            diagnostic.Parameters.AddWithValue("@VoucherTypeCode","CashReceipt");diagnostic.Parameters.AddWithValue("@FinancialConceptId",selected);
            diagnostic.Parameters.AddWithValue("@Offset",0);diagnostic.Parameters.AddWithValue("@PageSize",50);
            await using var reader=await diagnostic.ExecuteReaderAsync();var index=0;
            do
            {
                if(reader.FieldCount!=1||!reader.GetName(0).Contains("Showplan",StringComparison.OrdinalIgnoreCase))continue;
                while(await reader.ReadAsync())
                {
                    var path=Path.Combine(Path.GetTempPath(),$"auraly-concepts-{connection.Database}-{index++}.sqlplan");
                    await File.WriteAllTextAsync(path,reader.GetString(0));output.WriteLine("Diagnostic plan: "+path);
                }
            }while(await reader.NextResultAsync());
        }
        Assert.Empty(failures);
    }
}
