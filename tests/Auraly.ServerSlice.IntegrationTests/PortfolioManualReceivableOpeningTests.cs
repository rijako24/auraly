using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class ReceivablesVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_receivable_openings_bind_each_line_and_support_customer_or_party_only_collection(bool partyOnly)
    {
        var (customerId, userId, siteId) = await ConfigureAsync();
        var partyId = partyOnly ? fixture.SupplierPartyId :
            await ScalarAsync<Guid>("SELECT PartyId FROM dbo.Customers WHERE CustomerId=@Id", customerId);
        using var client = fixture.CreateUserClient(userId, AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Retry,
            ReceivablesPermissionCodes.Read, ReceivablesPermissionCodes.RegisterPayment, WorkSessionPermissionCodes.Read);
        var revenue = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "4198", "Revenue", "General", "Credit");
        var account = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "1398", "Asset", "Receivable", "Debit");
        var at = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.FromHours(-5));
        var id = Guid.NewGuid();
        var capture = new SaveVoucherDraftRequest(id, AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Dos cuentas por cobrar del mismo tercero", "CXCMANUAL",
            [new(account.AccountId, partyId, null, "Primera obligación", 1_000m, 0m, "CXC-UNO", Guid.NewGuid(), DueDate: at.AddDays(-15)),
                new(account.AccountId, partyId, null, "Segunda obligación", 2_000m, 0m, "CXC-DOS", Guid.NewGuid(), DueDate: at.AddDays(30)),
                new(revenue.AccountId, partyId, null, "Contrapartida", 0m, 3_000m, LineId: Guid.NewGuid())],
            null, null, MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{id}";
        VoucherDraftView saved;
        using (var response = await client.PutAsJsonAsync(path, capture with
        {
            Lines = capture.Lines.Select((line, index) => index == 0 ? line with { Reference = new string('X', 65) } : line).ToArray()
        }))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        using (var response = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("64 caracteres", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", id));
        using (var response = await client.PutAsJsonAsync(path, capture with { RowVersion = saved.RowVersion }))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        if (!partyOnly)
        {
            await using var roleConnection = new SqlConnection(fixture.ConnectionString);
            await roleConnection.OpenAsync();
            await using var role = new SqlCommand("UPDATE dbo.Customers SET IsActive=0 WHERE CustomerId=@Id", roleConnection);
            role.Parameters.AddWithValue("@Id", customerId);
            await role.ExecuteNonQueryAsync();
            try
            {
                using var response = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains("rol activo", await response.Content.ReadAsStringAsync());
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", id));
            }
            finally
            {
                role.CommandText = "UPDATE dbo.Customers SET IsActive=1 WHERE CustomerId=@Id";
                await role.ExecuteNonQueryAsync();
            }
        }
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
            beforeExecute: command =>
            {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                journal.Single(item => item!["SourceLineId"]!.GetValue<Guid>() == capture.Lines[1].LineId)!["SourceLineId"] = Guid.NewGuid();
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var response = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(1, fault.Count);
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Receivables WHERE SourceDocumentId=@Id", id));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.ReceivableTransactions WHERE SourceDocumentId=@Id", id));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", id));
        var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Equal(2, accepted.FinancialEffects!.Positions.Count);
        using (var writes = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.Receivables", StringComparison.Ordinal)))
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var response = await client.PostAsync($"/api/commerce/v1/accounting/postings/{id}/retry", null);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            }
            Assert.Equal(1, writes.Count);
        }
        var posted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Equal("Posted", posted.Status);
        Assert.Equal(accepted.FinancialEffects.Positions.Select(item => item.PositionId), posted.FinancialEffects!.Positions.Select(item => item.PositionId));
        foreach (var opening in posted.FinancialEffects.Positions)
        {
            Assert.Equal(capture.Lines.Single(line => line.LineId == opening.SourceLineId).DueDate, opening.DueDate);
            Assert.Equal(partyId, opening.PartyId);
            Assert.Equal(partyOnly ? (Guid?)null : customerId, opening.CounterpartyRoleId);
            Assert.Equal(partyOnly ? (Guid?)null : siteId, opening.PartySiteId);
            var page = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind=Receivable&subledgerId={opening.PositionId}&page=1&pageSize=10");
            var position = Assert.Single(page!.Items);
            Assert.Null(position.OpeningLinkIssue);
            Assert.Equal(account.AccountId, position.AccountId);
            Assert.Equal(opening.Amount, position.AvailableAmount);
        }
        Guid collectionId;
        string collectionNumber;
        if (!partyOnly)
        {
            collectionId = Guid.NewGuid();
            var payment = new ConfirmCustomerPaymentRequest(collectionId, fixture.BusinessId, customerId, null,
                at.AddDays(1), "COP", "Recaudo asistido de CxC manual",
                posted.FinancialEffects.Positions.Select(item => new CustomerPaymentAllocationRequest(item.PositionId, 100m)).ToArray(),
                [new("Cash", 200m, 200m)]);
            using var response = await SendAsync(client, "/api/commerce/v1/receivable-payments/confirm", payment, collectionId.ToString("N"));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            collectionNumber = (await response.Content.ReadFromJsonAsync<CustomerPaymentAcceptance>())!.DocumentNumber;
            await PortfolioOriginalAccountScenario.AssertOriginalLinesAsync(fixture, true, collectionId, 1);
        }
        else
        {
            var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
            var session = await fixture.OpenWorkSessionAsync(client);
            collectionId = Guid.NewGuid();
            var lines = posted.FinancialEffects.Positions.Select(item => new VoucherDraftLine(item.AccountId, item.PartyId, null,
                "Abono del tercero", 0m, 100m, LineId: Guid.NewGuid(), ExistingPosition: new("Receivable", item.PositionId)))
                .Prepend(new(cash.AccountId, null, null, "Ingreso a caja", 200m, 0m, LineId: Guid.NewGuid())).ToArray();
            using var savedPayment = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{collectionId}", capture with
            { DocumentId = collectionId, OccurredAt = at.AddDays(1), Description = "Recaudo de tercero sin rol de cliente", Lines = lines, WorkSessionId = session.WorkSessionId });
            Assert.True(savedPayment.IsSuccessStatusCode, await savedPayment.Content.ReadAsStringAsync());
            var paymentDraft = (await savedPayment.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{collectionId}/send", new SendVoucherDraftRequest(paymentDraft.RowVersion));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var collection = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", collection.Status);
            collectionNumber = collection.Row.EntryNumber!;
            Assert.Null(Assert.Single(collection.FinancialEffects!.Payments).CounterpartyRoleId);
        }
        foreach (var opening in posted.FinancialEffects.Positions)
            Assert.Equal(opening.Amount - 100m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Receivables WHERE ReceivableId=@Id", opening.PositionId));
        var history = await client.GetFromJsonAsync<CustomerPaymentHistoryPage>(
            $"/api/commerce/v1/receivable-payments?search={Uri.EscapeDataString(collectionNumber)}&page=1&pageSize=10");
        var paymentHistory = Assert.Single(history!.Items);
        Assert.Equal("Processed", paymentHistory.Status);
        Assert.Equal(200m, paymentHistory.TotalAmount);
        Assert.Equal(partyOnly ? (Guid?)null : customerId, paymentHistory.CustomerId);
        Assert.Equal(partyOnly ? (Guid?)collectionId : null, paymentHistory.SourceDocumentId);
    }
}
