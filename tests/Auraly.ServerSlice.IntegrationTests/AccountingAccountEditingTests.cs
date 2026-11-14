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
    [Theory]
    [InlineData("InputVat", "Liability", "Payable", "General", "Debit")]
    [InlineData("InputVat", "Liability", "CustomerAdvance", "General", "Debit")]
    [InlineData("OutputVat", "Liability", "Payable", "General", "Credit")]
    [InlineData("Inventory", "Asset", "Receivable", "General", "Debit")]
    [InlineData("CustomerCreditsPayable", "Liability", "General", "CustomerAdvance", "Credit")]
    [InlineData("SupplierCreditsReceivable", "Asset", "General", "SupplierAdvance", "Debit")]
    [InlineData("NetPayrollPayable", "Liability", "General", "Payable", "Credit")]
    [InlineData("EmployeeLoansReceivable", "Asset", "General", "Receivable", "Debit")]
    [InlineData("EmployeeShortagesReceivable", "Asset", "General", "Receivable", "Debit")]
    [InlineData("WithholdingIncomeTaxPayable", "Liability", "CustomerAdvance", "General", "Credit")]
    [InlineData("TransferClearing", "Asset", "Cash", "General", "Debit")]
    [InlineData("CashClosureDifferencesPending", "Asset", "Receivable", "General", "Debit")]
    public async Task Posting_readiness_rejects_a_function_that_contradicts_the_native_purpose(
        string category, string type, string function, string expectedFunction, string side)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            type == "Asset" ? "1398" : "2298", type, function,
            type == "Asset" ? "Debit" : "Credit");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM accounting.PostingAccountReadiness(@Tenant,@Category,@Account)", connection);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Category", category);
        command.Parameters.AddWithValue("@Account", account.AccountId);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        using var rejected = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
            new SetAccountMappingRequest(fixture.TenantId, fixture.BusinessId, category,
                account.AccountId, new(2026,1,1), null));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var coherent = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            type == "Asset" ? "1397" : "2497", type, expectedFunction, side);
        command.Parameters["@Account"].Value = coherent.AccountId;
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData("Cash")]
    [InlineData("Bank")]
    [InlineData("DebitCardClearing")]
    [InlineData("CreditCardClearing")]
    [InlineData("CardClearing")]
    public async Task Posting_readiness_rejects_an_explicit_portfolio_function_for_a_treasury_category(string category)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        using var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null);
        Assert.True(defaults.IsSuccessStatusCode, await defaults.Content.ReadAsStringAsync());
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "1398", "Asset", "Receivable", "Debit");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT COUNT(*) FROM accounting.PostingAccountReadiness(@Tenant,@Category,@Account)", connection);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Category", category);
        command.Parameters.AddWithValue("@Account", account.AccountId);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        using var rejected = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
            new SetAccountMappingRequest(fixture.TenantId, fixture.BusinessId, category, account.AccountId,
                new(2026, 1, 1), null));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Posting_readiness_rejects_an_explicit_general_function_for_a_control_category(bool payable)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        using var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null);
        Assert.True(defaults.IsSuccessStatusCode, await defaults.Content.ReadAsStringAsync());
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            payable ? "2298" : "1398", payable ? "Liability" : "Asset", "General", payable ? "Credit" : "Debit");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT COUNT(*) FROM accounting.PostingAccountReadiness(@Tenant,@Category,@Account);
            """, connection);
        command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        command.Parameters.AddWithValue("@Business", fixture.BusinessId);
        command.Parameters.AddWithValue("@Category", payable ? AccountingCategories.AccountsPayable : AccountingCategories.AccountsReceivable);
        command.Parameters.AddWithValue("@Account", account.AccountId);
        Assert.Equal(0, (int)(await command.ExecuteScalarAsync())!);
        using var rejected = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
            new SetAccountMappingRequest(fixture.TenantId, fixture.BusinessId,
                payable ? AccountingCategories.AccountsPayable : AccountingCategories.AccountsReceivable,
                account.AccountId, new(2026, 1, 1), null));
        Assert.False(rejected.IsSuccessStatusCode);
        // An explicit and coherent function remains usable. No account is
        // rewritten to manufacture compatibility with the selected category.
        var coherent = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            payable ? "2297" : "1397", payable ? "Liability" : "Asset", payable ? "Payable" : "Receivable",
            payable ? "Credit" : "Debit");
        command.Parameters["@Account"].Value = coherent.AccountId;
        Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
    }

    [Theory]
    [InlineData("6", false)]
    [InlineData("6", true)]
    [InlineData("7", false)]
    [InlineData("7", true)]
    public async Task Explicit_cost_accounts_share_posting_and_reports_without_reclassifying_legacy_parents(string pucClass, bool legacyParent)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var parent = await service.CreateAccountAsync(user, new(Guid.NewGuid(), fixture.TenantId,
            pucClass + (legacyParent ? "99891" : "99892"), "Agrupación de costos", legacyParent ? "Expense" : "Cost", false, false));
        var cost = await service.CreateAccountAsync(user, new(Guid.NewGuid(), fixture.TenantId,
            parent.Code + "01", "Costo operacional", "Cost", true, false,
            new(parent.AccountId, "General", "Debit", null, null, null)));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "519898", "Clase incompatible", "Cost", true, false)));
        Assert.Contains((await service.AccountOptionsAsync(user, new(1, 25, cost.Code, ExpenseOnly: true), default)).Items,
            account => account.AccountId == cost.AccountId);
        var date = new DateOnly(2026, 10, 21);
        var balanceBefore = await service.GetBalanceSheetAsync(user, date);
        var cash = (await service.ListAccountsAsync(user)).Single(account => account.Code == "110505");
        var id = Guid.NewGuid();
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId,
                new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Costo clasificado", [
                    new(cost.AccountId, null, null, "Costo", 100, 0), new(cash.AccountId, null, null, "Pago", 0, 100)]));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Contains(await service.GetIncomeStatementAsync(user, date, date), row =>
            row.Section == "Cost" && row.AccountCode == cost.Code && row.Amount == 100);
        var balanceAfter = await service.GetBalanceSheetAsync(user, date);
        Assert.Equal(-100m, balanceAfter.Where(row => row.AccountCode == "RESULTADO").Sum(row => row.Amount)
            - balanceBefore.Where(row => row.AccountCode == "RESULTADO").Sum(row => row.Amount));
        Assert.Equal(parent.AccountType, (await service.ListAccountsAsync(user)).Single(account => account.AccountId == parent.AccountId).AccountType);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var ready = new SqlCommand("""
            DECLARE @Entry uniqueidentifier=(SELECT EntryId FROM dbo.AccountingEntries
              WHERE TenantId=@Tenant AND BusinessId=@Business AND SourceDocumentId=@Source
                AND SourceDocumentType=N'ManualAccountingVoucher');
            SELECT (SELECT COUNT(*) FROM accounting.PostingAccountReadiness(@Tenant,N'CostOfGoodsSold',@Account)),
              (SELECT COUNT(*) FROM accounting.PostingAccountReadiness(@Tenant,N'Inventory',@Account)),
              (SELECT SUM(EligibleFunctionalAmount) FROM purchasing.ReceiptCostVoucherLines(@Entry) WHERE AccountId=@Account);
            """, connection);
        ready.Parameters.AddWithValue("@Tenant", fixture.TenantId); ready.Parameters.AddWithValue("@Business", fixture.BusinessId);
        ready.Parameters.AddWithValue("@Account", cost.AccountId);
        ready.Parameters.AddWithValue("@Source", id);
        await using var reader = await ready.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(1, reader.GetInt32(0)); Assert.Equal(0, reader.GetInt32(1));
        Assert.Equal(100m, reader.GetDecimal(2));
    }

    [Fact]
    public async Task Automatic_inventory_mapping_rejects_a_liability_without_replacing_its_valid_configuration()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null))
            Assert.True(defaults.IsSuccessStatusCode, await defaults.Content.ReadAsStringAsync());
        var mappings = (await client.GetFromJsonAsync<AccountingMappingView[]>("/api/commerce/v1/accounting/account-mappings"))!;
        var original = mappings.Where(item => item.Category == AccountingCategories.Inventory && item.BusinessId is null)
            .OrderByDescending(item => item.EffectiveFrom).First();
        var liability = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "2995", "Liability", "General", "Credit");
        var valid = new SetAccountMappingRequest(fixture.TenantId, original.BusinessId, original.Category,
            original.AccountId, original.EffectiveFrom, original.EffectiveTo);
        try
        {
            using var response = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings", valid with { AccountId = liability.AccountId });
            Assert.False(response.IsSuccessStatusCode, "Inventory must not be mapped to a liability account.");
            var after = (await client.GetFromJsonAsync<AccountingMappingView[]>("/api/commerce/v1/accounting/account-mappings"))!;
            Assert.Equal(original.AccountId, after.Single(item => item.Category == original.Category &&
                item.BusinessId == original.BusinessId && item.EffectiveFrom == original.EffectiveFrom).AccountId);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var resolved = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM accounting.PostingAccountReadiness(@Tenant,N'Inventory',@Invalid)),
                  (SELECT COUNT(*) FROM accounting.PostingAccountReadiness(@Tenant,N'Inventory',@Valid));
                """, connection);
            resolved.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            resolved.Parameters.AddWithValue("@Business", fixture.BusinessId);
            resolved.Parameters.AddWithValue("@Date", new DateTime(2026, 10, 25));
            resolved.Parameters.AddWithValue("@Invalid", liability.AccountId);
            resolved.Parameters.AddWithValue("@Valid", original.AccountId);
            await using var reader = await resolved.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
        }
        finally
        {
            using var restored = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings", valid);
            Assert.True(restored.IsSuccessStatusCode, await restored.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public Task Trial_balance_rolls_up_each_explicit_auxiliary_parent_once_and_preserves_legacy_levels() =>
        AssertFreeHierarchyAsync(false);

    [Fact]
    [Trait("Performance", "AccountingTrialBalance")]
    [Trait("Performance", "AccountingAuxiliary")]
    public Task Trial_balance_remains_scoped_with_one_hundred_thousand_unrelated_movements() =>
        AssertFreeHierarchyAsync(true);

    private async Task AssertFreeHierarchyAsync(bool withUnrelatedMovements)
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var code = withUnrelatedMovements ? "830597" : "830596";
        var root = await CreateAsync(code, "Control con auxiliares libres", false);
        var site = await CreateAsync(code + "0", "Agrupación propia de siete dígitos", false, root);
        var concept = await CreateAsync(code + "01", "Agrupación propia de ocho dígitos", false, site);
        // Sharing a prefix does not make this unused grouping an ancestor.
        var unrelated = await CreateAsync(code + "010", "Agrupación no seleccionada", false, concept);
        var first = await CreateAsync(code + "0101", "Primer auxiliar", true, concept);
        var second = await CreateAsync(code + "01021234567890123456789012", "Auxiliar de treinta y dos dígitos", true, concept);
        var counterpart = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, withUnrelatedMovements ? "999997" : "999996", "Control acreedor del informe", "OrderCredit", true, false));
        var before = (await service.GetTrialBalanceAsync(user, new(2026, 10, 21), new(2026, 10, 21)))
            .ToDictionary(row => row.AccountCode);
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        await PostAsync(new(2026, 10, 1), [(first, 100m)]);
        await PostAsync(new(2026, 10, 21), [(first, 25m), (second, 75m)]);
        await PostAsync(new(2026, 11, 1), [(first, 999m)]);
        if (withUnrelatedMovements) await SeedUnrelatedMovementsAsync();

        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        using var activity = new Activity("trial-balance-free-hierarchy").SetIdFormat(ActivityIdFormat.W3C).Start();
        using var counter = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId);
        var timer = Stopwatch.StartNew();
        var rows = await service.GetTrialBalanceAsync(user, new(2026, 10, 21), new(2026, 10, 21));
        timer.Stop();
        Assert.Equal(1, counter.Count);
        Assert.True(timer.Elapsed < TimeSpan.FromMilliseconds(750), $"Trial balance took {timer.Elapsed.TotalMilliseconds:F1} ms.");
        output.WriteLine($"Jerarquía libre: {counter.Count} SQL, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        foreach (var account in new[] { root, site, concept })
        {
            var row = Assert.Single(rows, item => item.AccountCode == account.Code);
            Assert.Equal(account.Name, row.AccountName);
            Assert.Equal(100m, row.OpeningDebit);
            Assert.Equal(100m, row.Debit);
            Assert.Equal(200m, row.ClosingDebit);
        }
        Assert.DoesNotContain(rows, row => row.AccountCode == unrelated.Code);
        Assert.Equal(125m, Assert.Single(rows, row => row.AccountCode == first.Code).ClosingDebit);
        Assert.Equal(75m, Assert.Single(rows, row => row.AccountCode == second.Code).ClosingDebit);
        foreach (var structuralCode in new[] { "8", "83", "8305" })
        {
            var row = Assert.Single(rows, item => item.AccountCode == structuralCode);
            before.TryGetValue(structuralCode, out var prior);
            Assert.Equal((prior?.OpeningDebit ?? 0m) + 100m, row.OpeningDebit);
            Assert.Equal((prior?.Debit ?? 0m) + 100m, row.Debit);
            Assert.Equal((prior?.ClosingDebit ?? 0m) + 200m, row.ClosingDebit);
        }
        Assert.Equal((before.GetValueOrDefault("9")?.Credit ?? 0m) + 100m, Assert.Single(rows, row => row.AccountCode == "9").Credit);
        Assert.Empty(await service.GetTrialBalanceAsync(user with { TenantId = Guid.NewGuid() }, new(2026, 10, 21), new(2026, 10, 21)));
        Assert.Empty(await service.GetTrialBalanceAsync(user with { BusinessId = Guid.NewGuid() }, new(2026, 10, 21), new(2026, 10, 21)));

        // Opening a balance grouping must return its actual children, including
        // intermediate auxiliary parents, with the same totals as that balance.
        foreach (var ancestor in new[] { root, site, concept })
        {
            var query = new AccountMovementQuery(new(2026, 10, 21), new(2026, 10, 21), AccountRootCode: ancestor.Code);
            await service.GetAccountMovementsPageAsync(user, query);
            using var rootCounter = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId);
            var rootTimer = Stopwatch.StartNew();
            var page = await service.GetAccountMovementsPageAsync(user, query);
            rootTimer.Stop();
            Assert.Equal(1, rootCounter.Count);
            Assert.True(rootTimer.Elapsed < TimeSpan.FromMilliseconds(750),
                $"Auxiliary grouping {ancestor.Code}: {rootTimer.Elapsed.TotalMilliseconds:F1} ms.");
            Assert.Equal(2, page.TotalCount);
            Assert.Equal(100m, page.Accounts.Sum(account => account.OpeningBalance));
            Assert.Equal(100m, page.Accounts.Sum(account => account.Debit));
            Assert.Equal(200m, page.Accounts.Sum(account => account.ClosingBalance));
            Assert.All(page.Items, item => Assert.True(item.AccountCode == first.Code || item.AccountCode == second.Code));
            output.WriteLine($"Auxiliar de agrupación {ancestor.Code}: {rootCounter.Count} SQL, {rootTimer.Elapsed.TotalMilliseconds:F1} ms.");
        }
        var unrelatedRoot = new AccountMovementQuery(new(2026, 10, 21), new(2026, 10, 21), AccountRootCode: unrelated.Code);
        Assert.Empty((await service.GetAccountMovementsPageAsync(user, unrelatedRoot)).Accounts);
        Assert.Empty((await service.GetAccountMovementsPageAsync(user with { TenantId = Guid.NewGuid() }, unrelatedRoot)).Items);
        var structural = await service.GetAccountMovementsPageAsync(user,
            unrelatedRoot with { AccountRootCode = "8" });
        var structuralBalance = Assert.Single(rows, row => row.AccountCode == "8");
        Assert.Equal(structuralBalance.OpeningDebit - structuralBalance.OpeningCredit,
            structural.Accounts.Sum(account => account.OpeningBalance));
        Assert.Equal(structuralBalance.Debit, structural.Accounts.Sum(account => account.Debit));
        var legacy = await client.GetFromJsonAsync<AccountMovementRow[]>(
            $"/api/commerce/v1/accounting/reports/account-movements?accountCode={concept.Code}&from=2026-10-21&to=2026-10-21");
        Assert.Equal(2, legacy!.Length);
        var apiRoot = await client.GetFromJsonAsync<AccountMovementPage>(
            $"/api/commerce/v1/accounting/reports/account-movements?accountRootCode={concept.Code}&from=2026-10-21&to=2026-10-21&page=1&pageSize=1");
        Assert.Equal(2, apiRoot!.TotalCount);
        Assert.Single(apiRoot.Items);

        if (withUnrelatedMovements)
        {
            // Reuse the same 100000 foreign journal rows. Local period rows are
            // posted through the canonical voucher so both pages contain 50.
            await PostAsync(new(2026, 10, 21), Enumerable.Repeat((first, 1m), 101).ToArray());
            var auxiliaryQuery = new AccountMovementQuery(new(2026, 10, 21), new(2026, 10, 21),
                1, 50, first.Code, second.Code);
            foreach (var pageNumber in new[] { 1, 2 })
            {
                var query = auxiliaryQuery with { Page = pageNumber };
                await service.GetAccountMovementsPageAsync(user, query); // Warmup is outside measurement.
                using var pageCounter = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId);
                var pageTimer = Stopwatch.StartNew();
                var page = await service.GetAccountMovementsPageAsync(user, query);
                pageTimer.Stop();
                Assert.Equal(1, pageCounter.Count);
                Assert.True(pageTimer.Elapsed < TimeSpan.FromMilliseconds(750),
                    $"Auxiliary page {pageNumber}: {pageTimer.Elapsed.TotalMilliseconds:F1} ms.");
                Assert.Equal(50, page.Items.Count);
                Assert.Equal(103, page.TotalCount);
                Assert.Equal(201m, page.Accounts.Sum(item => item.Debit));
                Assert.Equal(100m, page.Accounts.Sum(item => item.OpeningBalance));
                Assert.Equal(301m, page.Accounts.Sum(item => item.ClosingBalance));
                Assert.All(page.Items, item => Assert.True(item.AccountCode == first.Code || item.AccountCode == second.Code));
                output.WriteLine($"Auxiliar con 100000 partidas ajenas, página {pageNumber}: {pageCounter.Count} SQL, {pageTimer.Elapsed.TotalMilliseconds:F1} ms.");
            }
        }

        Task<AccountingAccountView> CreateAsync(string code, string name, bool posting, AccountingAccountView? parent = null) =>
            service.CreateAccountAsync(user, new(Guid.NewGuid(), fixture.TenantId, code, name, "OrderDebit", posting, false,
                parent is null ? null : new(parent.AccountId, "General", "Debit", null, null, null)));

        async Task PostAsync(DateOnly date, (AccountingAccountView Account, decimal Amount)[] debits)
        {
            var lines = debits.Select(item => new ManualVoucherLineRequest(item.Account.AccountId,
                null, null, "Movimiento del auxiliar", item.Amount, 0m)).Append(
                    new(counterpart.AccountId, null, null, "Contrapartida", 0m, debits.Sum(item => item.Amount))).ToArray();
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
                new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
                    new DateTimeOffset(date.ToDateTime(new(12, 0)), TimeSpan.Zero), "MANUAL_VOUCHER", "Jerarquía libre", lines));
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        async Task SeedUnrelatedMovementsAsync()
        {
            // Synthetic posted journal data exists only in the disposable fixture
            // database. It must not contribute to the queried business's totals.
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            Assert.StartsWith("AuralyServerSlice_", connection.Database);
            await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
            await using var command = new SqlCommand("""
                SET XACT_ABORT ON;
                SET NOCOUNT ON;
                DECLARE @OtherBusiness uniqueidentifier=NEWID(),@EntryId uniqueidentifier=NEWID(),
                  @DocumentId uniqueidentifier=NEWID(),@At datetimeoffset(7)='2026-10-21T12:00:00+00:00';
                INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
                VALUES(@OtherBusiness,@TenantId,N'Otra sede del benchmark',N'',N'',N'',N'',N'');
                -- The existing tenant-wide center also covers this new site.
                INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,
                  SourceDocumentType,SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
                VALUES(NEWID(),@TenantId,@OtherBusiness,@DocumentId,N'ManualAccountingVoucher',
                  HASHBYTES('SHA2_256',N'benchmark'),@At,1,N'Posted',1,@At);
                INSERT dbo.AccountingEntries(EntryId,TenantId,BusinessId,PeriodId,SourceDocumentId,
                  SourceDocumentType,EntryNumber,OccurredAt,PostedAt,Description,DebitTotal,CreditTotal,SourcePayloadHash,RuleVersion)
                SELECT TOP(1) @EntryId,@TenantId,@OtherBusiness,PeriodId,@DocumentId,N'ManualAccountingVoucher',
                  LEFT(CONVERT(nvarchar(36),@EntryId),24),@At,@At,N'Movimientos ajenos al reporte',50000,50000,
                  HASHBYTES('SHA2_256',N'benchmark'),1
                FROM dbo.AccountingPeriods WHERE TenantId=@TenantId AND StartsOn<='20261021' AND EndsOn>='20261021';
                IF @@ROWCOUNT<>1 THROW 51400,N'El benchmark requiere un período contable existente.',1;
                CREATE TABLE #Numbers(N int PRIMARY KEY);
                WITH Digit(N) AS (SELECT value FROM (VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) d(value))
                INSERT #Numbers SELECT a.N+10*b.N+100*c.N FROM Digit a CROSS JOIN Digit b CROSS JOIN Digit c;
                DECLARE @Start int=0;
                WHILE @Start<100000
                BEGIN
                  -- Only this disposable, serialized fixture holds the table lock.
                  -- FK checks use seeks without a hash-join workspace grant.
                  INSERT dbo.AccountingEntryLines WITH(TABLOCK)(EntryId,LineNumber,AccountId,Description,Debit,Credit)
                  SELECT @EntryId,N+@Start+1,CASE WHEN @Start<50000 THEN @DebitAccount ELSE @CreditAccount END,
                    N'Otra sede',CASE WHEN @Start<50000 THEN 1 ELSE 0 END,CASE WHEN @Start>=50000 THEN 1 ELSE 0 END
                  FROM #Numbers ORDER BY N OPTION(LOOP JOIN,MAXDOP 1);
                  SET @Start=@Start+1000;
                END;
                DROP TABLE #Numbers;
                DECLARE @Count bigint,@Debit decimal(19,4),@Credit decimal(19,4);
                SELECT @Count=COUNT_BIG(*),@Debit=SUM(Debit),@Credit=SUM(Credit)
                  FROM dbo.AccountingEntryLines WHERE EntryId=@EntryId OPTION(MAXDOP 1);
                IF @Count<>100000 OR @Debit<>50000 OR @Credit<>50000
                  THROW 51400,N'La historia sintética no está completa o no cuadra.',1;
                """, connection, transaction) { CommandTimeout = 60 };
            command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            command.Parameters.AddWithValue("@DebitAccount", first.AccountId);
            command.Parameters.AddWithValue("@CreditAccount", counterpart.AccountId);
            try
            {
                await command.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                if (transaction.Connection?.State == System.Data.ConnectionState.Open)
                    await transaction.RollbackAsync();
                throw;
            }
        }
    }

    [Fact]
    public async Task Order_accounts_use_the_same_voucher_and_trial_balance_without_changing_financial_statements()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var debitParent = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "830598", "Control deudor", "OrderDebit", false, false));
        var creditParent = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "999998", "Control acreedor", "OrderCredit", false, false));
        var debit = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "8305980101", "Control libre", "OrderDebit", true, false,
                new(debitParent.AccountId, "General", "Debit", null, null, null)));
        var credit = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "9999980101", "Contrapartida de control", "OrderCredit", true, false,
                new(creditParent.AccountId, "General", "Credit", null, null, null)));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "8305980102", "Tipo incorrecto", "Asset", true, false)));
        var date = new DateOnly(2026, 10, 21);
        var balanceBefore = await service.GetBalanceSheetAsync(user, date);
        var incomeBefore = await service.GetIncomeStatementAsync(user, date, date);
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        var id = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(id, fixture.BusinessId,
                new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Control sin cambio patrimonial", [
                    new(debit.AccountId, null, null, "Control", 100, 0),
                    new(credit.AccountId, null, null, "Contrapartida", 0, 100)]));
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var entry = await service.GetEntryAsync(user, id);
        Assert.NotNull(entry);
        Assert.Equal(100, entry.DebitTotal);
        Assert.Equal(entry.DebitTotal, entry.CreditTotal);
        var trial = await service.GetTrialBalanceAsync(user, date, date);
        Assert.Contains(trial, row => row.AccountCode == debit.Code && row.Debit == 100 && row.Credit == 0);
        Assert.Contains(trial, row => row.AccountCode == credit.Code && row.Debit == 0 && row.Credit == 100);
        Assert.Equal(balanceBefore, await service.GetBalanceSheetAsync(user, date));
        Assert.Equal(incomeBefore, await service.GetIncomeStatementAsync(user, date, date));
    }

    [Fact]
    public async Task Auxiliary_uses_free_codes_without_master_segments_and_preserves_explicit_configuration()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var identity = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read });
        var baseAccount = await service.CreateAccountAsync(identity,
            new(Guid.NewGuid(), fixture.TenantId, "110598", "Base de caja libre", "Asset", false, false));
        var properties = new AccountingAccountSemantics(baseAccount.AccountId, "Cash", "Debit", null, null, null);
        var request = new CreateAccountingAccountRequest(Guid.NewGuid(), fixture.TenantId, "1105980007041",
            "Caja con numeración propia", "Asset", true, false, properties);
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        using var activity = new Activity("free-puc-create").SetIdFormat(ActivityIdFormat.W3C).Start();
        AccountingAccountView auxiliary;
        using (var counter = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
        {
            var timer = Stopwatch.StartNew();
            auxiliary = await service.CreateAccountAsync(identity, request);
            timer.Stop();
            Assert.Equal(3, counter.Count);
            Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2));
            output.WriteLine($"Crear auxiliar libre: {counter.Count} SQL, {timer.Elapsed.TotalMilliseconds:F1} ms.");
        }
        Assert.Equal(request.Code, auxiliary.Code);
        Assert.Equal(properties, auxiliary.Semantics);
        Assert.Equal(auxiliary, await service.CreateAccountAsync(identity, request));
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.CreateAccountAsync(identity,
            request with { AccountId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.CreateAccountAsync(identity,
            request with { Semantics = properties with { BalanceSide = "Credit" } }));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.CreateAccountAsync(identity,
            request with { AccountId = Guid.NewGuid(), Code = "1105982", Semantics = properties with { BusinessId = Guid.NewGuid() } }));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.CreateAccountAsync(identity,
            request with { AccountId = Guid.NewGuid(), Code = "1105983", Semantics = properties with { ParentAccountId = auxiliary.AccountId } }));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.CreateAccountAsync(identity,
            request with { AccountId = Guid.NewGuid(), Code = "1105984", Semantics = properties with { FinancialFunction = "Payable" } }));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.CreateAccountAsync(identity,
            request with { AccountId = Guid.NewGuid(), Code = "1105985", Semantics = properties with { FinancialFunction = "Bank" } }));
        await Assert.ThrowsAsync<AccountingForbiddenException>(() => service.CreateAccountAsync(
            identity with { Permissions = new HashSet<string> { AccountingPermissionCodes.Read } }, request));
        var edited = await service.UpdateAccountAsync(identity, auxiliary.AccountId,
            new(auxiliary.Name + " editada", false, auxiliary.RowVersion!, properties));
        Assert.Null(edited.Semantics!.BusinessId);
        Assert.False(edited.ConfigurationLocked);
        Assert.Equal(edited, await service.UpdateAccountAsync(identity, edited.AccountId,
            new(edited.Name, false, auxiliary.RowVersion!, edited.Semantics)));

        var maintenanceBase = await service.CreateAccountAsync(identity,
            new(Guid.NewGuid(), fixture.TenantId, "514598", "Mantenimiento libre", "Expense", false, false));
        var maintenance = await service.CreateAccountAsync(identity,
            new(Guid.NewGuid(), fixture.TenantId, "5145980000102", "Cambio de aceite TLW 160", "Expense", true, false,
                new(maintenanceBase.AccountId, "General", "Debit", null, null, null)));
        Assert.Null(maintenance.Semantics!.BankAccountId);
        Assert.Null(maintenance.Semantics.PaymentMethodOptionId);
        Assert.Equal(maintenanceBase.AccountId, maintenance.Semantics.ParentAccountId);
        var grouping = await service.AccountOptionsAsync(identity, new(1, 1, "514598", IncludeStructural: true, GroupingOnly: true));
        Assert.Equal(1, grouping.TotalCount);
        Assert.Equal(maintenanceBase.AccountId, Assert.Single(grouping.Items).AccountId);
        var payableBase = await service.CreateAccountAsync(identity,
            new(Guid.NewGuid(), fixture.TenantId, "220598", "Base de proveedores libre", "Liability", false, false));
        var payable = await service.CreateAccountAsync(identity,
            new(Guid.NewGuid(), fixture.TenantId, "2205980042", "Proveedores", "Liability", true, true,
                new(payableBase.AccountId, "Payable", "Credit", null, null, null)));
        await Assert.ThrowsAsync<AccountingValidationException>(() => service.UpdateAccountAsync(identity, payable.AccountId,
            new(payable.Name, false, payable.RowVersion!)));
    }

    [Fact]
    public async Task Accepted_pending_voucher_locks_account_properties_without_posting_or_duplicate_effects()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var store = scope.ServiceProvider.GetRequiredService<IAccountingStore>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Configure, AccountingPermissionCodes.Read, AccountingPermissionCodes.Activate });
        await service.EnsureDefaultsAsync(user);
        await service.ActivateAsync(user, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        var parent = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "519598", "Base para congelar", "Expense", false, false));
        var account = await service.CreateAccountAsync(user,
            new(Guid.NewGuid(), fixture.TenantId, "5195980300001", "Gasto libre", "Expense", true, false,
                new(parent.AccountId, "General", "Debit", null, null, null)));
        var bank = (await service.ListAccountsAsync(user)).First(a => a.Code == "111005");
        var command = new ConfirmManualAccountingVoucherRequest(Guid.NewGuid(), fixture.BusinessId,
            new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Congelar al aceptar", [
                new(account.AccountId, null, null, "Gasto", 100, 0), new(bank.AccountId, null, null, "Banco", 0, 100)]);
        await store.ConfirmManualVoucherAsync(user, command, default);
        var used = Assert.Single((await service.AccountOptionsAsync(user, new(1, 1, AccountId: account.AccountId, Search: null))).Items);
        Assert.True(used.ConfigurationLocked);
        Assert.Null(await service.GetEntryAsync(user, command.VoucherId));
        await Assert.ThrowsAsync<AccountingConflictException>(() => service.UpdateAccountAsync(user, used.AccountId,
            new("No debe guardar", false, used.RowVersion!, used.Semantics! with { BalanceSide = "Credit" })));
        var renamed = await service.UpdateAccountAsync(user, used.AccountId, new("Nombre corregido", false, used.RowVersion!, used.Semantics));
        Assert.Equal(used.Semantics, renamed.Semantics);
        await store.ConfirmManualVoucherAsync(user, command, default);
        var replay = Assert.Single((await service.AccountOptionsAsync(user, new(1, 1, AccountId: account.AccountId, Search: null))).Items);
        Assert.Equal(renamed.RowVersion, replay.RowVersion);
    }

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
    public async Task Configured_account_keeps_its_semantics_until_posting_finishes()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        var account = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var category = AccountingCategories.Cash;
        await using var acceptance = new SqlConnection(fixture.ConnectionString);
        await acceptance.OpenAsync();
        await using var accepting = (SqlTransaction)await acceptance.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
        await using (var mapping = new SqlCommand("""
            INSERT dbo.AccountingAccountMappings(MappingId,TenantId,BusinessId,Category,AccountId,EffectiveFrom,CreatedAt)
            VALUES(NEWID(),@Tenant,@Business,@Category,@Account,'20990101',SYSDATETIMEOFFSET());
            """, acceptance, accepting))
        {
            mapping.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            mapping.Parameters.AddWithValue("@Business", fixture.BusinessId);
            mapping.Parameters.AddWithValue("@Category", category);
            mapping.Parameters.AddWithValue("@Account", account.AccountId);
            Assert.Equal(1, await mapping.ExecuteNonQueryAsync());
        }
        await using (var resolve = new SqlCommand("""
            SELECT AccountId FROM accounting.ConfiguredPostingAccount(@Tenant,@Business,@Category,'20990101');
            """, acceptance, accepting))
        {
            resolve.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            resolve.Parameters.AddWithValue("@Business", fixture.BusinessId);
            resolve.Parameters.AddWithValue("@Category", category);
            Assert.Equal(account.AccountId, (Guid)(await resolve.ExecuteScalarAsync())!);
        }
        // This is the gap between resolving the winning account and completing
        // its journal. A concurrent semantic edit must not overtake posting.
        // LOCK_TIMEOUT 0 tests the held lock, without a timing-based assertion.
        await using var editor = new SqlConnection(fixture.ConnectionString);
        await editor.OpenAsync();
        await using var editing = (SqlTransaction)await editor.BeginTransactionAsync();
        await using var change = new SqlCommand("""
            SET LOCK_TIMEOUT 0;
            UPDATE dbo.AccountingAccounts SET FinancialFunction=N'General'
            WHERE AccountId=@Account AND TenantId=@Tenant;
            """, editor, editing);
        change.Parameters.AddWithValue("@Account", account.AccountId);
        change.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        var failure = await Record.ExceptionAsync(async () => { await change.ExecuteNonQueryAsync(); });
        await editing.RollbackAsync();
        Assert.Equal(1222, Assert.IsType<SqlException>(failure).Number);
        await accepting.RollbackAsync();
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
        // The compatibility bank input now configures the same account through
        // the PUC participant, so its optimistic version changes as well.
        var configured = await client.GetFromJsonAsync<AccountingAccountOptionPage>(
            $"/api/commerce/v1/accounting/accounts?page=1&pageSize=25&search={account.Code}");
        account = Assert.Single(configured!.Items);
        Assert.Equal(bankId, account.Semantics!.BankAccountId);
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
