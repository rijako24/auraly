using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AccountingSliceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Auraly accounting slice";
}

[Collection(AccountingSliceCollection.Name)]
public sealed class AccountingVerticalSliceTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Invoice_and_credit_note_post_balanced_once_and_periods_are_controlled()
    {
        var invoice = WithUblSnapshot(fixture.CreateValidRequest(9_811));
        using (var pos = fixture.CreateClient())
        using (var upload = fixture.CreateUploadMessage(invoice))
        using (var response = await pos.SendAsync(upload))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Equal(AccountingPostingStatuses.PendingConfiguration,
            await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", invoice.DocumentId));
        Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", invoice.DocumentId));

        using var accounting = fixture.CreateAdminClient(
            AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.PeriodsManage, AccountingPermissionCodes.Retry,
            SalesReturnPermissionCodes.Create, SalesReturnPermissionCodes.Confirm);
        await ConfigureAsync(accounting);

        using (var retry = await accounting.PostAsync($"/api/commerce/v1/accounting/postings/{invoice.DocumentId:D}/retry", null))
        {
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var posting = await retry.Content.ReadFromJsonAsync<AccountingPostingView>();
            Assert.NotNull(posting); Assert.Equal(AccountingPostingStatuses.Posted, posting.Status); Assert.NotNull(posting.EntryId);
        }
        await AssertBalancedAsync(invoice.DocumentId);
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", invoice.DocumentId));

        using (var replay = fixture.CreateUploadMessage(invoice))
        using (var replayResponse = await fixture.CreateClient().SendAsync(replay))
            Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", invoice.DocumentId));

        using (var entryResponse = await accounting.GetAsync($"/api/commerce/v1/accounting/entries/by-document/{invoice.DocumentId:D}"))
        {
            Assert.Equal(HttpStatusCode.OK, entryResponse.StatusCode);
            var entry = await entryResponse.Content.ReadFromJsonAsync<AccountingEntryView>();
            Assert.NotNull(entry); Assert.StartsWith("ASI-", entry.EntryNumber); Assert.Equal(entry.DebitTotal, entry.CreditTotal); Assert.True(entry.Lines.Count >= 3);
        }

        var returnRequest = new ConfirmSalesReturnRequest(
            Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId, invoice.DocumentId,
            new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.FromHours(-5)),
            ReturnEconomicResolutions.Refund, "Cash", "Devolucion contable",
            [new ConfirmSalesReturnLineRequest(1, .5m, ReturnInventoryDispositions.Sellable)]);
        using (var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/sales-returns/confirm")
        { Content = JsonContent.Create(returnRequest) })
        {
            message.Headers.Add("Idempotency-Key", $"accounting-return-{returnRequest.ReturnId:N}");
            using var response = await accounting.SendAsync(message);
            Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(AccountingPostingStatuses.Posted,
            await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", returnRequest.ReturnId));
        await AssertBalancedAsync(returnRequest.ReturnId);
        Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", returnRequest.ReturnId));

        using (var report = await accounting.GetAsync("/api/commerce/v1/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31"))
        {
            Assert.Equal(HttpStatusCode.OK, report.StatusCode);
            var rows = await report.Content.ReadFromJsonAsync<IReadOnlyList<TrialBalanceRow>>();
            Assert.NotNull(rows); Assert.NotEmpty(rows);
            Assert.Equal(rows.Sum(row => row.Debit), rows.Sum(row => row.Credit));
        }

        var nextPeriod = Guid.NewGuid();
        using (var create = await accounting.PostAsJsonAsync("/api/commerce/v1/accounting/periods",
            new CreateAccountingPeriodRequest(nextPeriod, fixture.TenantId, new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), "2027")))
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using (var close = await accounting.PostAsync($"/api/commerce/v1/accounting/periods/{nextPeriod:D}/close", null))
            Assert.Equal(HttpStatusCode.NoContent, close.StatusCode);
        Assert.Equal("Closed", await ScalarAsync<string>("SELECT Status FROM dbo.AccountingPeriods WHERE PeriodId=@Id", nextPeriod));
    }

    [Fact]
    public async Task Accounting_endpoints_enforce_permission_and_scope()
    {
        using var denied = fixture.CreateAdminClient();
        using var response = await denied.GetAsync("/api/commerce/v1/accounting/reports/trial-balance?from=2026-01-01&to=2026-12-31");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var configured = fixture.CreateAdminClient(AccountingPermissionCodes.Configure);
        using var wrongScope = await configured.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
            new CreateAccountingAccountRequest(Guid.NewGuid(), Guid.NewGuid(), "9999", "Fuera de alcance", "Asset", true, false));
        Assert.Equal(HttpStatusCode.Forbidden, wrongScope.StatusCode);
    }

    private async Task ConfigureAsync(HttpClient client)
    {
        var accounts = new Dictionary<string, (string Code, string Name, string Type)>(StringComparer.Ordinal)
        {
            [AccountingCategories.Cash] = ("110505", "Caja general", "Asset"),
            [AccountingCategories.DebitCardClearing] = ("111005", "Tarjetas debito por cobrar", "Asset"),
            [AccountingCategories.CreditCardClearing] = ("111010", "Tarjetas credito por cobrar", "Asset"),
            [AccountingCategories.TransferClearing] = ("111015", "Transferencias por conciliar", "Asset"),
            [AccountingCategories.AccountsReceivable] = ("130505", "Clientes", "Asset"),
            [AccountingCategories.SalesRevenue] = ("413595", "Ingresos por ventas", "Revenue"),
            [AccountingCategories.SalesReturns] = ("417595", "Devoluciones en ventas", "ContraRevenue"),
            [AccountingCategories.OutputVat] = ("240805", "IVA generado", "Liability"),
            [AccountingCategories.Inventory] = ("143505", "Inventarios", "Asset"),
            [AccountingCategories.CostOfGoodsSold] = ("613595", "Costo de ventas", "Expense"),
            [AccountingCategories.CustomerCreditsPayable] = ("238095", "Saldos a favor de clientes", "Liability")
        };
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var item in accounts)
        {
            var id = Guid.NewGuid(); ids[item.Key] = id;
            using var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/accounts",
                new CreateAccountingAccountRequest(id, fixture.TenantId, item.Value.Code, item.Value.Name, item.Value.Type, true,
                    item.Key is AccountingCategories.AccountsReceivable or AccountingCategories.CustomerCreditsPayable));
            Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        }
        var center = Guid.NewGuid();
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/cost-centers",
            new CreateCostCenterRequest(center, fixture.BusinessId, "PRINCIPAL", "Operacion principal", null, true)))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var period = Guid.NewGuid();
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/accounting/periods",
            new CreateAccountingPeriodRequest(period, fixture.TenantId, new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "2026")))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        foreach (var item in ids)
        {
            using var response = await client.PutAsJsonAsync("/api/commerce/v1/accounting/account-mappings",
                new SetAccountMappingRequest(fixture.TenantId, null, item.Key, item.Value, new DateOnly(2026, 1, 1), null));
            Assert.True(response.StatusCode == HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        }
    }

    private async Task AssertBalancedAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT e.DebitTotal,e.CreditTotal,SUM(l.Debit),SUM(l.Credit)
            FROM dbo.AccountingEntries e INNER JOIN dbo.AccountingEntryLines l ON l.EntryId=e.EntryId
            WHERE e.SourceDocumentId=@Id GROUP BY e.DebitTotal,e.CreditTotal;
            """, connection); command.Parameters.AddWithValue("@Id", documentId); await using var reader = await command.ExecuteReaderAsync(); Assert.True(await reader.ReadAsync());
        Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(1)); Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(2)); Assert.Equal(reader.GetDecimal(1), reader.GetDecimal(3));
    }

    private PosSaleUploadRequest WithUblSnapshot(PosSaleUploadRequest request)
    {
        var address = new PosSaleUblAddressContract("11001", "Bogota", "Bogota D.C.", "11", "CL 1 2 3");
        var supplier = new PosSaleUblPartyContract(ServerSliceFixture.SupplierTaxId, "7", "31", "1", "EMISOR HISTORICO", "EMISOR HISTORICO", "R-99-PN", "01", "IVA", address);
        var customer = new PosSaleUblPartyContract("222222222", "0", "13", "2", "CLIENTE HISTORICO", "CLIENTE HISTORICO", "R-99-PN", "ZZ", "No aplica", address);
        return request with
        {
            UblSnapshot = new PosSaleUblSnapshotContract(fixture.FiscalIssuerConfigurationId, "COP", "01", supplier, customer,
            new PosSaleUblAuthorizationContract(ServerSliceFixture.AuthorizationNumber, new DateOnly(2026, 1, 1), new DateOnly(2028, 12, 31), ServerSliceFixture.Prefix, 1, 10000),
            "auraly-test-software", [new PosSaleUblLineContract(1, "P-E2E", "999", "EA", "IVA", 19m)], "1", "10", DateOnly.FromDateTime(request.FiscalSnapshot.IssuedAt.Date), null)
        };
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    { await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync(); await using var command = new SqlCommand(sql, connection); command.Parameters.AddWithValue("@Id", id); return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T)); }
    private async Task<int> CountAsync(string table, string column, Guid id)
    { Assert.Contains($"{table}:{column}", new[] { "AccountingEntries:SourceDocumentId" }); await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync(); await using var command = new SqlCommand($"SELECT COUNT(*) FROM dbo.[{table}] WHERE [{column}]=@Id", connection); command.Parameters.AddWithValue("@Id", id); return Convert.ToInt32(await command.ExecuteScalarAsync()); }
}
