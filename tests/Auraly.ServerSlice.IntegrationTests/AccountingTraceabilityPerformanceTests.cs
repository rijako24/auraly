using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountingTraceabilityPerformanceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly accounting traceability performance";
}

[Collection(AccountingTraceabilityPerformanceCollection.Name)]
[Trait("Performance", "AccountingTraceability")]
public sealed class AccountingTraceabilityPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task First_next_and_filtered_pages_have_bounded_IO_with_one_hundred_thousand_sources()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure });
        await service.EnsureDefaultsAsync(user);
        var accounts = await service.ListAccountsAsync(user);
        var lines = JsonSerializer.Serialize(new ManualVoucherLineRequest[] {
            new(accounts.First(account => account.Code == "519595").AccountId, null, null, "Gasto", 100m, 0m),
            new(accounts.First(account => account.Code == "111005").AccountId, null, null, "Contrapartida", 0m, 100m) });
        user = user with { BusinessId = Guid.NewGuid() };
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var setupDatabase = connection.Database;
        var setupSession = connection.ServerProcessId;
        var phasePath = Path.Combine(Path.GetTempPath(), $"auraly-traceability-{setupDatabase}-phase.json");
        Task MarkPhaseAsync(string phase) => File.WriteAllTextAsync(phasePath,
            JsonSerializer.Serialize(new { Phase = phase, Database = setupDatabase, SessionId = setupSession }));
        // Bulk fixture data is isolated from all other test collections. It is
        // never processed, published, or inserted into an application database.
        await using var seed = new SqlCommand("""
            SET NOCOUNT ON;
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
            VALUES(@BusinessId,@TenantId,N'Sede benchmark',N'',N'',N'',N'',N'');
            CREATE TABLE #Numbers(N int PRIMARY KEY);
            WITH Digit(N) AS(SELECT value FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(value))
            INSERT #Numbers SELECT a.N+10*b.N+100*c.N
            FROM Digit a CROSS JOIN Digit b CROSS JOIN Digit c;
            CREATE TABLE #Parties(N int PRIMARY KEY,PartyId uniqueidentifier);
            DECLARE @PartyStart int=0;
            WHILE @PartyStart<10000
            BEGIN
              INSERT #Parties SELECT N+@PartyStart,NEWID() FROM #Numbers ORDER BY N OPTION(MAXDOP 1);
              INSERT dbo.Parties WITH(TABLOCK)(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
              SELECT PartyId,@TenantId,N'NaturalPerson',CONCAT(N'Benchmark ',N),N'Incomplete',1,@UserId,@At
              FROM #Parties WHERE N>=@PartyStart AND N<@PartyStart+1000 OPTION(LOOP JOIN,MAXDOP 1);
              SET @PartyStart=@PartyStart+1000;
            END;
            -- Keep setup's LOB sort grants bounded without changing the measured
            -- volume, skew, timeout, query or latency gate.
            CREATE TABLE #SourceBatch(N int PRIMARY KEY,DocumentId uniqueidentifier,
              PartyId uniqueidentifier,OccurredAt datetimeoffset(7),Payload nvarchar(max));
            DECLARE @BatchStart int=0,@SourceBatchSize int=200;
            WHILE @BatchStart<100000
            BEGIN
              -- Keep source index maintenance below the wide LOB sort observed
              -- with 1000 rows. The history volume and measured query are unchanged.
              -- GUIDs are stored once before building their matching JSON.
              INSERT #SourceBatch(N,DocumentId,PartyId,OccurredAt)
              SELECT number.N+@BatchStart,NEWID(),party.PartyId,DATEADD(second,number.N+@BatchStart,@At)
              FROM #Numbers number JOIN #Parties party
                ON party.N=CASE WHEN number.N+@BatchStart<50000 THEN 0 ELSE (number.N+@BatchStart)%10000 END
              WHERE number.N<@SourceBatchSize
              ORDER BY number.N OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
              UPDATE document SET Payload=
                (SELECT document.DocumentId VoucherId,@BusinessId BusinessId,document.OccurredAt OccurredAt,
                  N'MANUAL_VOUCHER' ConceptCode,N'Benchmark' Description,document.PartyId MainPartyId,
                  JSON_QUERY(@Lines) Lines FOR JSON PATH,WITHOUT_ARRAY_WRAPPER)
              FROM #SourceBatch document;
              INSERT dbo.AccountingSourceDocuments WITH(TABLOCK)(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,
                PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
              SELECT DocumentId,N'ManualAccountingVoucher',@TenantId,@BusinessId,Payload,
                HASHBYTES('SHA2_256',CONVERT(varchar(max),Payload)),OccurredAt,@At,1 FROM #SourceBatch OPTION(LOOP JOIN,MAXDOP 1);
              INSERT dbo.AccountingPostingJobs WITH(TABLOCK)(AccountingPostingJobId,TenantId,BusinessId,
                SourceDocumentId,SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,
                Status,AttemptCount,CreatedAt)
              SELECT NEWID(),@TenantId,@BusinessId,source.SourceDocumentId,source.SourceDocumentType,
                source.PayloadHash,source.OccurredAt,1,N'Pending',0,@At
              FROM #SourceBatch batch JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=batch.DocumentId
              WHERE source.SourceDocumentType=N'ManualAccountingVoucher' AND source.BusinessId=@BusinessId
              OPTION(LOOP JOIN,FORCE ORDER,MAXDOP 1);
              TRUNCATE TABLE #SourceBatch;
              SET @BatchStart=@BatchStart+@SourceBatchSize;
            END;
            DROP TABLE #SourceBatch;
            DECLARE @MatchPartyId uniqueidentifier=(SELECT PartyId FROM #Parties WHERE N=1),
              @FrequentPartyId uniqueidentifier=(SELECT PartyId FROM #Parties WHERE N=0);
            DROP TABLE #Parties;
            DROP TABLE #Numbers;
            SELECT @MatchPartyId,@FrequentPartyId;
            """, connection) { CommandTimeout = 120 };
        seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        seed.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        seed.Parameters.AddWithValue("@UserId", fixture.UserId);
        seed.Parameters.AddWithValue("@At", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        seed.Parameters.AddWithValue("@Lines", lines);
        Guid partyId, frequentPartyId;
        await MarkPhaseAsync("Seeding");
        // The synthetic history is one isolated fixture, not 1,000 independent
        // source/job commits. Keep its bounded batches and real constraints,
        // and never leave half a history behind if setup fails.
        await using (var seedTransaction = (SqlTransaction)await connection.BeginTransactionAsync())
        {
            seed.Transaction = seedTransaction;
            try
            {
                await using (var seedResult = await seed.ExecuteReaderAsync())
                {
                    Assert.True(await seedResult.ReadAsync());
                    partyId = seedResult.GetGuid(0);
                    frequentPartyId = seedResult.GetGuid(1);
                }
                await seedTransaction.CommitAsync();
            }
            catch (SqlException error) when (error.Number == -2)
            {
                await MarkPhaseAsync("SeedFailed");
                output.WriteLine("Synthetic setup timed out before any page measurement.");
                try
                {
                    if (seedTransaction.Connection is not null) await seedTransaction.RollbackAsync();
                }
                catch (Exception rollbackError)
                {
                    throw new AggregateException("Synthetic setup timed out and its rollback also failed.", error, rollbackError);
                }
                throw;
            }
        }
        var migrationTimer = Stopwatch.StartNew();
        await MarkPhaseAsync("LinkingJobs");
        await fixture.ApplyMigrationAsync("20261022_LinkAccountingSourcesToJobs.sql", connection);
        output.WriteLine($"Link 100000 existing source jobs: {migrationTimer.Elapsed.TotalMilliseconds:F1} ms.");
        await using (var verify = new SqlCommand("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE AccountingPostingJobId IS NULL", connection))
            Assert.Equal(0, Convert.ToInt32(await verify.ExecuteScalarAsync()));
        await MarkPhaseAsync("Backfill");
        migrationTimer.Restart();
        await fixture.ApplyMigrationAsync("20261021_IndexManualVoucherParties.sql", connection);
        output.WriteLine($"Backfill 100000 sources: {migrationTimer.Elapsed.TotalMilliseconds:F1} ms.");
        await MarkPhaseAsync("Replay");
        migrationTimer.Restart();
        await fixture.ApplyMigrationAsync("20261021_IndexManualVoucherParties.sql", connection);
        output.WriteLine($"Idempotent backfill replay: {migrationTimer.Elapsed.TotalMilliseconds:F1} ms.");
        await using (var verify = new SqlCommand("SELECT COUNT(*) FROM accounting.SourcePartyIndex", connection))
            Assert.Equal(100_000, Convert.ToInt32(await verify.ExecuteScalarAsync()));
        await MarkPhaseAsync("Measuring");
        await connection.CloseAsync();
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var cases = new[] {
            (Name: "first", Page: 1, PartyId: (Guid?)null, Search: (string?)null, Total: 100_000, Items: 50),
            (Name: "next", Page: 2, PartyId: (Guid?)null, Search: (string?)null, Total: 100_000, Items: 50),
            (Name: "party", Page: 1, PartyId: (Guid?)partyId, Search: (string?)null, Total: 5, Items: 5),
            (Name: "frequent-party", Page: 1, PartyId: (Guid?)frequentPartyId, Search: (string?)null, Total: 50_005, Items: 50),
            (Name: "missing", Page: 1, PartyId: (Guid?)null, Search: (string?)"BENCH-NO-MATCH", Total: 0, Items: 0),
        };
        var exceededBudgets = new List<string>();
        (string Sql, string Name, int Page, Guid? PartyId, string? Search, double P95)? slowest = null;
        (string Sql, string Name, int Page, Guid? PartyId, string? Search, double P95)? grantScenario = null;
        foreach (var sample in cases)
        {
            var milliseconds = new List<double>(20);
            string? sql = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(database);
                var timer = Stopwatch.StartNew();
                AccountingDocumentPage result;
                try
                {
                    result = await service.ListDocumentsAsync(user, new(2026, 10, 1), new(2026, 10, 31),
                        null, null, sample.Search, sample.Page, 50, partyId: sample.PartyId);
                }
                catch (SqlException)
                {
                    var path = Path.Combine(Path.GetTempPath(), $"auraly-traceability-{database}-{sample.Name}-timeout.sql");
                    await File.WriteAllTextAsync(path, counter.LastCommandText ?? "");
                    output.WriteLine($"SQL failure in {sample.Name}, sample {attempt + 1}; command: {path}");
                    // Do not rerun a failed command during the timed samples.
                    throw;
                }
                timer.Stop();
                Assert.Equal(1, counter.Count);
                sql = counter.LastCommandText;
                Assert.Equal(sample.Total, result.TotalCount);
                Assert.Equal(sample.Items, result.Items.Count);
                Assert.Equal(result.Items.Count, result.Items.Select(item => item.SourceDocumentId).Distinct().Count());
                Assert.All(result.Items, item => Assert.Equal("Pending", item.Status));
                if (sample.PartyId is not null) Assert.All(result.Items, item => Assert.Equal(sample.PartyId, item.MainPartyId));
                milliseconds.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = milliseconds.Order().ElementAt(18);
            output.WriteLine($"100000 sources / 10000 parties, {sample.Name}: first {milliseconds[0]:F1} ms, p95 {p95:F1} ms, 1 SQL/page.");
            if (sample.Name == "frequent-party")
                grantScenario = (sql!, sample.Name, sample.Page, sample.PartyId, sample.Search, p95);
            if (p95 > 800)
            {
                exceededBudgets.Add($"{sample.Name}: p95 {p95:F1} ms exceeds the 800 ms inbox budget.");
                if (slowest is null || p95 > slowest.Value.P95)
                    slowest = (sql!, sample.Name, sample.Page, sample.PartyId, sample.Search, p95);
            }
        }
        await MarkPhaseAsync("Diagnosing");
        // Capture the skewed party's actual grant/spills after all timed samples,
        // or the slowest failure when there is one. Diagnostics never overlap
        // acceptance timings.
        if ((slowest ?? grantScenario) is { } failure)
        {
            await connection.OpenAsync();
            await using var diagnostic = new SqlCommand("SET STATISTICS XML ON;\n" + failure.Sql + "\nSET STATISTICS XML OFF;", connection);
            diagnostic.Parameters.AddWithValue("@TenantId", user.TenantId);
            diagnostic.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            diagnostic.Parameters.AddWithValue("@From", new DateTime(2026, 10, 1));
            diagnostic.Parameters.AddWithValue("@To", new DateTime(2026, 10, 31));
            diagnostic.Parameters.AddWithValue("@DocumentType", DBNull.Value);
            diagnostic.Parameters.AddWithValue("@Status", DBNull.Value);
            diagnostic.Parameters.AddWithValue("@Search", (object?)failure.Search ?? DBNull.Value);
            diagnostic.Parameters.AddWithValue("@PartyId", (object?)failure.PartyId ?? DBNull.Value);
            diagnostic.Parameters.AddWithValue("@Offset", (failure.Page - 1) * 50);
            diagnostic.Parameters.AddWithValue("@PageSize", 50);
            try
            {
                await using var reader = await diagnostic.ExecuteReaderAsync();
                var planNumber = 0;
                do
                {
                    if (reader.FieldCount != 1 || !reader.GetName(0).Contains("Showplan", StringComparison.OrdinalIgnoreCase)) continue;
                    while (await reader.ReadAsync())
                    {
                        var path = Path.Combine(Path.GetTempPath(), $"auraly-traceability-{database}-{failure.Name}-{planNumber++}.sqlplan");
                        var planText = reader.GetString(0);
                        await File.WriteAllTextAsync(path, planText);
                        var plan = XDocument.Parse(planText);
                        var grants = plan.Descendants().Where(node => node.Name.LocalName == "MemoryGrantInfo")
                            .Select(node => $"granted {node.Attribute("GrantedMemory")?.Value} KB, used {node.Attribute("MaxUsedMemory")?.Value} KB");
                        var spills = plan.Descendants().Count(node => node.Name.LocalName == "SpillToTempDb");
                        output.WriteLine($"Plan {planNumber - 1}: {string.Join("; ", grants)}, spill warnings {spills}.");
                        output.WriteLine($"Diagnostic plan after timed samples: {path}");
                    }
                } while (await reader.NextResultAsync());
            }
            catch (SqlException error) when (exceededBudgets.Count > 0)
            {
                output.WriteLine($"Additional actual-plan diagnostic failed with SQL {error.Number}; the measured budget failures remain below.");
            }
        }
        Assert.Empty(exceededBudgets);
    }

}
