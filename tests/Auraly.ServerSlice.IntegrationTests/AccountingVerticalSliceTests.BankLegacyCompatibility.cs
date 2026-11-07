using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_legacy_bank_history_is_preserved_and_only_proven_origins_can_be_reconciled(bool hasUnprovenHistory)
    {
        await fixture.GrantReconciliationRolePermissionsAsync();
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate,
            AccountingPermissionCodes.BankReconciliationRead, AccountingPermissionCodes.BankReconciliationManage);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null))
            defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared")))
            activate.EnsureSuccessStatusCode();
        var accountId = Guid.NewGuid();
        var code = "1119" + RandomNumberGenerator.GetInt32(10_000_000, 100_000_000);
        using (var created = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(accountId, fixture.TenantId, code, "Auxiliar histórico compartido", "Asset", true, false)))
            created.EnsureSuccessStatusCode();
        var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var counterpart = accounts.First(value => value.AccountType == "Expense" && value.AllowsPosting && !value.RequiresParty).AccountId;
        var typeId = await ScalarAsync<Guid>("SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking';", Guid.Empty);
        var banks = new[] { Guid.NewGuid(), Guid.NewGuid() };
        foreach (var bank in banks)
        {
            using var created = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bank:D}",
                new SaveBankAccountRequest(bank, null, typeId, "Banco histórico", bank.ToString("N"), bank.ToString("N"), false, true, null));
            created.EnsureSuccessStatusCode();
        }
        var date = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var entries = new[] {
            await InsertPostedBankEntryAsync(fixture.BusinessId, accountId, counterpart, date, 100m),
            await InsertPostedBankEntryAsync(fixture.BusinessId, accountId, counterpart, date, 200m),
        };
        if (hasUnprovenHistory)
            await InsertPostedBankEntryAsync(fixture.BusinessId, accountId, counterpart, date, 50m);

        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var legacy = new SqlCommand("""
            UPDATE accounting.BankAccounts SET AccountingAccountId=@Account WHERE TenantId=@Tenant AND BankAccountId IN(@Bank1,@Bank2);
            UPDATE source SET PayloadJson=CONCAT(N'{"BankAccountId":"',CONVERT(nvarchar(36),
              CASE entry.EntryId WHEN @Entry1 THEN @Bank1 ELSE @Bank2 END),N'"}')
            FROM dbo.AccountingSourceDocuments source JOIN dbo.AccountingEntries entry
              ON entry.SourceDocumentId=source.SourceDocumentId AND entry.SourceDocumentType=source.SourceDocumentType
            WHERE entry.EntryId IN(@Entry1,@Entry2);
            """, connection))
        {
            legacy.Parameters.AddWithValue("@Account", accountId);
            legacy.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            legacy.Parameters.AddWithValue("@Bank1", banks[0]); legacy.Parameters.AddWithValue("@Bank2", banks[1]);
            legacy.Parameters.AddWithValue("@Entry1", entries[0]); legacy.Parameters.AddWithValue("@Entry2", entries[1]);
            await legacy.ExecuteNonQueryAsync();
        }
        async Task<string> JournalAsync()
        {
            await using var command = new SqlCommand("""
                SELECT CONVERT(varchar(64),HASHBYTES('SHA2_256',(
                SELECT entry.DebitTotal,entry.CreditTotal,line.* FROM dbo.AccountingEntries entry
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE EXISTS(SELECT 1 FROM dbo.AccountingEntryLines own WHERE own.EntryId=entry.EntryId AND own.AccountId=@Id)
                ORDER BY entry.EntryId,line.LineNumber FOR JSON PATH)),2);
                """, connection);
            command.Parameters.AddWithValue("@Id", accountId);
            return (string)(await command.ExecuteScalarAsync())!;
        }
        var before = await JournalAsync();
        await fixture.ApplyMigrationAsync("20261023_BankAuxiliariesAndAcceptedAccounts.sql", connection);
        await fixture.ApplyMigrationAsync("20261023_BankAuxiliariesAndAcceptedAccounts.sql", connection);
        Assert.Equal(before, await JournalAsync());
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingAccounts WHERE AccountId=@Id AND IsActive=0 AND BankAccountId IS NULL AND ConfigurationLockedAt IS NOT NULL;", accountId));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.BankAccounts WHERE HistoricalAccountingAccountId=@Id AND AccountingAccountId IS NULL;", accountId));
        var future = new List<Guid>();
        for (var index = 0; index < banks.Length; index++)
        {
            await using var command = new SqlCommand("""
                SELECT AccountId FROM accounting.BankPostingAccount(@Tenant,@Business,@Bank);
                SELECT book.EntryId,book.HasBankAttribution FROM accounting.BankBookEntries(@Tenant,@Bank) book;
                SELECT COUNT(*) FROM accounting.BankBookEntries(NEWID(),@Bank);
                """, connection);
            command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            command.Parameters.AddWithValue("@Business", fixture.BusinessId);
            command.Parameters.AddWithValue("@Bank", banks[index]);
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync()); future.Add(reader.GetGuid(0));
            Assert.NotEqual(accountId, future[^1]); Assert.False(await reader.ReadAsync());
            Assert.True(await reader.NextResultAsync());
            var proven = new List<Guid>(); var unknown = 0;
            while (await reader.ReadAsync())
                if (reader.GetBoolean(1)) proven.Add(reader.GetGuid(0)); else unknown++;
            Assert.Equal(entries[index], Assert.Single(proven));
            Assert.Equal(hasUnprovenHistory ? 1 : 0, unknown);
            Assert.True(await reader.NextResultAsync()); Assert.True(await reader.ReadAsync()); Assert.Equal(0, reader.GetInt32(0));
        }
        Assert.Equal(2, future.Distinct().Count());
        for (var index = 0; index < banks.Length; index++)
        {
            var amount = (index + 1) * 100m;
            var id = Guid.NewGuid(); var file = Encoding.UTF8.GetBytes(id.ToString());
            using var imported = await client.PostAsJsonAsync("/api/commerce/v1/accounting/bank-reconciliations",
                new ImportBankReconciliationRequest(id, banks[index], new(2026, 9, 1), new(2026, 9, 30), 0m, amount,
                    "historial.csv", Convert.ToHexString(SHA256.HashData(file)), Convert.ToBase64String(file),
                    [new(1, new(2026, 9, 5), "Histórico probado", null, amount, amount)]));
            if (hasUnprovenHistory)
            {
                Assert.Equal(HttpStatusCode.Conflict, imported.StatusCode);
                Assert.Contains("sin banco demostrable", await imported.Content.ReadAsStringAsync());
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.BankReconciliations WHERE ReconciliationId=@Id;", id));
            }
            else
            {
                Assert.True(imported.StatusCode == HttpStatusCode.Created, await imported.Content.ReadAsStringAsync());
                var result = (await imported.Content.ReadFromJsonAsync<BankReconciliationDetailView>())!;
                Assert.Equal(entries[index], Assert.Single(result.BookLines).EntryId);
                Assert.Equal(amount, result.Summary.BookMovementTotal);
            }
        }
    }
}
