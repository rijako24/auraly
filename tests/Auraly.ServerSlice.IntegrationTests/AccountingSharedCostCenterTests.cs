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
public sealed class AccountingSharedCostCenterTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Centers_are_shared_between_sites_without_warehouse_capture_and_posted_snapshots_do_not_change()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activation = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activation.EnsureSuccessStatusCode();
        var parentId = Guid.NewGuid();
        var code = "SHARED-" + parentId.ToString("N")[..12];
        AccountingCostCenterView parent;
        using (var create = await client.PostAsJsonAsync("/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(parentId, fixture.TenantId, code, "Centro compartido", null, false)))
        {
            create.EnsureSuccessStatusCode();
            parent = (await create.Content.ReadFromJsonAsync<AccountingCostCenterView>())!;
            Assert.Equal(fixture.TenantId, parent.TenantId);
        }
        var otherBusiness = Guid.NewGuid(); var foreignTenant = Guid.NewGuid(); var foreignCenter = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var setup = new SqlCommand("""
            INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website)
              VALUES(@Business,@Tenant,N'Otra sede del centro compartido',N'',N'',N'',N'',N'');
            INSERT dbo.UserRoles(UserRoleId,UserId,RoleId,BusinessId,AssignedAt)
              VALUES(NEWID(),@User,@Role,@Business,SYSUTCDATETIME());
            EXEC dbo.AccountingDefaultsProvision @TenantId=@Tenant,@BusinessId=@Business,@Now=@Now;
            INSERT dbo.Tenants(TenantId,Name,Email) VALUES(@ForeignTenant,N'Otra empresa de prueba',N'centers@example.test');
            INSERT dbo.AccountingCostCenters(CostCenterId,TenantId,Code,Name,IsDefault,IsActive,CreatedAt)
              VALUES(@ForeignCenter,@ForeignTenant,@Code,N'Centro privado de otra empresa',1,1,@Now);
            """, connection))
        {
            setup.Parameters.AddWithValue("@Business", otherBusiness); setup.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            setup.Parameters.AddWithValue("@User", fixture.UserId); setup.Parameters.AddWithValue("@Role", fixture.RoleId);
            setup.Parameters.AddWithValue("@ForeignTenant", foreignTenant); setup.Parameters.AddWithValue("@ForeignCenter", foreignCenter);
            setup.Parameters.AddWithValue("@Code", code); setup.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
            await setup.ExecuteNonQueryAsync();
        }
        client.DefaultRequestHeaders.Remove("X-Business-Id");
        client.DefaultRequestHeaders.Add("X-Business-Id", otherBusiness.ToString("D"));
        var shared = (await client.GetFromJsonAsync<AccountingCostCenterPage>(
            $"/api/commerce/v1/accounting/cost-centers?search={code}&page=1&pageSize=1"))!;
        Assert.Equal(parentId, Assert.Single(shared.Items).CostCenterId);
        Assert.Equal(1, shared.TotalCount);
        var foreign = (await client.GetFromJsonAsync<AccountingCostCenterPage>(
            $"/api/commerce/v1/accounting/cost-centers?ids={foreignCenter}&pageSize=1"))!;
        Assert.Empty(foreign.Items);
        var childId = Guid.NewGuid();
        using (var child = await client.PostAsJsonAsync("/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(childId, fixture.TenantId, "CHILD-" + childId.ToString("N")[..12], "Hijo entre sedes", parentId, false)))
            child.EnsureSuccessStatusCode();
        using (var wrongParent = await client.PostAsJsonAsync("/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(Guid.NewGuid(), fixture.TenantId, "FOREIGN-" + Guid.NewGuid().ToString("N")[..12], "No debe crearse", foreignCenter, false)))
            Assert.Equal(HttpStatusCode.BadRequest, wrongParent.StatusCode);
        using (var warehouse = await client.PutAsJsonAsync("/api/commerce/v1/accounting/cost-center-assignments", new {
            assignmentId = Guid.NewGuid(), businessId = otherBusiness, costCenterId = parentId,
            operationKind = "Sales", warehouseId = fixture.WarehouseId, isActive = true }))
            Assert.Equal(HttpStatusCode.BadRequest, warehouse.StatusCode);
        using (var rule = await client.PutAsJsonAsync("/api/commerce/v1/accounting/cost-center-assignments",
            new SaveAccountingCostCenterAssignmentRequest(Guid.NewGuid(), otherBusiness, parentId, "Sales", true)))
            rule.EnsureSuccessStatusCode();
        var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
        var account = accounts.First(row => row.IsActive && row.AllowsPosting && !row.RequiresParty &&
            row.Semantics?.FinancialFunction is null or "General");
        var documentId = Guid.NewGuid();
        using (var accepted = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/vouchers",
            new ConfirmManualAccountingVoucherRequest(documentId, otherBusiness,
                new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero), "AJUSTE", "Centro utilizado desde otra sede",
                [new(account.AccountId, null, parentId, "Centro creado en la primera sede", 125, 0),
                 new(account.AccountId, null, childId, "Centro hijo creado en la segunda sede", 0, 125)])))
            accepted.EnsureSuccessStatusCode();
        await using var snapshot = new SqlCommand("""
            SELECT entry.EntryId,line.LineNumber,line.AccountId,line.CostCenterId,line.CostCenterCodeSnapshot,
              line.CostCenterNameSnapshot,line.Debit,line.Credit
            FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Document ORDER BY line.LineNumber FOR JSON PATH;
            """, connection);
        snapshot.Parameters.AddWithValue("@Document", documentId);
        var before = (string)(await snapshot.ExecuteScalarAsync())!;
        Assert.Contains("Centro compartido", before, StringComparison.Ordinal);
        using (var rename = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/cost-centers/{parentId}",
            new UpdateCostCenterRequest(code, "Nombre posterior", null, false, true, parent.RowVersion))) rename.EnsureSuccessStatusCode();
        Assert.Equal(before, (string)(await snapshot.ExecuteScalarAsync())!);
        await using var totals = new SqlCommand("""
            SELECT COUNT(DISTINCT entry.EntryId),COUNT(*),SUM(line.Debit-line.Credit)
            FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Document AND entry.BusinessId=@Business AND entry.TenantId=@Tenant;
            """, connection);
        totals.Parameters.AddWithValue("@Document", documentId); totals.Parameters.AddWithValue("@Business", otherBusiness);
        totals.Parameters.AddWithValue("@Tenant", fixture.TenantId);
        await using var result = await totals.ExecuteReaderAsync();
        Assert.True(await result.ReadAsync()); Assert.Equal(1, result.GetInt32(0)); Assert.Equal(2, result.GetInt32(1)); Assert.Equal(0, result.GetDecimal(2));
    }

}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountingSharedCostCenterPerformanceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Shared cost-center performance";
}

[Collection(AccountingSharedCostCenterPerformanceCollection.Name)]
public sealed class AccountingSharedCostCenterPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Ten_thousand_centers_keep_search_and_first_next_pages_in_one_bounded_command()
    {
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read });
        var prefix = "CCB-" + Guid.NewGuid().ToString("N")[..12] + "-";
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var seed = new SqlCommand("""
            WITH Digits AS(SELECT N FROM(VALUES(0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) digit(N))
            INSERT dbo.AccountingCostCenters(CostCenterId,TenantId,Code,Name,IsDefault,IsActive,CreatedAt)
            SELECT NEWID(),@Tenant,@Prefix+RIGHT(N'0000'+CONVERT(nvarchar(4),a.N+10*b.N+100*c.N+1000*d.N),4),
              N'Centro del benchmark',0,1,SYSUTCDATETIME() FROM Digits a CROSS JOIN Digits b CROSS JOIN Digits c CROSS JOIN Digits d;
            """, connection);
        seed.Parameters.AddWithValue("@Tenant", fixture.TenantId); seed.Parameters.AddWithValue("@Prefix", prefix);
        await seed.ExecuteNonQueryAsync();
        // The collection owns an isolated database. Its fixture drops that
        // database after measurement instead of deleting 10,000 referenced
        // principals against another scenario's accumulated financial history.
        foreach (var query in new[] { new AccountingCostCenterQuery(Search: prefix, PageSize: 20),
            new AccountingCostCenterQuery(Search: prefix, Page: 2, PageSize: 20),
            new AccountingCostCenterQuery(Search: prefix + "09", PageSize: 20) })
        {
            var times = new List<double>();
            for (var sample = 0; sample <= 20; sample++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    commandFilter: command => command.CommandText.Contains("SELECT COUNT(*) FROM dbo.AccountingCostCenters", StringComparison.Ordinal));
                var timer = Stopwatch.StartNew();
                var page = await service.ListCostCentersAsync(user, query);
                timer.Stop(); Assert.Equal(1, counter.Count); Assert.Equal(20, page.Items.Count);
                Assert.Equal(query.Search == prefix ? 10_000 : 100, page.TotalCount);
                Assert.All(page.Items, row => Assert.StartsWith(query.Search!, row.Code));
                if (query.Page == 2) Assert.Equal(prefix + "0020", page.Items[0].Code);
                if (sample > 0) times.Add(timer.Elapsed.TotalMilliseconds);
            }
            var p95 = times.Order().ElementAt(18);
            output.WriteLine("Cost centers: page={0}, search={1}, p95={2:F1}ms, 1 SQL, 20 samples", query.Page, query.Search, p95);
            Assert.True(p95 < 500, $"The cost-center page exceeded 500ms: p95 {p95:F1}ms.");
        }
    }
}
