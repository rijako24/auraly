using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Payroll.Contracts;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingMappingFreezeTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Native_source_keeps_known_accounts_fills_missing_configuration_and_preserves_legacy_posted_replay(
        bool missingCounterpart, bool legacyPending)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        var first = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "5196", "Expense", "General", "Debit");
        var second = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "5197", "Expense", "General", "Debit");
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure });
        const string category = "SalaryExpense";
        var at = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        string previous;
        // An empty scoped result means there was no override to restore.
        // This SQL version serializes that empty set as NULL, so name it [] explicitly.
        await using (var backup = new SqlCommand("""
            SELECT COALESCE((SELECT MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt
            FROM dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND Category IN(@Category,N'NetPayrollPayable')
            FOR JSON PATH,INCLUDE_NULL_VALUES),N'[]');
            """, connection))
        {
            Bind(backup);
            previous = (string)(await backup.ExecuteScalarAsync() ?? throw new InvalidOperationException("The mapping snapshot was not returned."));
        }
        async Task SetAsync(Guid accountId) => await service.SetMappingAsync(user,
            new(fixture.TenantId, fixture.BusinessId, category, accountId, new(2026, 1, 1), null));
        SqlAccountingPostingJobWriter.Source Source() => PayrollSource(at, category);
        async Task AcceptAsync(SqlAccountingPostingJobWriter.Source source)
        {
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            Assert.Equal(1, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                [source], at, AccountingJobRequirement.AccountingEntryOnly, default));
            await transaction.CommitAsync();
        }
        async Task<string?> SnapshotAsync(Guid id)
        {
            await using var read = new SqlCommand("SELECT ResolvedAccountMappingsJson FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND BusinessId=@BusinessId", connection);
            read.Parameters.AddWithValue("@Id", id);
            read.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            return await read.ExecuteScalarAsync() as string;
        }
        try
        {
            await SetAsync(first.AccountId);
            if (missingCounterpart)
            {
                await using var remove = new SqlCommand("DELETE dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND Category=N'NetPayrollPayable'", connection);
                Bind(remove);
                await remove.ExecuteNonQueryAsync();
            }
            var original = Source();
            await AcceptAsync(original);
            var frozen = await SnapshotAsync(original.DocumentId);
            Assert.NotNull(frozen);
            using (var values = JsonDocument.Parse(frozen))
                Assert.Equal(first.AccountId, values.RootElement.EnumerateArray().Single(value => value.GetProperty("Category").GetString() == category)
                    .GetProperty("AccountId").GetGuid());
            await SetAsync(second.AccountId);
            await AcceptAsync(original with { JobId = Guid.NewGuid() });
            Assert.Equal(frozen, await SnapshotAsync(original.DocumentId));
            if (missingCounterpart)
            {
                using var values = JsonDocument.Parse(frozen);
                Assert.DoesNotContain(values.RootElement.EnumerateArray(), value => value.GetProperty("Category").GetString() == "NetPayrollPayable");
                await processor.ProcessAsync(original.DocumentId, original.DocumentType, fixture.BusinessId, default);
                Assert.Null(await service.GetEntryAsync(user, original.DocumentId));
                await AssertPendingAsync(original.DocumentId, "NetPayrollPayable");
                await using var restoreCounterpart = new SqlCommand("""
                    INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                    SELECT * FROM OPENJSON(@Previous) WITH(MappingId uniqueidentifier,TenantId uniqueidentifier,BusinessId uniqueidentifier,
                      Category nvarchar(64),AccountId uniqueidentifier,EffectiveFrom date,EffectiveTo date,CreatedAt datetimeoffset(7))
                    WHERE Category=N'NetPayrollPayable';
                    """, connection);
                restoreCounterpart.Parameters.Add("@Previous", SqlDbType.NVarChar, -1).Value = previous;
                await restoreCounterpart.ExecuteNonQueryAsync();
            }
            if (legacyPending)
                await ClearSnapshotAsync(original.DocumentId);
            else
            {
                // Invalidating the selected auxiliary must not substitute the new
                // mapping. Retry can use the same identity once it is usable again.
                await SetActiveAsync(first.AccountId, false);
                try
                {
                    await processor.ProcessAsync(original.DocumentId, original.DocumentType, fixture.BusinessId, default);
                    Assert.Null(await service.GetEntryAsync(user, original.DocumentId));
                    await AssertPendingAsync(original.DocumentId, category);
                }
                finally { await SetActiveAsync(first.AccountId, true); }
            }
            await processor.ProcessAsync(original.DocumentId, original.DocumentType, fixture.BusinessId, default);
            var entry = await service.GetEntryAsync(user, original.DocumentId);
            Assert.NotNull(entry);
            Assert.Contains(entry.Lines, line => line.AccountId == (legacyPending ? second.AccountId : first.AccountId) && line.Debit == 100m);
            Assert.DoesNotContain(entry.Lines, line => line.AccountId == (legacyPending ? first.AccountId : second.AccountId));
            var completedSnapshot = await SnapshotAsync(original.DocumentId);
            await processor.ProcessAsync(original.DocumentId, original.DocumentType, fixture.BusinessId, default);
            Assert.Equal(completedSnapshot, await SnapshotAsync(original.DocumentId));
            // A completed pre-cutover job has no reconstructible mapping snapshot.
            // Its idempotent replay must leave that history untouched.
            await ClearSnapshotAsync(original.DocumentId);
            await processor.ProcessAsync(original.DocumentId, original.DocumentType, fixture.BusinessId, default);
            Assert.Null(await SnapshotAsync(original.DocumentId));
            var later = Source();
            await AcceptAsync(later);
            var nextSnapshot = await SnapshotAsync(later.DocumentId);
            Assert.NotNull(nextSnapshot);
            using var next = JsonDocument.Parse(nextSnapshot);
            Assert.Equal(second.AccountId, next.RootElement.EnumerateArray().Single(value => value.GetProperty("Category").GetString() == category)
                .GetProperty("AccountId").GetGuid());
        }
        finally
        {
            await using var restore = new SqlCommand("""
                DELETE dbo.AccountingAccountMappings WHERE TenantId=@TenantId AND Category IN(@Category,N'NetPayrollPayable');
                INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,EffectiveTo,CreatedAt)
                SELECT * FROM OPENJSON(@Previous) WITH(MappingId uniqueidentifier,TenantId uniqueidentifier,BusinessId uniqueidentifier,
                  Category nvarchar(64),AccountId uniqueidentifier,EffectiveFrom date,EffectiveTo date,CreatedAt datetimeoffset(7));
                """, connection);
            Bind(restore);
            restore.Parameters.Add("@Previous", SqlDbType.NVarChar, -1).Value = previous;
            await restore.ExecuteNonQueryAsync();
        }
        async Task AssertPendingAsync(Guid id, string missingCategory)
        {
            await using var read = new SqlCommand("SELECT Status,LastErrorCode,LastErrorMessage FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND BusinessId=@BusinessId", connection);
            read.Parameters.AddWithValue("@Id", id);
            read.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await using var row = await read.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            Assert.Equal(AccountingPostingStatuses.PendingConfiguration, row.GetString(0));
            Assert.Equal("AccountMappingMissing", row.GetString(1));
            Assert.Contains(missingCategory, row.GetString(2));
        }
        async Task ClearSnapshotAsync(Guid id)
        {
            await using var clear = new SqlCommand("UPDATE dbo.AccountingPostingJobs SET ResolvedAccountMappingsJson=NULL WHERE SourceDocumentId=@Id AND BusinessId=@BusinessId", connection);
            clear.Parameters.AddWithValue("@Id", id);
            clear.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await clear.ExecuteNonQueryAsync();
        }
        async Task SetActiveAsync(Guid id, bool active)
        {
            await using var change = new SqlCommand("UPDATE dbo.AccountingAccounts SET IsActive=@Active WHERE AccountId=@Id AND TenantId=@TenantId", connection);
            change.Parameters.AddWithValue("@Id", id);
            change.Parameters.AddWithValue("@Active", active);
            change.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await change.ExecuteNonQueryAsync();
        }
        void Bind(SqlCommand command)
        {
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            command.Parameters.AddWithValue("@Category", category);
        }
    }

    [Fact]
    public async Task An_invalid_site_override_or_frozen_account_never_falls_back_or_crosses_scope()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        var shared = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "5194", "Expense", "General", "Debit");
        var site = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "5195", "Expense", "General", "Debit");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var command = new SqlCommand("""
            DELETE dbo.AccountingAccountMappings WHERE TenantId=@Tenant AND Category=N'SalaryExpense';
            INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,CreatedAt)
            VALUES(NEWID(),@Tenant,NULL,N'SalaryExpense',@Shared,'20260101',SYSDATETIMEOFFSET()),
              (NEWID(),@Tenant,@Business,N'SalaryExpense',@Site,'20260101',SYSDATETIMEOFFSET());
            UPDATE dbo.AccountingAccounts SET IsActive=0 WHERE AccountId=@Site AND TenantId=@Tenant;
            SELECT (SELECT AccountId FROM accounting.ConfiguredPostingAccountIdentity(@Tenant,@Business,N'SalaryExpense','20260802')),
              (SELECT COUNT(*) FROM accounting.ConfiguredPostingAccount(@Tenant,@Business,N'SalaryExpense','20260802')),
              (SELECT COUNT(*) FROM accounting.ResolvedPostingAccount(@Tenant,@Business,N'SalaryExpense','20260802',@InvalidSnapshot)),
              (SELECT COUNT(*) FROM accounting.ResolvedPostingAccount(@Tenant,@Business,N'SalaryExpense','20260802',NULL)),
              (SELECT COUNT(*) FROM accounting.ResolvedPostingAccount(@Tenant,@Business,N'SalaryExpense','20260802',N'[]')),
              (SELECT AccountId FROM accounting.ResolvedPostingAccount(@Tenant,@Business,N'SalaryExpense','20260802',@ValidSnapshot)),
              (SELECT COUNT(*) FROM accounting.ResolvedPostingAccount(@OtherTenant,@Business,N'SalaryExpense','20260802',@ValidSnapshot)),
              (SELECT COUNT(*) FROM accounting.ResolvedPostingAccount(@Tenant,@OtherBusiness,N'SalaryExpense','20260802',@ValidSnapshot));
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@OtherTenant", Guid.NewGuid());
        command.Parameters.AddWithValue("@OtherBusiness", Guid.NewGuid());
        command.Parameters.AddWithValue("@Shared", shared.AccountId);
        command.Parameters.AddWithValue("@Site", site.AccountId);
        command.Parameters.AddWithValue("@InvalidSnapshot", JsonSerializer.Serialize(new[] { new { Category = "SalaryExpense", AccountId = site.AccountId } }));
        command.Parameters.AddWithValue("@ValidSnapshot", JsonSerializer.Serialize(new[] { new { Category = "SalaryExpense", AccountId = shared.AccountId } }));
        await using (var rows = await command.ExecuteReaderAsync())
        {
            Assert.True(await rows.ReadAsync());
            Assert.Equal(site.AccountId, rows.GetGuid(0));
            foreach (var index in new[] { 1, 2, 3, 4, 6, 7 }) Assert.Equal(0, rows.GetInt32(index));
            Assert.Equal(shared.AccountId, rows.GetGuid(5));
        }
        await transaction.RollbackAsync();
    }

    [Fact]
    public async Task Accepting_a_native_source_serializes_mapping_edits_and_rolls_back_its_snapshot_with_the_source()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var at = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var source = PayrollSource(at, "SalaryExpense");
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        Assert.Equal(1, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
            [source], at, AccountingJobRequirement.AccountingEntryOnly, default));
        await using var editor = new SqlConnection(fixture.ConnectionString);
        await editor.OpenAsync();
        await using (var change = new SqlCommand("""
            SET LOCK_TIMEOUT 0;
            UPDATE dbo.AccountingAccountMappings SET AccountId=AccountId
            WHERE TenantId=@Tenant AND Category=N'SalaryExpense';
            """, editor))
        {
            change.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            var failure = await Record.ExceptionAsync(async () => { await change.ExecuteNonQueryAsync(); });
            Assert.Equal(1222, Assert.IsType<SqlException>(failure).Number);
        }
        await transaction.RollbackAsync();
        await using var absent = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id)+
              (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id);
            """, connection);
        absent.Parameters.AddWithValue("@Id", source.DocumentId);
        Assert.Equal(0, (int)(await absent.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Performance", "NativeMappingFreeze")]
    public async Task One_hundred_native_sources_freeze_up_to_one_hundred_dates_in_one_command_under_two_seconds(bool distinctDates)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var at = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var samples = new List<double>();
        for (var round = 0; round <= 20; round++)
        {
            var sources = Enumerable.Range(0, 100).Select(index => PayrollSource(
                distinctDates ? at.AddDays(index) : at, "SalaryExpense")).ToArray();
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
            using (var commands = new InventoryBalanceProcessingTests.CommandCounter(connection.Database))
            {
                var timer = Stopwatch.StartNew();
                Assert.Equal(100, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                    sources, at, AccountingJobRequirement.AccountingEntryOnly, default));
                timer.Stop();
                Assert.Equal(1, commands.Count);
                if (round > 0) samples.Add(timer.Elapsed.TotalMilliseconds);
            }
            await using (var verify = new SqlCommand("""
                SELECT COUNT(*),COUNT(DISTINCT HASHBYTES('SHA2_256',job.ResolvedAccountMappingsJson)),
                  MIN(LEN(job.ResolvedAccountMappingsJson))
                FROM OPENJSON(@Ids) WITH(Id uniqueidentifier '$') input
                JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=input.Id AND job.BusinessId=@BusinessId;
                """, connection, transaction))
            {
                verify.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(sources.Select(source => source.DocumentId)));
                verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                await using var rows = await verify.ExecuteReaderAsync();
                Assert.True(await rows.ReadAsync());
                Assert.Equal(100, rows.GetInt32(0));
                Assert.Equal(1, rows.GetInt32(1));
                Assert.True(rows.GetInt64(2) > 2);
            }
            await transaction.RollbackAsync();
        }
        var p95 = samples.Order().ElementAt(18);
        output.WriteLine("Mappings nativos, 100 fuentes/{0} fechas: p95 {1:F1} ms, 1 SQL, 20 muestras.", distinctDates ? 100 : 1, p95);
        Assert.True(p95 < 2000, $"p95 {p95:F1} ms exceeds the 2000 ms acceptance budget.");
    }

    private SqlAccountingPostingJobWriter.Source PayrollSource(DateTimeOffset at, string category)
    {
        var id = Guid.NewGuid();
        var date = DateOnly.FromDateTime(at.Date);
        return new(fixture.TenantId, fixture.BusinessId, id, PayrollAccountingDocumentTypes.Accrual,
            PayrollContractSerializer.Serialize(new PayrollAccountingPayload(fixture.TenantId, fixture.BusinessId,
                id, "Regular", date, date, date, "Configuración fijada al aceptar", [
                    new(category, 100m, 0, fixture.SupplierPartyId, "Devengado"),
                    new("NetPayrollPayable", 0, 100m, fixture.SupplierPartyId, "Contrapartida")])), at, Guid.NewGuid());
    }
}

public sealed partial class ReceivablesVerticalSliceTests
{
    [Fact]
    public async Task Imported_portfolio_keeps_its_accepted_auxiliary_and_unavailable_accounts_leave_no_opening()
    {
        var (customerId, userId, partySiteId) = await ConfigureAsync();
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        using var scope = fixture.CreateScope();
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure });
        await service.EnsureDefaultsAsync(user);
        var at = new DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
        var id = Guid.NewGuid();
        var counterpart = await AccountIdByCodeAsync("413595");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var first = await PortfolioOriginalAccountScenario.RemapAsync(fixture, client, true);
        try
        {
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable))
            {
                var payload = new PreexistingReceivablePayload(fixture.TenantId, fixture.BusinessId, id,
                    customerId, partySiteId, userId, $"MAPPING-{id:N}", at, at.AddDays(30), 100m, counterpart, null);
                Assert.Equal(1, await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                    [new(fixture.TenantId, fixture.BusinessId, id, ReceivablesDocumentTypes.PreexistingReceivable,
                        PreexistingReceivableContractSerializer.Serialize(payload), at, Guid.NewGuid())], at,
                    AccountingJobRequirement.AccountingEntryOnly, default));
                await transaction.CommitAsync();
            }
            var second = await PortfolioOriginalAccountScenario.RemapAsync(fixture, client, true);
            try
            {
                await SetActiveAsync(false);
                try
                {
                    await processor.ProcessAsync(id, ReceivablesDocumentTypes.PreexistingReceivable, fixture.BusinessId, default);
                    await using var pending = new SqlCommand("""
                        SELECT job.Status,job.LastErrorCode,job.LastErrorMessage,
                          (SELECT COUNT(*) FROM dbo.Receivables WHERE SourceDocumentId=@Id AND BusinessId=@Business),
                          (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND BusinessId=@Business)
                        FROM dbo.AccountingPostingJobs job WHERE job.SourceDocumentId=@Id AND job.BusinessId=@Business;
                        """, connection);
                    pending.Parameters.AddWithValue("@Id", id);
                    pending.Parameters.AddWithValue("@Business", fixture.BusinessId);
                    await using var row = await pending.ExecuteReaderAsync();
                    Assert.True(await row.ReadAsync());
                    Assert.Equal(AccountingPostingStatuses.PendingConfiguration, row.GetString(0));
                    Assert.Equal("AccountMappingMissing", row.GetString(1));
                    Assert.Contains("No se registró", row.GetString(2));
                    Assert.Equal(0, row.GetInt32(3));
                    Assert.Equal(0, row.GetInt32(4));
                }
                finally { await SetActiveAsync(true); }
                await processor.ProcessAsync(id, ReceivablesDocumentTypes.PreexistingReceivable, fixture.BusinessId, default);
                var entry = await service.GetEntryAsync(user, id);
                Assert.NotNull(entry);
                Assert.Contains(entry.Lines, line => line.AccountId == first.AccountId && line.Debit == 100m);
                Assert.DoesNotContain(entry.Lines, line => line.AccountId == second.AccountId);
                await processor.ProcessAsync(id, ReceivablesDocumentTypes.PreexistingReceivable, fixture.BusinessId, default);
                Assert.Equal(100m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE SourceDocumentId=@Id", id));
            }
            finally { await PortfolioOriginalAccountScenario.RestoreAsync(fixture, second); }
        }
        finally { await PortfolioOriginalAccountScenario.RestoreAsync(fixture, first); }

        async Task SetActiveAsync(bool active)
        {
            await using var command = new SqlCommand("UPDATE dbo.AccountingAccounts SET IsActive=@Active WHERE TenantId=@Tenant AND AccountId=@Id", connection);
            command.Parameters.AddWithValue("@Active", active);
            command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            command.Parameters.AddWithValue("@Id", first.AccountId);
            await command.ExecuteNonQueryAsync();
        }
    }
}
