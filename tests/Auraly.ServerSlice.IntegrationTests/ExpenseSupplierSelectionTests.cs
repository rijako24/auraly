using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Parties;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ExpenseSupplierSelectionTests(ServerSliceFixture fixture)
{
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
