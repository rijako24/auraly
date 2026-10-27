using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingAccountMovementTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Auxiliary_preserves_opening_party_scope_totals_and_balance_between_pages()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user,new(new(2026,1,1),"COP","ZeroDeclared"));
        var code = $"839{System.Security.Cryptography.RandomNumberGenerator.GetInt32(100,1000)}";
        var parent = await service.CreateAccountAsync(user,new(Guid.NewGuid(),fixture.TenantId,code,"Agrupación del auxiliar","OrderDebit",false,false));
        var first = await service.CreateAccountAsync(user,new(Guid.NewGuid(),fixture.TenantId,code+"01","Primer auxiliar","OrderDebit",true,true,
            new(parent.AccountId,"General","Debit",null,null,null)));
        var second = await service.CreateAccountAsync(user,new(Guid.NewGuid(),fixture.TenantId,code+"02","Segundo auxiliar","OrderDebit",true,true,
            new(parent.AccountId,"General","Debit",null,null,null)));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Guid otherParty;
        await using (var party = new SqlCommand("SELECT TOP(1) PartyId FROM dbo.Customers WHERE TenantId=@TenantId AND PartyId<>@PartyId ORDER BY CustomerId",connection))
        {
            party.Parameters.AddWithValue("@TenantId",fixture.TenantId);
            party.Parameters.AddWithValue("@PartyId",fixture.SupplierPartyId);
            otherParty = (Guid)(await party.ExecuteScalarAsync())!;
        }
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend);
        await PostAsync(1,50,fixture.SupplierPartyId);
        await PostAsync(10,100,fixture.SupplierPartyId);
        await PostAsync(11,-25,fixture.SupplierPartyId);
        await PostAsync(12,40,otherParty);
        await PostAsync(25,999,fixture.SupplierPartyId);

        var query = new AccountMovementQuery(new(2026,10,10),new(2026,10,20),1,1,first.Code,second.Code,fixture.SupplierPartyId);
        await service.GetAccountMovementsPageAsync(user,query); // Warm up outside timing.
        using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
        var watch = Stopwatch.StartNew();
        var page = await service.GetAccountMovementsPageAsync(user,query);
        watch.Stop();
        Assert.Equal(1,counter.Count);
        Assert.True(watch.Elapsed<TimeSpan.FromMilliseconds(750),$"Auxiliary: {watch.Elapsed.TotalMilliseconds:F1} ms");
        output.WriteLine($"Auxiliar: {counter.Count} SQL, {watch.Elapsed.TotalMilliseconds:F1} ms");
        Assert.Equal(4,page.TotalCount);
        Assert.Equal(4,page.TotalPages);
        var row = Assert.Single(page.Items);
        Assert.Equal(150m,row.Balance);
        Assert.Equal(first.Code,row.AccountCode);
        Assert.Equal(fixture.SupplierPartyId,row.PartyId);
        Assert.False(string.IsNullOrWhiteSpace(row.PartyName));
        Assert.Null(row.BaseAmount);
        var total = Assert.Single(page.Accounts,item=>item.AccountCode==first.Code);
        Assert.Equal(50m,total.OpeningBalance);
        Assert.Equal(100m,total.Debit);
        Assert.Equal(25m,total.Credit);
        Assert.Equal(125m,total.ClosingBalance);
        Assert.Equal(2,total.MovementCount);
        Assert.Null(total.BaseAmount);

        var next = await service.GetAccountMovementsPageAsync(user,query with { Page=2 });
        Assert.Equal(125m,Assert.Single(next.Items).Balance);
        Assert.Equal(page.Accounts,next.Accounts);
        var all = await client.GetFromJsonAsync<AccountMovementPage>(Url(query with { PageSize=50,PartyId=null }));
        Assert.Equal(6,all!.TotalCount);
        Assert.Contains(all.Items,item=>item.PartyId==otherParty);
        var openingOnly = await service.GetAccountMovementsPageAsync(user,query with { From=new(2026,10,13),PageSize=50 });
        Assert.Empty(openingOnly.Items);
        Assert.Equal(125m,Assert.Single(openingOnly.Accounts,item=>item.AccountCode==first.Code).ClosingBalance);
        Assert.Empty((await service.GetAccountMovementsPageAsync(user,query with { Page=100 })).Items);
        Assert.Empty((await service.GetAccountMovementsPageAsync(user,query with { PartyId=Guid.NewGuid() })).Accounts);
        Assert.Empty((await service.GetAccountMovementsPageAsync(user with { TenantId=Guid.NewGuid() },query)).Accounts);
        Assert.Empty((await service.GetAccountMovementsPageAsync(user with { BusinessId=Guid.NewGuid() },query)).Accounts);
        using var noRead = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        Assert.Equal(HttpStatusCode.Forbidden,(await noRead.GetAsync(Url(query))).StatusCode);
        foreach (var invalid in new[] {query with { Page=0 },query with { PageSize=201 },query with { AccountFrom=second.Code,AccountTo=first.Code },query with { To=query.From.AddDays(-1) }})
            Assert.Equal(HttpStatusCode.BadRequest,(await client.GetAsync(Url(invalid))).StatusCode);
        Assert.Equal(6,(await service.GetAccountMovementsPageAsync(user,query with { To=DateOnly.MaxValue,PageSize=50 })).TotalCount);

        // One canonical voucher establishes more than the new page maximum.
        // The old array API must remain compatible while the admin pages it.
        var largeId=Guid.NewGuid();
        var largeLines=Enumerable.Range(1,201).Select(index=>new ManualVoucherLineRequest(
            first.AccountId,fixture.SupplierPartyId,null,$"Línea {index}",1m,0m)).ToList();
        largeLines.Add(new(second.AccountId,fixture.SupplierPartyId,null,"Contrapartida del lote",0m,201m));
        using(var accepted=await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(largeId,fixture.BusinessId,new(2026,10,15,12,0,0,TimeSpan.Zero),
                "MANUAL_VOUCHER","Compatibilidad del auxiliar",largeLines,fixture.SupplierPartyId)))
            Assert.Equal(HttpStatusCode.Accepted,accepted.StatusCode);
        var legacyRows=await client.GetFromJsonAsync<AccountMovementRow[]>(
            $"/api/commerce/v1/accounting/reports/account-movements?accountCode={first.Code}&from=2026-10-10&to=2026-10-20");
        Assert.Equal(204,legacyRows!.Length);
        Assert.Equal(50,(await service.GetAccountMovementsPageAsync(user,query with { PageSize=50 })).Items.Count);

        using (var reportCommands = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var report = await service.GetAccountMovementReportAsync(user, query with { Page = 2 });
            Assert.Equal(1, reportCommands.Count);
            Assert.Equal(1, report.Page);
            Assert.True(report.TotalCount > 200);
            Assert.Equal(report.TotalCount, report.Items.Count);
            Assert.Equal(1, report.TotalPages);
            Assert.All(report.Items, item => Assert.Equal(fixture.SupplierPartyId, item.PartyId));
            Assert.Contains(report.Items, item => item.SourceDocumentId == largeId);
        }
        var printUrl = Url(query).Replace("/account-movements?", "/account-movements/print?");
        var printed = await client.GetFromJsonAsync<AccountMovementPage>(printUrl);
        Assert.Equal(printed!.TotalCount, printed.Items.Count);
        Assert.Equal(HttpStatusCode.Forbidden, (await noRead.GetAsync(printUrl)).StatusCode);
        Assert.Empty((await service.GetAccountMovementReportAsync(user with { BusinessId = Guid.NewGuid() }, query)).Items);
        // Exercise the same SQL guard at a small bound; it must reject before
        // details are enriched, without truncating a valid book or doing a second read.
        var store = scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        using (var reportCommands = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
        {
            var error = await Assert.ThrowsAsync<AccountingValidationException>(() =>
                store.GetAccountMovementsPageAsync(user, query, default, maximumRows: 3));
            Assert.Contains("Reduce el período", error.Message);
            Assert.Equal(1, reportCommands.Count);
        }

        async Task PostAsync(int day, decimal amount, Guid partyId)
        {
            var id = Guid.NewGuid();
            var request = new ConfirmManualAccountingVoucherRequest(id,fixture.BusinessId,
                new(2026,10,day,12,0,0,TimeSpan.Zero),"MANUAL_VOUCHER","Auxiliar por tercero",
                [new(first.AccountId,partyId,null,"Partida del auxiliar",Math.Max(amount,0),Math.Max(-amount,0)),
                 new(second.AccountId,partyId,null,"Contrapartida",Math.Max(-amount,0),Math.Max(amount,0))],otherParty);
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",request);
            Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
            using var entry = await client.GetAsync($"/api/commerce/v1/accounting/entries/by-document/{id}");
            Assert.Equal(HttpStatusCode.OK,entry.StatusCode);
        }
    }

    [Fact]
    public async Task Auxiliary_keeps_its_totals_and_rows_consistent_when_a_voucher_posts_between_read_stages()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var code = $"839{System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000)}";
        var account = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, code, "Auxiliar de lectura concurrente", "OrderDebit", true, false));
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        await PostAsync(7m);

        await using var gate = new SqlConnection(fixture.ConnectionString);
        await gate.OpenAsync();
        var resource = Guid.NewGuid().ToString("N");
        await using (var acquire = new SqlCommand("""
            DECLARE @Result int;
            EXEC @Result=sys.sp_getapplock @Resource=@Resource,@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0;
            SELECT @Result;
            """, gate))
        {
            acquire.Parameters.AddWithValue("@Resource", resource);
            Assert.True((int)(await acquire.ExecuteScalarAsync())! >= 0);
        }

        // The probe pauses the real SQL batch after its totals, before its page.
        // The concurrent voucher still enters through the public acceptance/motor.
        const string boundary = "WITH PeriodKeys AS (";
        var session = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var probe = new InventoryBalanceProcessingTests.CommandCounter(gate.Database,
            commandFilter: command => !session.Task.IsCompleted && command.CommandText.Contains(boundary, StringComparison.Ordinal)
                && Equals(command.Parameters["@AccountFrom"].Value, code),
            beforeExecute: command =>
            {
                command.CommandText = command.CommandText.Replace(boundary, """
                    EXEC sys.sp_getapplock @Resource=@AuxiliaryReadGate,@LockMode=N'Shared',@LockOwner=N'Session';
                    EXEC sys.sp_releaseapplock @Resource=@AuxiliaryReadGate,@LockOwner=N'Session';
                    WITH PeriodKeys AS (
                    """, StringComparison.Ordinal);
                command.Parameters.AddWithValue("@AuxiliaryReadGate", resource);
                session.TrySetResult(command.Connection!.ServerProcessId);
            });
        var query = new AccountMovementQuery(new(2026, 10, 1), new(2026, 10, 31), 1, 50, code, code);
        var reading = service.GetAccountMovementsPageAsync(user, query);
        Guid concurrentId;
        try
        {
            var sessionId = await session.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await using var waiting = new SqlCommand("""
                SELECT COUNT(*) FROM sys.dm_tran_locks
                WHERE request_session_id=@SessionId AND resource_type=N'APPLICATION' AND request_status=N'WAIT';
                """, gate);
            waiting.Parameters.AddWithValue("@SessionId", sessionId);
            while ((int)(await waiting.ExecuteScalarAsync(deadline.Token))! == 0)
                await Task.Delay(25, deadline.Token); // Bounded wait for the SQL barrier, not a timing assertion.
            concurrentId = await PostAsync(11m);
        }
        finally
        {
            await using var release = new SqlCommand("EXEC sys.sp_releaseapplock @Resource=@Resource,@LockOwner=N'Session';", gate);
            release.Parameters.AddWithValue("@Resource", resource);
            await release.ExecuteNonQueryAsync();
        }
        var page = await reading;
        Assert.Equal(1, probe.Count);
        Assert.Equal(page.TotalCount, page.Items.Count);
        var total = Assert.Single(page.Accounts);
        Assert.Equal(total.Debit, page.Items.Sum(item => item.Debit));
        Assert.Equal(total.Credit, page.Items.Sum(item => item.Credit));
        Assert.Equal(total.ClosingBalance, page.Items[^1].Balance);
        Assert.DoesNotContain(page.Items, item => item.SourceDocumentId == concurrentId);
        var refreshed = await service.GetAccountMovementsPageAsync(user, query);
        Assert.Contains(refreshed.Items, item => item.SourceDocumentId == concurrentId);
        Assert.Equal(18m, Assert.Single(refreshed.Accounts).Debit);

        async Task<Guid> PostAsync(decimal amount)
        {
            var id = Guid.NewGuid();
            using var accepted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
                new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId, new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero),
                    "MANUAL_VOUCHER", "Lectura concurrente",
                    [new(account.AccountId, null, null, "Débito", amount, 0m),
                     new(account.AccountId, null, null, "Crédito", 0m, amount)]));
            Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
            using var posted = await client.GetAsync($"/api/commerce/v1/accounting/entries/by-document/{id}");
            Assert.Equal(HttpStatusCode.OK, posted.StatusCode);
            return id;
        }
    }

    [Fact]
    [Trait("Performance", "AccountingAuxiliaryReport")]
    public async Task Full_auxiliary_uses_one_bounded_snapshot_and_rejects_oversize_before_detail_reads()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        foreach (var size in new[] { 200, 4998, 5000 })
        {
            var accountId = Guid.NewGuid();
            var code = "8" + BitConverter.ToUInt64(accountId.ToByteArray()).ToString("D20");
            await service.CreateAccountAsync(user, new(accountId, fixture.TenantId, code, "Volumen de informe auxiliar", "OrderDebit", true, true));
            // Synthetic balanced history only in this disposable database. The
            // report must also handle old posted entries without a source payload.
            await using (var seed = new SqlCommand("""
                SET XACT_ABORT ON; BEGIN TRANSACTION;
                DECLARE @Entry uniqueidentifier=NEWID(),@Document uniqueidentifier=NEWID(),
                  @At datetimeoffset(7)='2026-10-20T12:00:00+00:00';
                INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                  SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
                VALUES(NEWID(),@Tenant,@Business,@Document,N'ManualAccountingVoucher',HASHBYTES('SHA2_256',N'auxiliary-report-volume'),@At,1,N'Posted',1,@At);
                INSERT dbo.AccountingEntries(EntryId,TenantId,BusinessId,PeriodId,SourceDocumentId,SourceDocumentType,
                  EntryNumber,OccurredAt,PostedAt,Description,DebitTotal,CreditTotal,SourcePayloadHash,RuleVersion)
                SELECT TOP(1) @Entry,@Tenant,@Business,PeriodId,@Document,N'ManualAccountingVoucher',
                  LEFT(CONVERT(nvarchar(36),@Entry),24),@At,@At,N'Historia sintética de informe',@Count/2,@Count/2,
                  HASHBYTES('SHA2_256',N'auxiliary-report-volume'),1 FROM dbo.AccountingPeriods
                WHERE TenantId=@Tenant AND StartsOn<='20261020' AND EndsOn>='20261020';
                IF @@ROWCOUNT<>1 THROW 51400,N'El benchmark requiere su período contable.',1;
                WITH numbers AS(SELECT TOP(@Count) ROW_NUMBER() OVER(ORDER BY(SELECT NULL)) N
                  FROM sys.all_objects a CROSS JOIN sys.all_objects b)
                INSERT dbo.AccountingEntryLines(EntryId,LineNumber,AccountId,PartyId,PartyIdentificationSnapshot,
                  PartyNameSnapshot,Description,Debit,Credit,BaseAmount)
                SELECT @Entry,number.N,@Account,party.PartyId,party.Identification,party.DisplayName,N'Partida histórica',
                  CASE WHEN number.N%2=1 THEN 1 ELSE 0 END,CASE WHEN number.N%2=0 THEN 1 ELSE 0 END,
                  CASE WHEN number.N%10=0 THEN NULL ELSE 0 END
                FROM numbers number JOIN dbo.Parties party ON party.PartyId=@Party AND party.TenantId=@Tenant;
                IF @@ROWCOUNT<>@Count THROW 51400,N'La historia sintética quedó incompleta.',1;
                COMMIT;
                """, connection))
            {
                seed.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                seed.Parameters.AddWithValue("@Business", fixture.BusinessId);
                seed.Parameters.AddWithValue("@Account", accountId);
                seed.Parameters.AddWithValue("@Party", fixture.SupplierPartyId);
                seed.Parameters.AddWithValue("@Count", size);
                await seed.ExecuteNonQueryAsync();
            }
            var query = new AccountMovementQuery(new(2026, 10, 20), new(2026, 10, 20), AccountRootCode: code);
            if (size == 5000)
            {
                using var count = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var error = await Assert.ThrowsAsync<AccountingValidationException>(() => service.GetAccountMovementReportAsync(user, query));
                Assert.Contains("Reduce el período", error.Message);
                Assert.Equal(1, count.Count);
                using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read);
                using var rejected = await client.GetAsync($"/api/commerce/v1/accounting/reports/account-movements/print?from=2026-10-20&to=2026-10-20&accountRootCode={code}");
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                Assert.Equal(50, (await service.GetAccountMovementsPageAsync(user, query)).Items.Count);
                continue;
            }
            await service.GetAccountMovementReportAsync(user, query);
            var samples = new List<double>();
            for (var sample = 0; sample < 20; sample++)
            {
                using var activity = new Activity("auxiliary-full-report").SetIdFormat(ActivityIdFormat.W3C)
                    .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded).Start();
                using var count = new InventoryBalanceProcessingTests.CommandCounter(connection.Database, activity.TraceId);
                var timer = Stopwatch.StartNew();
                var report = await service.GetAccountMovementReportAsync(user, query);
                timer.Stop();
                samples.Add(timer.Elapsed.TotalMilliseconds);
                Assert.Equal(1, count.Count);
                Assert.Equal(size, report.TotalCount);
                Assert.Equal(size, report.Items.Count);
                Assert.Equal(0m, Assert.Single(report.Accounts).ClosingBalance);
                Assert.Equal(0m, report.Items[^1].Balance);
                Assert.Null(report.Accounts[0].BaseAmount);
                Assert.Contains(report.Items, item => item.BaseAmount == 0m);
                Assert.Contains(report.Items, item => item.BaseAmount is null);
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine("Libro completo, {0} movimientos: p95 {1:F1} ms, una consulta, 20 muestras.", size, p95);
            Assert.True(p95 < 2000, $"El informe completo de {size} movimientos tardó p95 {p95:F1} ms.");
        }
    }

    private static string Url(AccountMovementQuery query) =>
        $"/api/commerce/v1/accounting/reports/account-movements?from={query.From:yyyy-MM-dd}&to={query.To:yyyy-MM-dd}&page={query.Page}&pageSize={query.PageSize}&accountFrom={query.AccountFrom}&accountTo={query.AccountTo}"+
        (query.PartyId is null?"":$"&partyId={query.PartyId}");
}
