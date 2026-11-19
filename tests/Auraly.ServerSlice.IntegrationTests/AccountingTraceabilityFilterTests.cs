using System.Diagnostics;
using Auraly.Application.Catalog;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingTraceabilityFilterTests(ServerSliceFixture fixture, ITestOutputHelper output) : IAsyncLifetime
{
    private readonly List<Guid> readFixtureBusinesses = [];
    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => fixture.DeactivateReadFixtureBusinessesAsync(readFixtureBusinesses);

    [Fact]
    public async Task Native_receipts_and_egresses_share_voucher_filters_without_duplicate_documents_or_unbounded_reads()
    {
        var business = Guid.NewGuid(); var party = Guid.NewGuid();
        readFixtureBusinesses.Add(business);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            SET NOCOUNT ON;
            DECLARE @Customer uniqueidentifier=NEWID(),@Series uniqueidentifier=NEWID(),@SupplierSeries uniqueidentifier=NEWID(),
              @Manual uniqueidentifier=NEWID(),@Cross uniqueidentifier=NEWID(),@SupplierPayment uniqueidentifier=NEWID();
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
              VALUES(@Business,@Tenant,N'Recibos administrativos',N'',N'',N'',N'',N'');
            INSERT dbo.Parties(PartyId,TenantId,PartyType,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
              VALUES(@Party,@Tenant,N'Organization',N'Pagador del ensayo',N'Incomplete',1,@Actor,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(CustomerId,PartyId,TenantId,IsActive,CreatedAt)
              VALUES(@Customer,@Party,@Tenant,1,SYSDATETIMEOFFSET());
            INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DocumentType,Prefix,SeriesCode,Padding,
              RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(@Series,@Business,N'ReceivablePayment',N'RC',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET()),
                (@SupplierSeries,@Business,N'PayablePayment',N'CE',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            CREATE TABLE #Payments(N int PRIMARY KEY,Id uniqueidentifier,At datetimeoffset(7));
            WITH digit(N) AS(SELECT value FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(value))
            INSERT #Payments SELECT a.N+10*b.N+100*c.N+1000*d.N,NEWID(),
              DATEADD(second,a.N+10*b.N+100*c.N+1000*d.N,CONVERT(datetimeoffset,'2026-11-01T12:00:00+00:00'))
              FROM digit a CROSS JOIN digit b CROSS JOIN digit c CROSS JOIN digit d;
            INSERT dbo.CustomerPayments(PaymentId,BusinessId,CustomerId,DocumentSeriesId,DocumentNumber,DocumentPrefix,
              DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,TotalAmount,
              Status,ConfirmedByUserId,AcceptedAt)
            SELECT Id,@Business,@Customer,@Series,CONCAT(N'RC-',N),N'RC',N'00',N+1,CONVERT(nvarchar(36),Id),
              HASHBYTES('SHA2_256',N'receipt'),At,'COP',100,N'Accepted',@Actor,At FROM #Payments;
            DECLARE @Sources TABLE(Id uniqueidentifier,Type nvarchar(64),At datetimeoffset(7),Payload nvarchar(max),N int);
            INSERT @Sources SELECT Id,N'ReceivablePayment',At,N'{}',N FROM #Payments;
            INSERT @Sources VALUES
              (@Manual,N'ManualAccountingVoucher','2026-11-02T12:00:00+00:00',N'{"PayloadVersion":4,"VoucherTypeCode":"CashReceipt"}',10000),
              (@Cross,N'ManualAccountingVoucher','2026-11-03T12:00:00+00:00',N'{"PayloadVersion":4,"VoucherTypeCode":"Accounting"}',10001),
              (@SupplierPayment,N'PayablePayment','2026-11-04T12:00:00+00:00',N'{}',10002);
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
              PayloadHash,OccurredAt,AcceptedAt)
            SELECT Id,Type,@Tenant,@Business,Payload,HASHBYTES('SHA2_256',CONVERT(varbinary(max),Payload)),At,At
              FROM @Sources WHERE N<>9999;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
              SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
            SELECT NEWID(),@Tenant,@Business,Id,Type,HASHBYTES('SHA2_256',CONVERT(varbinary(max),Payload)),At,1,N'Pending',0,At
              FROM @Sources WHERE N<>9998;
            -- A child group has no independent source/job or number. It must not
            -- turn its parent's single receipt into another listed document.
            INSERT dbo.CustomerPayments(PaymentId,BusinessId,CustomerId,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,
              TotalAmount,Status,ConfirmedByUserId,AcceptedAt,ParentSourceDocumentId,ParentSourceDocumentType,SourceGroupId)
              VALUES(NEWID(),@Business,@Customer,N'manual-child',HASHBYTES('SHA2_256',N'child'),SYSDATETIMEOFFSET(),
                'COP',100,N'Accepted',@Actor,SYSDATETIMEOFFSET(),@Manual,N'ManualAccountingVoucher',NEWID());
            INSERT dbo.SupplierPayments(PaymentId,BusinessId,SupplierId,DocumentSeriesId,DocumentNumber,DocumentPrefix,
              DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,PaidAt,CurrencyCode,TotalAmount,
              Status,ConfirmedByUserId,AcceptedAt)
              VALUES(@SupplierPayment,@Business,@Supplier,@SupplierSeries,N'CE-1',N'CE',N'00',1,N'supplier',HASHBYTES('SHA2_256',N'egress'),
                '2026-11-04T12:00:00+00:00','COP',100,N'Accepted',@Actor,SYSDATETIMEOFFSET());
            SELECT @Manual,@Cross,@SupplierPayment;
            """, connection) { CommandTimeout = 120 };
        seed.Parameters.AddWithValue("@Business", business); seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        seed.Parameters.AddWithValue("@Party", party); seed.Parameters.AddWithValue("@Actor", fixture.UserId);
        seed.Parameters.AddWithValue("@Supplier", fixture.SupplierId);
        Guid manual, cross, supplier;
        await using (var result = await seed.ExecuteReaderAsync())
        {
            Assert.True(await result.ReadAsync());
            manual = result.GetGuid(0); cross = result.GetGuid(1); supplier = result.GetGuid(2);
        }
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId,fixture.TenantId,business,
            new HashSet<string> { AccountingPermissionCodes.Read });
        var from = new DateOnly(2026,11,1); var to = new DateOnly(2026,11,30);
        var cases = new[] {
            (Name:"first",Page:1,Party:(Guid?)null,Search:(string?)null,Status:(string?)null,Total:10001,Items:25),
            (Name:"next",Page:2,Party:(Guid?)null,Search:(string?)null,Status:(string?)null,Total:10001,Items:25),
            (Name:"party",Page:1,Party:(Guid?)party,Search:(string?)null,Status:(string?)null,Total:10000,Items:25),
            (Name:"number",Page:1,Party:(Guid?)party,Search:(string?)"RC-9999",Status:(string?)null,Total:1,Items:1),
            (Name:"missing-job",Page:1,Party:(Guid?)party,Search:(string?)null,Status:(string?)"MissingAccountingJob",Total:1,Items:1)
        };
        Guid? firstNative = null;
        await using var numberCommand=new SqlCommand { Connection=connection };
        await using var missingJobCommand=new SqlCommand { Connection=connection };
        var failures=new List<string>();
        foreach (var sample in cases)
        {
            var elapsed = new List<double>(20);
            for (var i=0;i<20;i++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    beforeExecute: command => {
                        if(sample.Name=="number"&&i==0)
                            PortfolioPaymentHistoryPerformanceTests.CaptureReadCommand(command,numberCommand);
                        if(sample.Name=="missing-job"&&i==0)
                            PortfolioPaymentHistoryPerformanceTests.CaptureReadCommand(command,missingJobCommand);
                    });
                var timer = Stopwatch.StartNew();
                var page = await service.ListDocumentsAsync(user,from,to,null,sample.Status,sample.Search,sample.Page,25,
                    partyId:sample.Party,voucherTypeCode:"CashReceipt");
                timer.Stop(); elapsed.Add(timer.Elapsed.TotalMilliseconds);
                Assert.Equal(1,counter.Count); Assert.Equal(sample.Total,page.TotalCount); Assert.Equal(sample.Items,page.Items.Count);
                Assert.All(page.Items,item=>Assert.Equal("CashReceipt",item.VoucherTypeCode));
                Assert.DoesNotContain(page.Items,item=>item.SourceDocumentId==cross||item.SourceDocumentId==supplier);
                Assert.Equal(page.Items.Count,page.Items.Select(item=>item.SourceDocumentId).Distinct().Count());
                if(sample.Name=="first")
                {
                    Assert.Equal(manual,page.Items[0].SourceDocumentId);
                    firstNative=page.Items[1].SourceDocumentId;
                }
                if(sample.Name=="next") Assert.DoesNotContain(page.Items,item=>item.SourceDocumentId==firstNative);
                if(sample.Party.HasValue) Assert.All(page.Items,item=> {
                    Assert.Equal(party,item.MainPartyId); Assert.Equal("Pagador del ensayo",item.MainPartyName);
                });
                if(sample.Name=="number") Assert.Equal("RC-9999",Assert.Single(page.Items).SourceDocumentNumber);
            }
            var p95=elapsed.Order().ElementAt(18);
            output.WriteLine($"10000 native receipts, {sample.Name}: first {elapsed[0]:F1} ms, p95 {p95:F1} ms, 1 SQL/page.");
            if(p95>800)failures.Add($"{sample.Name}: p95 {p95:F1} ms exceeds the 800 ms inbox budget.");
        }
        var profileDirectory=await PortfolioPaymentHistoryPerformanceTests.CaptureReportProfileAsync(numberCommand,output);
        if(failures.Count>0)
            await PortfolioPaymentHistoryPerformanceTests.CaptureReportProfileAsync(missingJobCommand,output);
        Assert.NotNull(profileDirectory);
        var partyCaptures=0;
        foreach(var file in Directory.EnumerateFiles(profileDirectory,"profile-*.json"))
        {
            using var profile=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file));
            var operators=profile.RootElement.EnumerateArray().ToArray();
            if(operators.Length==0||!operators[0].GetProperty("StmtText").GetString()!
                .StartsWith("INSERT #PartyDocumentKeys",StringComparison.Ordinal))continue;
            partyCaptures++;
            Assert.InRange(operators[0].GetProperty("Rows").GetInt64(),0,1);
            foreach(var item in operators.Where(item=>item.GetProperty("StmtText").GetString() is {} text
                &&System.Text.RegularExpressions.Regex.IsMatch(text,
                    @"OBJECT:\(\[[^\]]+\]\.\[dbo\]\.\[AccountingSourceDocuments\]\.")))
            {
                Assert.Contains("Seek",item.GetProperty("PhysicalOp").GetString());
                Assert.InRange(item.GetProperty("Rows").GetInt64(),0,1);
            }
        }
        Assert.True(partyCaptures>0,"The number-filtered read must restrict party membership to its search candidates.");
        Assert.Empty(failures);
        var egress=await service.ListDocumentsAsync(user,from,to,null,null,null,1,25,voucherTypeCode:"Disbursement");
        Assert.Equal(supplier,Assert.Single(egress.Items).SourceDocumentId);
        Assert.Equal("Disbursement",egress.Items[0].VoucherTypeCode);
        var wrongBusiness=await service.ListDocumentsAsync(user with { BusinessId=fixture.BusinessId },from,to,null,null,"RC-9999",1,25,
            voucherTypeCode:"CashReceipt");
        Assert.Empty(wrongBusiness.Items);
        var wrongTenant=await service.ListDocumentsAsync(user with { TenantId=Guid.NewGuid() },from,to,null,null,null,1,25,
            voucherTypeCode:"CashReceipt");
        Assert.Empty(wrongTenant.Items);
        using var lineCounter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
        var lines=await service.ListFinancialTraceabilityLinesAsync(user,from,to,null,null,"RC-9999",1,25,voucherTypeCode:"CashReceipt");
        Assert.Equal(firstNative,Assert.Single(lines.Items).SourceDocumentId);
        Assert.Equal(1,lineCounter.Count);
    }

    [Fact]
    public async Task Voucher_filter_precedes_pagination_and_restaurant_options_preserve_only_actual_history()
    {
        var businessId=Guid.NewGuid();
        readFixtureBusinesses.Add(businessId);
        await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
        await using var seed=new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
            VALUES(@Business,@Tenant,N'Filtros financieros',N'',N'',N'',N'',N'');
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt)
            SELECT NEWID(),SourceType,@Tenant,@Business,Payload,HASHBYTES('SHA2_256',CONVERT(varbinary(max),Payload)),OccurredAt,SYSDATETIMEOFFSET()
            FROM (VALUES
              (N'ManualAccountingVoucher',N'{"PayloadVersion":4,"VoucherTypeCode":"Accounting"}',CONVERT(datetimeoffset,'2026-11-02T10:00:00+00:00')),
              (N'ManualAccountingVoucher',N'{"PayloadVersion":4,"VoucherTypeCode":"Disbursement"}',CONVERT(datetimeoffset,'2026-11-03T10:00:00+00:00')),
              (N'ManualAccountingVoucher',N'{"PayloadVersion":2,"VoucherTypeCode":"Accounting"}',CONVERT(datetimeoffset,'2026-11-04T10:00:00+00:00')),
              (N'RestaurantTip',N'{}',CONVERT(datetimeoffset,'2026-11-05T10:00:00+00:00'))
            ) source(SourceType,Payload,OccurredAt);
            INSERT accounting.VoucherDrafts(DocumentId,TenantId,BusinessId,DocumentType,OccurredAt,ConceptCode,Description,
              CurrencyCode,CreatedBy,CreatedAt,UpdatedBy,UpdatedAt,ContentHash,PayloadVersion,VoucherTypeCode)
            VALUES(NEWID(),@Tenant,@Business,N'ManualAccountingVoucher','2026-11-01T10:00:00+00:00',N'General',N'Borrador',
              N'COP',@Actor,SYSDATETIMEOFFSET(),@Actor,SYSDATETIMEOFFSET(),HASHBYTES('SHA2_256',N'draft'),4,N'Accounting');
            """,connection);
        seed.Parameters.AddWithValue("@Business",businessId);seed.Parameters.AddWithValue("@Tenant",fixture.TenantId);seed.Parameters.AddWithValue("@Actor",fixture.UserId);
        await seed.ExecuteNonQueryAsync();
        using var scope=fixture.CreateScope();var service=scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user=new AccountingUserIdentity(fixture.UserId,fixture.TenantId,businessId,new HashSet<string>{AccountingPermissionCodes.Read});
        var from=new DateOnly(2026,11,1);var to=new DateOnly(2026,11,30);
        AccountingDocumentPage first;
        using(var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            first=await service.ListDocumentsAsync(user,from,to,"ManualAccountingVoucher",null,null,1,1,voucherTypeCode:"Accounting");
            Assert.Equal(1,counter.Count);
        }
        var second=await service.ListDocumentsAsync(user,from,to,"ManualAccountingVoucher",null,null,2,1,voucherTypeCode:"Accounting");
        Assert.Equal(2,first.TotalCount);Assert.Equal(2,second.TotalCount);
        Assert.Equal("Accounting",Assert.Single(first.Items).VoucherTypeCode);
        Assert.Equal("Accounting",Assert.Single(second.Items).VoucherTypeCode);
        Assert.NotEqual(first.Items[0].SourceDocumentId,second.Items[0].SourceDocumentId);
        var unrelated=await service.ListDocumentsAsync(user,from,to,"RestaurantTip",null,null,1,25,voucherTypeCode:"Accounting");
        Assert.Empty(unrelated.Items);Assert.Equal(0,unrelated.TotalCount);
        await Assert.ThrowsAsync<AccountingValidationException>(()=>service.ListDocumentsAsync(user,from,to,null,null,null,1,25,voucherTypeCode:"not-in-catalog"));
        var references=scope.ServiceProvider.GetRequiredService<ReferenceOptionService>();
        var identity=new CatalogUserIdentity(user.UserId,user.TenantId,user.BusinessId,user.Permissions);
        var current=await references.ListContextualAsync(identity,"accounting-document-type",null,null);
        Assert.DoesNotContain(current,option=>option.Code=="RestaurantTip");
        var history=await references.ListContextualAsync(identity,"accounting-document-type",from,to);
        Assert.EndsWith("(histórico)",Assert.Single(history.Where(option=>option.Code=="RestaurantTip")).Label);
        Assert.DoesNotContain(history,option=>option.Code is "RestaurantTipReversal" or "RestaurantTipSettlement");
    }
}
