using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Auraly.Commerce.Accounting.Contracts;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Fact]
    public async Task Bank_master_has_no_required_single_auxiliary_and_reads_its_links_from_the_PUC()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        var bankId = Guid.NewGuid();
        var typeId = await ScalarAsync<Guid>("""
            SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking';
            """, Guid.Empty);
        using var created = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId:D}", new
        {
            bankAccountId = bankId, accountTypeOptionId = typeId, bankName = "Banco configurable",
            accountNumber = bankId.ToString("N"), displayName = "Banco sin auxiliar obligatorio",
            isPrimary = false, isActive = true, rowVersion = (string?)null
        });
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        using (var initial = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, initial.RootElement.GetProperty("auxiliaryCount").GetInt32());
            AssertNoSingleAccount(initial.RootElement);
        }
        var groupId = Guid.NewGuid();
        using (var group = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(groupId, fixture.TenantId, "111993", "Vínculos bancarios desde el PUC", "Asset", false, false)))
            group.EnsureSuccessStatusCode();
        var auxiliaries = new List<AccountingAccountView>();
        for (var index = 1; index <= 2; index++)
        {
            using var account = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
                new CreateAccountingAccountRequest(Guid.NewGuid(), fixture.TenantId, $"1119930{index}",
                    $"Auxiliar bancario {index}", "Asset", true, false,
                    new(groupId, "Bank", "Debit", index == 1 ? null : fixture.BusinessId, bankId, null)));
            account.EnsureSuccessStatusCode();
            auxiliaries.Add((await account.Content.ReadFromJsonAsync<AccountingAccountView>())!);
        }
        using var listed = await client.GetAsync("/api/commerce/v1/accounting/bank-accounts?includeInactive=true");
        listed.EnsureSuccessStatusCode();
        using var result = System.Text.Json.JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        var bank = Assert.Single(result.RootElement.EnumerateArray().Where(value => value.GetProperty("bankAccountId").GetGuid() == bankId));
        Assert.Equal(2, bank.GetProperty("auxiliaryCount").GetInt32());
        AssertNoSingleAccount(bank);
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.BankAccounts WHERE BankAccountId=@Id AND AccountingAccountId IS NULL", bankId));
        // An unused auxiliary can leave treasury and require a party in the
        // same PUC edit. The old bank relation must not block its new function.
        using (var changed = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/accounts/{auxiliaries[0].AccountId}",
            new UpdateAccountingAccountRequest(auxiliaries[0].Name, true, auxiliaries[0].RowVersion!,
                auxiliaries[0].Semantics! with { FinancialFunction = "General", BankAccountId = null })))
            Assert.True(changed.IsSuccessStatusCode, await changed.Content.ReadAsStringAsync());
        var after = await client.GetFromJsonAsync<BankAccountView[]>("/api/commerce/v1/accounting/bank-accounts?includeInactive=true");
        var remaining = Assert.Single(after!, value => value.BankAccountId == bankId);
        Assert.Equal(1, remaining.AuxiliaryCount);
        Assert.Equal(auxiliaries[1].AccountId, remaining.AccountingAccountId);

        static void AssertNoSingleAccount(System.Text.Json.JsonElement bank)
        {
            if (bank.TryGetProperty("accountingAccountId", out var account))
                Assert.Equal(System.Text.Json.JsonValueKind.Null, account.ValueKind);
        }
    }

    [Fact]
    public async Task One_real_bank_reconciles_the_auxiliaries_of_three_businesses_once()
    {
        await fixture.GrantReconciliationRolePermissionsAsync();
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure, AccountingPermissionCodes.BankReconciliationRead,
            AccountingPermissionCodes.BankReconciliationManage, AccountingPermissionCodes.BankReconciliationClose);
        var groupId = Guid.NewGuid();
        using (var group = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(groupId, fixture.TenantId, "111994", "Auxiliares de un banco real", "Asset", false, false)))
            group.EnsureSuccessStatusCode();
        var firstAccountId = Guid.NewGuid();
        using (var account = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(firstAccountId, fixture.TenantId, "11199401", "Banco sede principal",
                "Asset", true, false, new(groupId, "General", "Debit", null, null, null))))
            account.EnsureSuccessStatusCode();
        var typeId = await ScalarAsync<Guid>("""
            SELECT OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND Code=N'Checking';
            """, Guid.Empty);
        var bankId = Guid.NewGuid();
        using (var bank = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId:D}",
            new SaveBankAccountRequest(bankId, firstAccountId, typeId, "Banco único", bankId.ToString("N"),
                "Una cuenta bancaria, tres sedes", false, true, null)))
            bank.EnsureSuccessStatusCode();
        var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var counterpartId = accounts.First(account => account.AccountType == "Expense" && account.AllowsPosting && !account.RequiresParty).AccountId;
        var businesses = new[] { fixture.BusinessId, Guid.NewGuid(), Guid.NewGuid() };
        var auxiliaries = new[] { firstAccountId, Guid.NewGuid(), Guid.NewGuid() };
        for (var index = 1; index < businesses.Length; index++)
        {
            await CreateBusinessWithUserAccessAsync(businesses[index], fixture.UserId);
            using var account = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
                new CreateAccountingAccountRequest(auxiliaries[index], fixture.TenantId, $"1119940{index + 1}",
                    $"Banco sede {index + 1}", "Asset", true, false,
                    new(groupId, "Bank", "Debit", businesses[index], bankId, null)));
            account.EnsureSuccessStatusCode();
        }
        var configurationNoticesBefore = await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages notice
            JOIN dbo.Businesses business ON business.BusinessId=notice.BusinessId
            WHERE business.TenantId=@Id AND notice.Stream=N'Configuration';
            """,fixture.TenantId);
        var expectedNotices=await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Businesses WHERE TenantId=@Id;",fixture.TenantId);
        Assert.True(expectedNotices>=3);
        var selectedBank=(await client.GetFromJsonAsync<BankAccountView[]>("/api/commerce/v1/accounting/bank-accounts"))!
            .Single(bank=>bank.BankAccountId==bankId);
        using(var notices=new InventoryBalanceProcessingTests.CommandCounter(
            new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter:command=>command.CommandText.Contains("INSERT dbo.PosSynchronizationOutboxMessages",StringComparison.Ordinal)))
        {
            using var renamed=await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId:D}",
                new SaveBankAccountRequest(bankId,null,selectedBank.AccountTypeOptionId,selectedBank.BankName,
                    selectedBank.AccountNumber,"Banco de tres sedes actualizado",selectedBank.IsPrimary,
                    selectedBank.IsActive,selectedBank.RowVersion));
            Assert.True(renamed.IsSuccessStatusCode,await renamed.Content.ReadAsStringAsync());
            Assert.Equal(1,notices.Count);
        }
        var configurationNoticesAfter = await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages notice
            JOIN dbo.Businesses business ON business.BusinessId=notice.BusinessId
            WHERE business.TenantId=@Id AND notice.Stream=N'Configuration';
            """,fixture.TenantId);
        Assert.Equal(expectedNotices,configurationNoticesAfter-configurationNoticesBefore);
        var date = new DateTimeOffset(2026, 9, 15, 9, 0, 0, TimeSpan.FromHours(-5));
        var entries = new List<Guid>();
        for (var index = 0; index < businesses.Length; index++)
            entries.Add(await InsertPostedBankEntryAsync(businesses[index], auxiliaries[index], counterpartId,
                date, (index + 1) * 100m));
        var csv = Encoding.UTF8.GetBytes("fecha;descripcion;referencia;valor;saldo\n2026-09-15;Sede 1;S1;100;100\n2026-09-15;Sede 2;S2;200;300\n2026-09-15;Sede 3;S3;300;600\n");
        var id = Guid.NewGuid();
        var request = new ImportBankReconciliationRequest(id, bankId, new(2026, 9, 1), new(2026, 9, 30), 0m, 600m,
            "tres-sedes.csv", Convert.ToHexString(SHA256.HashData(csv)), Convert.ToBase64String(csv),
            Enumerable.Range(1, 3).Select(index => new BankStatementLineImport(index, new(2026, 9, 15),
                $"Sede {index}", $"S{index}", index * 100m, index * (index + 1) * 50m)).ToArray());
        await using (var permissionConnection = new Microsoft.Data.SqlClient.SqlConnection(fixture.ConnectionString))
        {
            await permissionConnection.OpenAsync();
            await using var permission = new Microsoft.Data.SqlClient.SqlCommand("""
                SELECT binding.RolePermissionId,binding.PermissionId,binding.AssignedAt FROM dbo.RolePermissions binding
                JOIN dbo.Permissions permission ON permission.PermissionId=binding.PermissionId
                WHERE binding.RoleId=@RoleId AND permission.Resource=N'accounting.bank-reconciliation.read';
                """, permissionConnection);
            permission.Parameters.AddWithValue("@RoleId", fixture.RoleId);
            Guid bindingId,permissionId;
            DateTime assignedAt;
            await using (var reader = await permission.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                bindingId=reader.GetGuid(0);permissionId=reader.GetGuid(1);assignedAt=reader.GetDateTime(2);
            }
            permission.Parameters.AddWithValue("@BindingId",bindingId);
            permission.CommandText="DELETE dbo.RolePermissions WHERE RolePermissionId=@BindingId";
            Assert.Equal(1,await permission.ExecuteNonQueryAsync());
            try
            {
                // Management alone cannot return the consolidated readable
                // state. A rejected response must not leave an imported file.
                var deniedId=Guid.NewGuid();
                using var denied=await client.PostAsJsonAsync("/api/commerce/v1/accounting/bank-reconciliations",
                    request with { ReconciliationId=deniedId });
                Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.BankReconciliations WHERE ReconciliationId=@Id",deniedId));
                Assert.Equal(0,await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.BankStatementLines WHERE ReconciliationId=@Id",deniedId));
            }
            finally
            {
                permission.CommandText="INSERT dbo.RolePermissions(RolePermissionId,RoleId,PermissionId,AssignedAt) VALUES(@BindingId,@RoleId,@PermissionId,@AssignedAt)";
                permission.Parameters.AddWithValue("@PermissionId",permissionId);
                permission.Parameters.AddWithValue("@AssignedAt",assignedAt);
                Assert.Equal(1,await permission.ExecuteNonQueryAsync());
            }
        }
        using var counter = new InventoryBalanceProcessingTests.CommandCounter(
            new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("INSERT accounting.BankStatementLines", StringComparison.Ordinal));
        using var imported = await client.PostAsJsonAsync("/api/commerce/v1/accounting/bank-reconciliations",
            request);
        Assert.True(imported.StatusCode == HttpStatusCode.Created, await imported.Content.ReadAsStringAsync());
        var current = (await imported.Content.ReadFromJsonAsync<BankReconciliationDetailView>())!;
        Assert.Equal(3, current.BookLines.Count);
        Assert.Equal(businesses.Order(), current.BookLines.Select(line => line.BusinessId).Order());
        Assert.Equal(entries.Order(), current.BookLines.Select(line => line.EntryId).Order());
        Assert.Equal(600m, current.Summary.BookMovementTotal);
        Assert.Equal(0m, current.Summary.Difference);
        Assert.Equal(1, counter.Count);
        var paged = await client.GetFromJsonAsync<BankReconciliationDetailView>(
            $"/api/commerce/v1/accounting/bank-reconciliations/{id:D}?pageSize=1&statementPage=2&bookPage=2");
        Assert.NotNull(paged);
        Assert.Single(paged.StatementLines);
        Assert.Single(paged.BookLines);
        Assert.Equal(3, paged.Paging.StatementCount);
        Assert.Equal(3, paged.Paging.BookCount);
        Assert.Equal(3, paged.Paging.UnmatchedBookCount);
        Assert.Equal(2, paged.StatementLines.Single().LineNumber);
        Assert.Equal(600m, paged.Summary.StatementMovementTotal);
        Assert.Equal(600m, paged.Summary.BookMovementTotal);
        var list = await client.GetFromJsonAsync<BankReconciliationPage>(
            $"/api/commerce/v1/accounting/bank-reconciliations?bankAccountId={bankId:D}&pageSize=1");
        Assert.NotNull(list);
        Assert.Equal(id, Assert.Single(list.Items).ReconciliationId);
        Assert.Equal(1, list.TotalCount);
        foreach (var statement in current.StatementLines.OrderBy(line => line.LineNumber).ToArray())
        {
            var book = current.BookLines.Single(line => line.Amount == statement.Amount);
            using var match = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/bank-reconciliations/{id:D}/allocations",
                new CreateBankReconciliationAllocationRequest(Guid.NewGuid(), statement.StatementLineId,
                    book.EntryId, book.EntryLineNumber, statement.Amount, current.Summary.RowVersion));
            Assert.True(match.IsSuccessStatusCode, await match.Content.ReadAsStringAsync());
            current = (await match.Content.ReadFromJsonAsync<BankReconciliationDetailView>())!;
        }
        using var closed = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/bank-reconciliations/{id:D}/close",
            new ChangeBankReconciliationStatusRequest(current.Summary.RowVersion));
        Assert.True(closed.IsSuccessStatusCode, await closed.Content.ReadAsStringAsync());
        var result = (await closed.Content.ReadFromJsonAsync<BankReconciliationDetailView>())!;
        Assert.Equal(BankReconciliationStatuses.Closed, result.Summary.Status);
        Assert.Equal(600m, result.Summary.BookMovementTotal);
        Assert.Equal(3, result.Allocations.Count);
    }
}
