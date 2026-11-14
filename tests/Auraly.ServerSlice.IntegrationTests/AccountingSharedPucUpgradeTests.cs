using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class AccountingSharedPucUpgradeTests(ServerSliceFixture fixture)
{
    // Reconstruct the deployed index/FK as well as the old column. SQL Server
    // forbids renaming a column while its filtered index still references it.
    private const string LegacyScopeShapeSql = """
        ALTER TABLE dbo.AccountingAccounts DROP CONSTRAINT FK_AccountingAccounts_LegacyBusiness;
        DROP INDEX IX_AccountingAccounts_Business ON dbo.AccountingAccounts;
        EXEC sys.sp_rename N'dbo.AccountingAccounts.LegacyBusinessId',N'BusinessId',N'COLUMN';
        EXEC sys.sp_executesql N'CREATE INDEX IX_AccountingAccounts_Business ON dbo.AccountingAccounts(TenantId,BusinessId) WHERE BusinessId IS NOT NULL;';
        EXEC sys.sp_executesql N'ALTER TABLE dbo.AccountingAccounts ADD CONSTRAINT FK_AccountingAccounts_Business FOREIGN KEY(BusinessId,TenantId) REFERENCES dbo.Businesses(BusinessId,TenantId);';
        """;

    [Fact]
    public async Task Scope_cut_preflight_blocks_an_ambiguous_historical_bank_without_reassigning_accounts()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var type = new SqlCommand("SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking'", connection);
        var bankId = Guid.NewGuid();
        using (var saved = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId}", new
        {
            bankAccountId = bankId, accountTypeOptionId = (Guid)(await type.ExecuteScalarAsync())!,
            bankName = "Banco de revisión del corte", accountNumber = bankId.ToString("N"),
            displayName = "Banco compartido", isPrimary = false, isActive = true, rowVersion = (string?)null
        })) saved.EnsureSuccessStatusCode();
        var first = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "1119", "Asset", "Bank", "Debit", bankId);
        var second = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "1119", "Asset", "Bank", "Debit", bankId);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using var legacy = new SqlCommand(LegacyScopeShapeSql, connection, transaction);
            await legacy.ExecuteNonQueryAsync();
            await PreflightAsync(connection, transaction); // Already shared ambiguity is unchanged by this cut.
            legacy.CommandText = "UPDATE dbo.AccountingAccounts SET BusinessId=@Business WHERE TenantId=@Tenant AND AccountId=@Account; SELECT @@ROWCOUNT;";
            legacy.Parameters.AddWithValue("@Business", fixture.BusinessId);
            legacy.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            legacy.Parameters.AddWithValue("@Account", first.AccountId);
            Assert.Equal(1, (int)(await legacy.ExecuteScalarAsync())!);
            var issue = await Assert.ThrowsAsync<SqlException>(() => PreflightAsync(connection, transaction));
            Assert.Equal(51768, issue.Number);
            Assert.Contains(bankId.ToString(), issue.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally { await transaction.RollbackAsync(); }
        foreach (var original in new[] { first, second })
        {
            var page = await client.GetFromJsonAsync<AccountingAccountOptionPage>(
                $"/api/commerce/v1/accounting/account-options?page=1&pageSize=1&accountId={original.AccountId}");
            Assert.Equal(original, Assert.Single(page!.Items));
        }
    }

    [Fact]
    public async Task Used_account_scope_upgrade_preserves_journal_identity_and_replay_and_is_repeatable()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "1399", "Asset", "General", "Debit");
        var counterpart = (await service.ListAccountsAsync(user)).First(a => a.AccountType == "Expense" && a.AllowsPosting && !a.RequiresParty);
        var voucher = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
            new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Cuenta histórica compartida",
            [new(account.AccountId, null, null, "Activo histórico", 123.4567m, 0), new(counterpart.AccountId, null, null, "Contrapartida", 0, 123.4567m)]);
        using (var posted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", voucher))
            Assert.True(posted.StatusCode == HttpStatusCode.Accepted, await posted.Content.ReadAsStringAsync());
        var entry = await service.GetEntryAsync(user, voucher.VoucherId);
        Assert.NotNull(entry);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var command = new SqlCommand($"""
            UPDATE dbo.AccountingAccounts SET LegacyBusinessId=@Business WHERE TenantId=@Tenant AND AccountId=@Account;
            {LegacyScopeShapeSql}
            DELETE dbo.__RefactorLog WHERE OperationKey='d0ed2d20-3d89-433d-89c1-7f9033470ca8';
            """, connection);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Account", account.AccountId);
        await command.ExecuteNonQueryAsync();
        // Only the retired scope column is reconstructed. The real posted journal
        // and the new runtime stay intact; deployment must rename without DML.
        var before = await SnapshotAsync("BusinessId");
        await PreflightAsync(connection);
        await PreflightAsync(connection);
        await fixture.ReapplySchemaAsync();
        Assert.Equal(before, await SnapshotAsync("LegacyBusinessId"));
        await PreflightAsync(connection);
        await fixture.ReapplySchemaAsync();
        Assert.Equal(before, await SnapshotAsync("LegacyBusinessId"));
        var current = Assert.Single((await service.AccountOptionsAsync(user, new(1, 1, null, AccountId: account.AccountId))).Items);
        Assert.Null(current.Semantics!.BusinessId);
        Assert.Equal(account.AccountId, current.AccountId);
        Assert.Equal(account.Code, current.Code);
        Assert.Equal(account.AccountType, current.AccountType);
        using (var replay = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers", voucher))
            Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
        Assert.Equal(before, await SnapshotAsync("LegacyBusinessId"));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, account.Code + "99", "No admite sede propia", "Asset", true, false,
                account.Semantics! with { BusinessId = fixture.BusinessId })));

        async Task<string> SnapshotAsync(string scopeColumn)
        {
            command.CommandText = $"""
                SELECT (SELECT AccountId,TenantId,Code,Name,AccountType,AllowsPosting,RequiresParty,IsActive,
                    ParentAccountId,FinancialFunction,BalanceSide,{scopeColumn} HistoricalBusinessId,
                    BankAccountId,PaymentMethodOptionId,ConfigurationLockedAt,RowVersion
                    FROM dbo.AccountingAccounts WHERE TenantId=@Tenant AND AccountId=@Account
                    FOR JSON PATH,INCLUDE_NULL_VALUES) Account,
                  (SELECT entry.* FROM dbo.AccountingEntries entry WHERE entry.TenantId=@Tenant AND entry.SourceDocumentId=@Voucher
                    FOR JSON PATH,INCLUDE_NULL_VALUES) Entries,
                  (SELECT line.* FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
                    WHERE entry.TenantId=@Tenant AND entry.SourceDocumentId=@Voucher ORDER BY line.LineNumber
                    FOR JSON PATH,INCLUDE_NULL_VALUES) Lines
                FOR JSON PATH,INCLUDE_NULL_VALUES;
                """;
            if (!command.Parameters.Contains("@Voucher")) command.Parameters.AddWithValue("@Voucher", voucher.VoucherId);
            return (string)(await command.ExecuteScalarAsync())!;
        }
    }

    private static async Task PreflightAsync(SqlConnection connection, SqlTransaction? transaction = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.NotNull(root);
        var script = await File.ReadAllTextAsync(Path.Combine(root.FullName, "database", "Auraly.Database", "Scripts",
            "Migrations", "20261113_PreflightSharedPucAccounts.sql"));
        foreach (var batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            await using var command = new SqlCommand(batch, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
    }
}
