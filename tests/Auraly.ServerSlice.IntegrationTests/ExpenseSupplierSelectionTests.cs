using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Parties;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Returns;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ExpenseSupplierSelectionTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Expense_options_can_omit_directories_used_by_remote_pickers()
    {
        using var client = fixture.CreateAdminClient(ExpensePermissionCodes.Read);
        var compact = await client.GetFromJsonAsync<ExpenseWorkspaceOptions>("/api/commerce/v1/expenses/options?includeDirectories=false");
        var legacy = await client.GetFromJsonAsync<ExpenseWorkspaceOptions>("/api/commerce/v1/expenses/options");
        Assert.NotNull(compact);
        Assert.NotNull(legacy);
        Assert.Empty(compact.Suppliers);
        Assert.Empty(compact.ExpenseAccounts);
        Assert.NotEmpty(legacy.Suppliers);
        Assert.NotEmpty(legacy.ExpenseAccounts);
        Assert.Equal(legacy.Concepts, compact.Concepts);
        Assert.Equal(legacy.CostCenters, compact.CostCenters);
    }

    [Fact]
    public async Task Account_picker_searches_before_pagination_and_limits_expense_permissions()
    {
        using var client = fixture.CreateAdminClient(ExpensePermissionCodes.Create);
        const string path = "/api/commerce/v1/accounting/account-options?expenseOnly=true&pageSize=1";
        var first = await client.GetFromJsonAsync<AccountingAccountOptionPage>(path + "&page=1");
        Assert.NotNull(first);
        Assert.Single(first.Items);
        var last = await client.GetFromJsonAsync<AccountingAccountOptionPage>(path + $"&page={first.TotalPages}");
        var target = Assert.Single(last!.Items);
        var filtered = await client.GetFromJsonAsync<AccountingAccountOptionPage>(path + $"&page=1&search={Uri.EscapeDataString(target.Code)}&accountId={target.AccountId:D}");
        Assert.Equal(target.AccountId, Assert.Single(filtered!.Items).AccountId);
        Assert.Equal(1, filtered.TotalCount);
        Assert.Equal("Expense", filtered.Items[0].AccountType);
        var empty = await client.GetFromJsonAsync<AccountingAccountOptionPage>(path + $"&page={first.TotalPages + 1}");
        Assert.Empty(empty!.Items);
        Assert.Equal(first.TotalCount, empty.TotalCount);
        using var forbidden = await client.GetAsync("/api/commerce/v1/accounting/account-options?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Theory]
    [InlineData(SalesReturnPermissionCodes.Read)]
    [InlineData(SalesReturnPermissionCodes.Create)]
    public async Task Returns_use_customer_options_without_granting_other_party_access(string permission)
    {
        using var client = fixture.CreateAdminClient(permission);
        using var options = await client.GetAsync("/api/commerce/v1/parties/role-options?role=Customer&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, options.StatusCode);
        using var suppliers = await client.GetAsync("/api/commerce/v1/parties/role-options?role=Supplier&page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.Forbidden, suppliers.StatusCode);
        using var workspace = await client.GetAsync("/api/commerce/v1/parties/");
        Assert.Equal(HttpStatusCode.Forbidden, workspace.StatusCode);
    }

    [Fact]
    public async Task Expense_creator_can_select_a_supplier_without_access_to_the_party_workspace()
    {
        using var client = fixture.CreateAdminClient(ExpensePermissionCodes.Create);
        var options = await client.GetFromJsonAsync<PartyRoleOptionPage>(
            "/api/commerce/v1/parties/role-options?role=Supplier&page=1&pageSize=10");
        Assert.NotNull(options);
        Assert.Contains(options.Items, item => item.RoleId == fixture.SupplierId);
        Assert.All(options.Items, item => Assert.Equal("Supplier", item.Role));
        Assert.InRange(options.Items.Count, 1, 10);

        foreach (var role in new[] { "Any", "Customer", "User", "Employee" })
        {
            using var response = await client.GetAsync($"/api/commerce/v1/parties/role-options?role={role}");
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var workspace = await client.GetAsync("/api/commerce/v1/parties/");
        Assert.Equal(HttpStatusCode.Forbidden, workspace.StatusCode);
    }

    [Fact]
    public async Task Expense_reader_can_filter_by_supplier_without_browsing_other_parties()
    {
        using var client = fixture.CreateAdminClient(ExpensePermissionCodes.Read);
        var options = await client.GetFromJsonAsync<PartyRoleOptionPage>(
            "/api/commerce/v1/parties/role-options?role=Supplier&page=1&pageSize=10");
        Assert.NotNull(options);
        Assert.Contains(options.Items, item => item.RoleId == fixture.SupplierId);
        using var otherRole = await client.GetAsync("/api/commerce/v1/parties/role-options?role=Customer");
        Assert.Equal(HttpStatusCode.Forbidden, otherRole.StatusCode);
        using var workspace = await client.GetAsync("/api/commerce/v1/parties/");
        Assert.Equal(HttpStatusCode.Forbidden, workspace.StatusCode);
    }
}
