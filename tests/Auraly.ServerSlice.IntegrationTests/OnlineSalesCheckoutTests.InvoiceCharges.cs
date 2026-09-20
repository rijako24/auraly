using System.Net;
using System.Net.Http.Json;
using Auraly.Contracts.Authorization;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class OnlineSalesCheckoutTests
{
    [Theory]
    [InlineData("Always", 5000, "Cash", 0)]
    [InlineData("Never", 0, "Cash", 0)]
    [InlineData("Always", 5000, "CreditCard", 0)]
    [InlineData("Never", 0, "CreditCard", 0)]
    [InlineData("Always", 5000, "Cash", 19)]
    [InlineData("Always", 5000, "CreditCard", 19)]
    [InlineData("Never", 0, "Cash", 19)]
    public async Task Online_charge_commands_preserve_versions_clear_with_last_product_and_issue_once(
        string inclusion, decimal included, string method, decimal taxRate)
    {
        var userId = await CreateUserAsync("online-charge");
        using var client = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCreate, CommercePermissionCodes.SalesRemoveLine,
            InvoiceChargePermissions.Read, InvoiceChargePermissions.Configure,
            ExpensePermissionCodes.Read, ExpensePermissionCodes.Configure, CatalogPermissionCodes.Update);
        var options = (await client.GetFromJsonAsync<ExpenseWorkspaceOptions>("/api/commerce/v1/expenses/options"))!;
        var conceptId = Guid.NewGuid();
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/expenses/concepts/{conceptId}",
            new SaveExpenseConceptRequest(conceptId, fixture.BusinessId, "Domicilio online", options.ExpenseAccounts.First().AccountId,
                options.CostCenters.First().CostCenterId, null, true))) response.EnsureSuccessStatusCode();
        var id = Guid.NewGuid();
        var code = $"ONLINE-{id:N}"[..32];
        using var taxResponse = await client.PostAsJsonAsync("/api/commerce/v1/tax-profiles",
            new SaveTaxProfileRequest(fixture.BusinessId, code, $"IVA {taxRate}% online", taxRate));
        taxResponse.EnsureSuccessStatusCode();
        var tax = (await taxResponse.Content.ReadFromJsonAsync<TaxProfileSummary>())!;
        var configuration = new SaveInvoiceChargeRequest(id, 0, code, "Domicilio online", true, 0,
            "Fixed", 5000, inclusion, null, conceptId, tax.TaxProfileId, [], [fixture.SupplierId], tax.TaxProfileId);
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/invoice-charges/{id}", configuration))
            response.EnsureSuccessStatusCode();
        var draft = await CaptureAsync(client, await OpenAsync(client));
        var products = draft.PayableAmount;
        var appliedId = Guid.NewGuid();
        async Task<OnlineSalesDraft> Mutate(HttpMethod method, string suffix, object body, string key)
        {
            using var message = new HttpRequestMessage(method, $"/api/commerce/v1/pos/drafts/{draft.DraftId}/{suffix}")
                { Content = JsonContent.Create(body) };
            message.Headers.Add("Idempotency-Key", key);
            using var response = await client.SendAsync(message);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<OnlineSalesDraft>())!;
        }
        var add = new InvoiceChargeDraftRequest(appliedId, id, 1, fixture.SupplierId, null, draft.Version);
        var key = Guid.NewGuid().ToString("N");
        draft = await Mutate(HttpMethod.Put, $"charges/{appliedId}", add, key);
        Assert.Equal(products + included, draft.PayableAmount);
        var replay = await Mutate(HttpMethod.Put, $"charges/{appliedId}", add, key);
        Assert.Equal(draft.Version, replay.Version);
        Assert.Single(replay.Charges!);
        draft = await Mutate(HttpMethod.Post, $"charges/{appliedId}/remove",
            new RemoveOnlineSalesDraftLineRequest(draft.Version), Guid.NewGuid().ToString("N"));
        Assert.Empty(draft.Charges!); Assert.Equal(products, draft.PayableAmount);
        draft = await Mutate(HttpMethod.Put, $"charges/{appliedId}", add with { ExpectedVersion = draft.Version }, Guid.NewGuid().ToString("N"));
        using (var restricted = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCreate))
        using (var deniedRequest = new HttpRequestMessage(HttpMethod.Post,
            $"/api/commerce/v1/pos/drafts/{draft.DraftId}/lines/{Assert.Single(draft.Lines).LineId}/remove")
            { Content = JsonContent.Create(new RemoveOnlineSalesDraftLineRequest(draft.Version)) })
        {
            deniedRequest.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
            using var denied = await restricted.SendAsync(deniedRequest);
            Assert.Equal(HttpStatusCode.PreconditionRequired, denied.StatusCode);
        }
        draft = await Mutate(HttpMethod.Post, $"lines/{Assert.Single(draft.Lines).LineId}/remove",
            new RemoveOnlineSalesDraftLineRequest(draft.Version), Guid.NewGuid().ToString("N"));
        Assert.Empty(draft.Lines); Assert.Empty(draft.Charges!); Assert.Equal(0, draft.PayableAmount);
        draft = await CaptureAsync(client, draft);
        draft = await Mutate(HttpMethod.Put, $"charges/{appliedId}", add with { ExpectedVersion = draft.Version }, Guid.NewGuid().ToString("N"));
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/invoice-charges/{id}",
            configuration with { ExpectedVersion = 1, Value = 9000, IsActive = false })) response.EnsureSuccessStatusCode();
        var exhaustedId = Guid.NewGuid();
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/invoice-charges/{exhaustedId}",
            configuration with { ChargeId = exhaustedId, Code = $"MANUAL-{exhaustedId:N}"[..32], Name = "Agotados online",
                CalculationMode = "Manual", Value = 5000, InclusionMode = "Never" })) response.EnsureSuccessStatusCode();
        var exhaustedAppliedId = Guid.NewGuid();
        draft = await Mutate(HttpMethod.Put, $"charges/{exhaustedAppliedId}",
            new InvoiceChargeDraftRequest(exhaustedAppliedId, exhaustedId, 1, fixture.SupplierId, 6500, draft.Version),
            Guid.NewGuid().ToString("N"));
        Assert.Equal(products + included, draft.PayableAmount);
        var checkout = new CompleteOnlineSalesDraftRequest(draft.Version, [new(method, draft.PayableAmount, null,
            CardFranchiseCode: method == "CreditCard" ? "Visa" : null,
            ApprovalNumber: method == "CreditCard" ? "TEST-CARGO" : null)]);
        var checkoutKey = Guid.NewGuid().ToString("N");
        var completed = await CompleteAsync(client, draft.DraftId, checkout, checkoutKey);
        var duplicated = await CompleteAsync(client, draft.DraftId, checkout, checkoutKey);
        Assert.True(duplicated.IsDuplicate);
        Assert.Equal(completed.Receipt.DocumentId, duplicated.Receipt.DocumentId);
        Assert.Equal(products + included, completed.Receipt.PayableAmount);
        Assert.Equal(included > 0 ? 2 : 1, completed.Receipt.Lines.Count);
        Assert.Empty(completed.NextDraft.Charges!);
        await WaitForWithholdingSnapshotAsync(completed.Receipt.DocumentId);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var verify = new SqlCommand("""
            SELECT e.GrossAmount,e.SourceInvoiceId,
              (SELECT COUNT(*) FROM dbo.Expenses WHERE SourceInvoiceId=@Invoice),
              (SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Charge),
              (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Charge),
              (SELECT SUM(TotalAmount) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Invoice),
              (SELECT SUM(TaxAmount) FROM reporting.SalesReportTaxFacts WHERE SourceDocumentId=@Invoice)
            FROM dbo.Expenses e WHERE e.ExpenseId=@Charge AND e.BusinessId=@BusinessId;
            """, connection);
        verify.Parameters.AddWithValue("@Invoice", completed.Receipt.DocumentId);
        verify.Parameters.AddWithValue("@Charge", appliedId);
        verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync()); Assert.Equal(5000, reader.GetDecimal(0));
        Assert.Equal(completed.Receipt.DocumentId, reader.GetGuid(1));
        Assert.Equal(2, reader.GetInt32(2)); Assert.Equal(0, reader.GetInt32(3)); Assert.Equal(1, reader.GetInt32(4));
        Assert.Equal(completed.Receipt.PayableAmount, reader.GetDecimal(5));
        Assert.Equal(completed.Receipt.TaxAmount, reader.GetDecimal(6));
    }
}
