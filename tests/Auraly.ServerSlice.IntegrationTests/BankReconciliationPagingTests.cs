using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BankReconciliationPagingCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Bank reconciliation bounded pages";
}

[Collection(BankReconciliationPagingCollection.Name)]
public sealed class BankReconciliationPagingTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData("amount")]
    [InlineData("balance")]
    [InlineData("reference")]
    public async Task Invalid_statement_values_are_rejected_before_storage_or_rounding(string field)
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.BankReconciliationManage);
        var bytes=Encoding.UTF8.GetBytes("Archivo de validación");
        var row=new BankStatementLineImport(1,new(2026,9,1),"Movimiento",
            field=="reference"?new string('x',161):null,field=="amount"?1.00001m:1m,field=="balance"?1.00001m:1m);
        using var counter=new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("INSERT accounting.BankReconciliations",StringComparison.Ordinal));
        using var response=await client.PostAsJsonAsync("/api/commerce/v1/accounting/bank-reconciliations",
            new ImportBankReconciliationRequest(Guid.NewGuid(),Guid.NewGuid(),new(2026,9,1),new(2026,9,30),0m,1m,
                "validacion.csv",Convert.ToHexString(SHA256.HashData(bytes)),Convert.ToBase64String(bytes),[row]));
        Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        Assert.Equal(0,counter.Count);
    }

    [Fact]
    public async Task Ten_thousand_movements_and_one_thousand_periods_are_paged_and_filtered_before_enrichment()
    {
        await fixture.GrantReconciliationRolePermissionsAsync();
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.BankReconciliationRead, AccountingPermissionCodes.BankReconciliationManage);
        var accountId = Guid.NewGuid();
        using (var created = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(accountId, fixture.TenantId, "111999991", "Banco paginado", "Asset", true, false)))
            created.EnsureSuccessStatusCode();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        Guid optionId;
        await using (var option = new SqlCommand("SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking';", connection))
            optionId = (Guid)(await option.ExecuteScalarAsync())!;
        var bankId = Guid.NewGuid();
        using (var bank = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId:D}",
            new SaveBankAccountRequest(bankId, accountId, optionId, "Banco de prueba", "10000", "Banco paginado", false, true, null)))
            Assert.True(bank.IsSuccessStatusCode, await bank.Content.ReadAsStringAsync());
        var id = Guid.NewGuid();
        var rows = Enumerable.Range(1, 10_000).Select(n => new BankStatementLineImport(n,
            new DateOnly(2026, 9, 5), $"Extracto fila-{n:D5}", $"REF-{n:D5}", 1m, n)).ToArray();
        var file = Encoding.UTF8.GetBytes("Extracto aislado de prueba de paginación");
        using (var imported = await client.PostAsJsonAsync("/api/commerce/v1/accounting/bank-reconciliations",
            new ImportBankReconciliationRequest(id, bankId, new(2026, 9, 1), new(2026, 9, 30), 0m, 10_000m,
                "extracto-volumen.csv", Convert.ToHexString(SHA256.HashData(file)), Convert.ToBase64String(file), rows)))
        {
            Assert.True(imported.StatusCode == HttpStatusCode.Created, await imported.Content.ReadAsStringAsync());
            var accepted = (await imported.Content.ReadFromJsonAsync<BankReconciliationDetailView>())!;
            Assert.Equal(50, accepted.StatementLines.Count);
            Assert.Equal(10_000, accepted.Paging.StatementCount);
        }
        await using (var seed = new SqlCommand("""
            SET NOCOUNT ON; SET XACT_ABORT ON; BEGIN TRANSACTION;
            WITH D(N) AS(SELECT N FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) digit(N))
            SELECT a.N+10*b.N+100*c.N+1000*d.N+1 N,NEWID() EntryId,NEWID() SourceId INTO #Rows
            FROM D a CROSS JOIN D b CROSS JOIN D c CROSS JOIN D d;
            DECLARE @PeriodId uniqueidentifier=(SELECT PeriodId FROM dbo.AccountingPeriods
              WHERE TenantId=@TenantId AND StartsOn<='20260905' AND EndsOn>='20260905');
            DECLARE @Counterpart uniqueidentifier=(SELECT AccountId FROM dbo.AccountingAccounts WHERE TenantId=@TenantId AND Code=N'519595');
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
              PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT SourceId,N'BankPagingTest',@TenantId,@BusinessId,N'{}',HASHBYTES('SHA2_256',CONVERT(binary(16),SourceId)),
              '2026-09-05T12:00:00+00:00',SYSUTCDATETIME(),1 FROM #Rows;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
              SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,LastAttemptAt,CompletedAt,ResolvedBankAccountsJson)
            SELECT NEWID(),@TenantId,@BusinessId,SourceId,N'BankPagingTest',HASHBYTES('SHA2_256',CONVERT(binary(16),SourceId)),
              '2026-09-05T12:00:00+00:00',1,N'Posted',1,SYSUTCDATETIME(),SYSUTCDATETIME(),SYSUTCDATETIME(),N'[]' FROM #Rows;
            INSERT dbo.AccountingEntries(EntryId,TenantId,BusinessId,PeriodId,SourceDocumentId,SourceDocumentType,
              EntryNumber,OccurredAt,PostedAt,Description,DebitTotal,CreditTotal,SourcePayloadHash,RuleVersion)
            SELECT EntryId,@TenantId,@BusinessId,@PeriodId,SourceId,N'BankPagingTest',CONCAT(N'BK-PAGE-',RIGHT(CONCAT(N'00000',N),5)),
              '2026-09-05T12:00:00+00:00',SYSUTCDATETIME(),N'Libro bancario de prueba',1,1,HASHBYTES('SHA2_256',CONVERT(binary(16),SourceId)),1 FROM #Rows;
            INSERT dbo.AccountingEntryLines(EntryId,LineNumber,AccountId,Description,Debit,Credit)
            SELECT EntryId,1,@AccountId,CONCAT(N'Libro fila-',RIGHT(CONCAT(N'00000',N),5)),1,0 FROM #Rows
            UNION ALL SELECT EntryId,2,@Counterpart,N'Contrapartida',0,1 FROM #Rows;
            INSERT accounting.BankReconciliations(ReconciliationId,TenantId,BusinessId,BankAccountId,PeriodFrom,PeriodTo,
              OpeningBalance,ClosingBalance,FileName,FileSha256,OriginalFile,Status,CreatedByUserId,CreatedAt,UpdatedByUserId,UpdatedAt)
            SELECT NEWID(),@TenantId,@BusinessId,@BankId,DATEADD(day,N,'20200101'),DATEADD(day,N,'20200101'),0,0,
              CONCAT(N'archivo-',RIGHT(CONCAT(N'0000',N),4),N'.csv'),HASHBYTES('SHA2_256',CONVERT(binary(16),SourceId)),0x01,
              N'Preparation',@UserId,SYSUTCDATETIME(),@UserId,SYSUTCDATETIME() FROM #Rows WHERE N<=1000;
            COMMIT TRANSACTION;
            """, connection))
        {
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@UserId", fixture.UserId);
            seed.Parameters.AddWithValue("@AccountId", accountId);
            seed.Parameters.AddWithValue("@BankId", bankId);
            await seed.ExecuteNonQueryAsync();
        }
        var detailPath = $"/api/commerce/v1/accounting/bank-reconciliations/{id:D}";
        var first = (await client.GetFromJsonAsync<BankReconciliationDetailView>(detailPath))!;
        var next = (await client.GetFromJsonAsync<BankReconciliationDetailView>(detailPath+"?statementPage=2&bookPage=2"))!;
        Assert.Equal(10_000, first.Paging.BookCount);
        Assert.Equal(10_000, first.Paging.UnmatchedBookCount);
        Assert.Equal(50, first.BookLines.Count);
        Assert.Empty(first.StatementLines.Select(x=>x.StatementLineId).Intersect(next.StatementLines.Select(x=>x.StatementLineId)));
        Assert.Empty(first.BookLines.Select(x=>x.EntryId).Intersect(next.BookLines.Select(x=>x.EntryId)));
        var filtered = (await client.GetFromJsonAsync<BankReconciliationDetailView>(detailPath+"?statementSearch=fila-10000&bookSearch=fila-10000"))!;
        Assert.Equal(10_000, Assert.Single(filtered.StatementLines).LineNumber);
        Assert.Equal("BK-PAGE-10000", Assert.Single(filtered.BookLines).EntryNumber);
        Assert.Equal(1, filtered.Paging.StatementCount);
        Assert.Equal(1, filtered.Paging.BookCount);
        Assert.Equal(10_000, filtered.Paging.UnmatchedBookCount);
        Assert.Equal(10_000m, filtered.Summary.BookMovementTotal);
        Assert.Equal(10_000m, filtered.Summary.StatementMovementTotal);
        var beyond = (await client.GetFromJsonAsync<BankReconciliationDetailView>(detailPath+"?statementPage=201&bookPage=201"))!;
        Assert.Empty(beyond.StatementLines);
        Assert.Empty(beyond.BookLines);
        Assert.Equal(10_000, beyond.Paging.BookCount);
        var listPath=$"/api/commerce/v1/accounting/bank-reconciliations?bankAccountId={bankId:D}&pageSize=20";
        var listFirst=(await client.GetFromJsonAsync<BankReconciliationPage>(listPath))!;
        var listNext=(await client.GetFromJsonAsync<BankReconciliationPage>(listPath+"&page=2"))!;
        Assert.Equal(1001,listFirst.TotalCount);
        Assert.Equal(20,listFirst.Items.Count);
        Assert.Empty(listFirst.Items.Select(x=>x.ReconciliationId).Intersect(listNext.Items.Select(x=>x.ReconciliationId)));
        var listFiltered=(await client.GetFromJsonAsync<BankReconciliationPage>(listPath+"&search=archivo-0001"))!;
        Assert.Equal("archivo-0001.csv",Assert.Single(listFiltered.Items).FileName);
        Assert.Equal(1,listFiltered.TotalCount);
        foreach(var invalid in new[]{"?pageSize=101","?bookPage=0","?statementSearch="+new string('x',161)})
        {
            using var rejected=await client.GetAsync(detailPath+invalid);
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        }
        foreach(var invalid in new[]{"&page=0","&sortBy=untrusted","&sortDirection=other"})
        {
            using var rejected=await client.GetAsync(listPath+invalid);
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        }
        // No builds or diagnostic queries run concurrently with this measurement.
        foreach(var sample in new (string Name,string Path,int Commands)[]{("first-detail",detailPath,6),("next-detail",detailPath+"?statementPage=2&bookPage=2",6),
            ("filtered-detail",detailPath+"?statementSearch=fila-10000&bookSearch=fila-10000",6),("first-list",listPath,2),("next-list",listPath+"&page=2",2)})
        {
            var times=new List<double>();
            for(var i=0;i<20;i++)
            {
                using var counter=new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    commandFilter:command=>command.CommandText.Contains("accounting.BankReconciliations",StringComparison.Ordinal)||
                      command.CommandText.Contains("accounting.BankStatementLines",StringComparison.Ordinal)||
                      command.CommandText.Contains("accounting.BankReconciliationAllocations",StringComparison.Ordinal)||
                      command.CommandText.Contains("accounting.BankReconciliationStatusEvents",StringComparison.Ordinal));
                var timer=Stopwatch.StartNew();
                using var response=await client.GetAsync(sample.Path);
                var json=await response.Content.ReadAsStringAsync();
                timer.Stop();
                Assert.True(response.IsSuccessStatusCode,json);
                Assert.Equal(sample.Commands,counter.Count);
                Assert.True(json.Length<120_000,$"An oversized response was returned: {json.Length} characters.");
                times.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95=times.Order().ElementAt(18);
            output.WriteLine($"10000 movements / 1001 periods, {sample.Name}: first {times[0]:F1} ms, p95 {p95:F1} ms, {sample.Commands} SQL/page.");
            Assert.True(p95<2000,$"{sample.Name} p95 {p95:F1} ms exceeds 2000 ms.");
        }
    }
}
