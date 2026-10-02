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
public sealed class AccountingAccountEditingTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Puc_edit_preserves_identity_rejects_stale_and_foreign_writes_and_returns_the_saved_version()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        using var create = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(Guid.NewGuid(), fixture.TenantId, "5195959801", "Cuenta para editar", "Expense", true, false));
        create.EnsureSuccessStatusCode();
        var initial = (await create.Content.ReadFromJsonAsync<AccountingAccountView>())!;
        Assert.Equal(8, Convert.FromBase64String(initial.RowVersion!).Length);
        var path = $"/api/commerce/v1/accounting/accounts/{initial.AccountId:D}";
        var request = new UpdateAccountingAccountRequest("  Cuenta editada  ", true, initial.RowVersion!);
        using var write = await client.PutAsJsonAsync(path, request);
        write.EnsureSuccessStatusCode();
        var saved = (await write.Content.ReadFromJsonAsync<AccountingAccountView>())!;
        Assert.Equal("Cuenta editada", saved.Name);
        Assert.True(saved.RequiresParty);
        Assert.Equal(initial with { Name = saved.Name, RequiresParty = true, RowVersion = saved.RowVersion }, saved);
        Assert.NotEqual(initial.RowVersion, saved.RowVersion);
        using var replay = await client.PutAsJsonAsync(path, request);
        replay.EnsureSuccessStatusCode();
        Assert.Equal(saved, await replay.Content.ReadFromJsonAsync<AccountingAccountView>());
        using var stale = await client.PutAsJsonAsync(path, request with { Name = "No debe sobrescribir" });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var reader = fixture.CreateAdminClient(AccountingPermissionCodes.Read);
        using var denied = await reader.PutAsJsonAsync(path, request with { RowVersion = saved.RowVersion! });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var invalid = await client.PutAsJsonAsync(path, request with { Name = " ", RowVersion = saved.RowVersion! });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var invalidVersion = await client.PutAsJsonAsync(path, request with { RowVersion = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalidVersion.StatusCode);

        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var identity = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read });
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.UpdateAccountAsync(
            identity with { TenantId = Guid.NewGuid() }, saved.AccountId, request with { RowVersion = saved.RowVersion! }));

        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(database))
        {
            var timer = Stopwatch.StartNew();
            saved = await service.UpdateAccountAsync(identity, saved.AccountId,
                new("Nombre definitivo", false, saved.RowVersion!));
            timer.Stop();
            Assert.Equal(1, counter.Count);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Edit took {timer.Elapsed.TotalMilliseconds}ms");
            output.WriteLine($"PUC edit: {timer.Elapsed.TotalMilliseconds:F1}ms, {counter.Count} SQL command.");
        }
        var page = await client.GetFromJsonAsync<AccountingAccountOptionPage>(
            "/api/commerce/v1/accounting/accounts?page=1&pageSize=1&search=Nombre%20definitivo");
        Assert.Equal(saved, Assert.Single(page!.Items));
        Assert.Equal(1, page.TotalCount);
        var empty = await client.GetFromJsonAsync<AccountingAccountOptionPage>(
            "/api/commerce/v1/accounting/accounts?page=2&pageSize=1&search=Nombre%20definitivo");
        Assert.Empty(empty!.Items);
        Assert.Equal(1, empty.TotalCount);
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(database))
        {
            var result = await service.AccountOptionsAsync(identity, new(1, 25, null, IncludeStructural: true, IncludeInactive: true));
            Assert.InRange(result.Items.Count, 1, 25);
            Assert.Equal(1, counter.Count);
        }
    }

    [Fact]
    public async Task Puc_edit_keeps_the_active_bank_invariant_and_does_not_partially_rename()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        using var create = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(Guid.NewGuid(), fixture.TenantId, "1110059801", "Banco sin tercero", "Asset", true, false));
        create.EnsureSuccessStatusCode();
        var account = (await create.Content.ReadFromJsonAsync<AccountingAccountView>())!;
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT TOP(1) OptionId FROM reference.Options WHERE CatalogCode=N'bank-account-type' AND IsActive=1 ORDER BY SortOrder", connection);
        var type = (Guid)(await command.ExecuteScalarAsync())!;
        var bankId = Guid.NewGuid();
        using var bank = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/bank-accounts/{bankId:D}",
            new SaveBankAccountRequest(bankId, account.AccountId, type, "Banco prueba", bankId.ToString("N"), "Banco prueba", false, true, null));
        bank.EnsureSuccessStatusCode();
        using var invalid = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/accounts/{account.AccountId:D}",
            new UpdateAccountingAccountRequest("No debe guardar", true, account.RowVersion!));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var page = await client.GetFromJsonAsync<AccountingAccountOptionPage>(
            $"/api/commerce/v1/accounting/accounts?page=1&pageSize=25&search={account.Code}");
        Assert.Equal(account, Assert.Single(page!.Items));
        using var valid = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/accounts/{account.AccountId:D}",
            new UpdateAccountingAccountRequest("Nombre del banco corregido", false, account.RowVersion!));
        valid.EnsureSuccessStatusCode();
    }
}
