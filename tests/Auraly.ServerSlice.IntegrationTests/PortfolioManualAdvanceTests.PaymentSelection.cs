using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Selected_portfolio_positions_prepare_one_manual_payment_without_reading_other_debts(bool supplier)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var party = supplier ? await CreateSelectionSupplierPartyAsync() : await CreateCustomerPartyAsync(true);
        var debt = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "2296" : "1395", supplier ? "Liability" : "Asset", supplier ? "Payable" : "Receivable", supplier ? "Credit" : "Debit");
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "5196" : "4196", supplier ? "Expense" : "Revenue", "General", supplier ? "Debit" : "Credit");
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var at = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var kind = supplier ? "Payable" : "Receivable";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var siteCommand = new SqlCommand("SELECT TOP(1) PartySiteId FROM dbo.PartySites WHERE PartyId=@Party AND IsActive=1 ORDER BY IsPrimary DESC,PartySiteId", connection);
        siteCommand.Parameters.AddWithValue("@Party", party);
        var siteId = (Guid)(await siteCommand.ExecuteScalarAsync())!;
        var opening = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Obligaciones para selección", null,
            [.. Enumerable.Range(1, 100).Select(index => new VoucherDraftLine(debt.AccountId, party, null, $"Factura {index}",
                supplier ? 0 : 1_000, supplier ? 1_000 : 0, $"MENU-{index}", Guid.NewGuid(), DueDate: at.AddDays(30), PartySiteId: siteId)),
             new(counterpart.AccountId, party, null, "Contrapartida", supplier ? 100_000 : 0, supplier ? 0 : 100_000, LineId: Guid.NewGuid())],
            null, null, MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var opened = await SendReceiptScenarioAsync(client, opening);
        var ids = opened.FinancialEffects!.Positions.Select(position => position.PositionId).ToArray();
        Assert.Equal(100, ids.Length);
        await using var roleCommand = new SqlCommand(supplier
            ? "SELECT SupplierId FROM dbo.Suppliers WHERE TenantId=@Tenant AND PartyId=@Party"
            : "SELECT CustomerId FROM dbo.Customers WHERE TenantId=@Tenant AND PartyId=@Party", connection);
        roleCommand.Parameters.AddWithValue("@Tenant", fixture.TenantId); roleCommand.Parameters.AddWithValue("@Party", party);
        var roleId = (Guid)(await roleCommand.ExecuteScalarAsync())!;
        using var scope = fixture.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read });
        foreach (var size in new[] { 1, 100 })
        {
            var query = new AccountingOpenPositionQuery(kind, 1, size, CounterpartyRoleId: roleId, SubledgerIds: ids.Take(size).ToArray());
            await service.OpenPositionsAsync(actor, query); // Warm-up precedes measurement.
            var samples = new List<double>();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                using var reads = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                var watch = Stopwatch.StartNew();
                var selected = await service.OpenPositionsAsync(actor, query);
                watch.Stop(); samples.Add(watch.Elapsed.TotalMilliseconds);
                Assert.Equal(1, reads.Count); Assert.Equal(size, selected.Items.Count); Assert.Equal(size, selected.TotalCount);
                Assert.All(selected.Items, position => { Assert.Contains(position.SubledgerId, query.SubledgerIds!); Assert.Equal(party, position.PartyId); Assert.Equal(debt.AccountId, position.AccountId); });
            }
            var p95 = samples.Order().ElementAt(18);
            Assert.True(p95 < 1_500, $"{kind}, {size} seleccionadas, un SQL: p95 {p95:F1} ms.");
            Console.WriteLine($"{kind}, {size} seleccionadas, un SQL: p95 {p95:F1} ms.");
        }
        var request = new AccountingOpenPositionSelectionRequest(kind, roleId, ids.Take(2).ToArray(), new(2026, 9, 11));
        const string path = "/api/commerce/v1/accounting/open-positions/selection";
        using var response = await client.PostAsJsonAsync(path, request); response.EnsureSuccessStatusCode();
        var page = (await response.Content.ReadFromJsonAsync<AccountingOpenPositionPage>())!;
        Assert.Equal(2, page.TotalCount); Assert.Equal(2, page.Items.Count);
        Assert.All(page.Items, item => Assert.Equal(1_000, item.OutstandingAmount));
        foreach (var invalid in new[] { request with { SubledgerIds = [] }, request with { SubledgerIds = [ids[0], ids[0]] },
                     request with { SubledgerIds = [Guid.Empty] }, request with { SubledgerIds = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray() },
                     request with { SubledgerKind = "All" }, request with { CounterpartyRoleId = Guid.Empty } })
        {
            using var rejected = await client.PostAsJsonAsync(path, invalid);
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        }
        using (var deniedClient = fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate))
        using (var denied = await deniedClient.PostAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var selectedQuery = new AccountingOpenPositionQuery(kind, 1, 2, CounterpartyRoleId: roleId, SubledgerIds: request.SubledgerIds);
        Assert.Empty((await service.OpenPositionsAsync(actor with { TenantId = Guid.NewGuid() }, selectedQuery)).Items);
        Assert.Empty((await service.OpenPositionsAsync(actor with { BusinessId = Guid.NewGuid() }, selectedQuery)).Items);
        Assert.Empty((await service.OpenPositionsAsync(actor, selectedQuery with { CounterpartyRoleId = Guid.NewGuid() })).Items);
        using (var unrestricted = await client.GetAsync($"/api/commerce/v1/accounting/open-positions?subledgerKind={kind}&pageSize=100"))
            Assert.Equal(HttpStatusCode.BadRequest, unrestricted.StatusCode);
        // Selecting is read-only. Only this explicit confirmation applies the
        // two partial amounts through the existing manual-voucher engine.
        var paymentId = Guid.NewGuid();
        var amounts = new[] { 125m, 75m };
        var payment = new SaveVoucherDraftRequest(paymentId, AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Pago de las dos seleccionadas", null,
            [.. page.Items.Select((position, index) => new VoucherDraftLine(position.AccountId, position.PartyId, null,
                position.DocumentNumber, supplier ? amounts[index] : 0, supplier ? 0 : amounts[index], position.DocumentNumber,
                Guid.NewGuid(), ExistingPosition: new(kind, position.SubledgerId), FinancialGroupId: paymentId, PartySiteId: position.PartySiteId)),
             new(cash.AccountId, party, null, "Dinero", supplier ? 0 : 200, supplier ? 200 : 0, LineId: Guid.NewGuid(), FinancialGroupId: paymentId)],
            null, null, MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: supplier ? "Disbursement" : "CashReceipt");
        var paid = await SendReceiptScenarioAsync(client, payment);
        using var replay = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{paymentId}/send", new SendVoucherDraftRequest(paid.RowVersion));
        replay.EnsureSuccessStatusCode();
        var after = await service.OpenPositionsAsync(actor, selectedQuery);
        foreach (var (position, index) in page.Items.Select((position, index) => (position, index)))
            Assert.Equal(1_000 - amounts[index], Assert.Single(after.Items.Where(item => item.SubledgerId == position.SubledgerId)).OutstandingAmount);
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", paymentId));
        Assert.Equal(1, await CountAsync(supplier ? "SELECT COUNT(*) FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id" : "SELECT COUNT(*) FROM dbo.CustomerPayments WHERE ParentSourceDocumentId=@Id", paymentId));
    }
}
