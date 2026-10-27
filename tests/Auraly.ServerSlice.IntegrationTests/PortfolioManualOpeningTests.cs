using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PayablesVerticalSliceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_payable_openings_bind_each_source_line_and_reuse_native_payment_application(bool differentAccounts)
    {
        await ConfigureSeriesAndAccountingAsync(Guid.NewGuid());
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend, AccountingPermissionCodes.Retry,
            PayablesPermissionCodes.Read, PayablesPermissionCodes.RegisterPayment);
        var expense = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "5198", "Expense", "General", "Debit");
        var first = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "2298", "Liability", "Payable", "Credit");
        var second = differentAccounts ? await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "2297", "Liability", "Payable", "Credit") : first;
        var at = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.FromHours(-5));
        var id = Guid.NewGuid();
        var capture = new SaveVoucherDraftRequest(id, AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Dos obligaciones del mismo proveedor", $"AP-{id:N}",
            [new(expense.AccountId, fixture.SupplierPartyId, null, "Gasto reconocido", 3_000m, 0m, LineId: Guid.NewGuid()),
                new(first.AccountId, fixture.SupplierPartyId, null, "Primera obligación", 0m, 1_000m,
                    "FACTURA-UNO", Guid.NewGuid(), DueDate: at.AddDays(-15)),
                new(second.AccountId, fixture.SupplierPartyId, null, "Segunda obligación", 0m, 2_000m,
                    "FACTURA-DOS", Guid.NewGuid(), DueDate: at.AddDays(30))], null, null,
            MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{id}";
        var invalid = capture with { Lines = capture.Lines.Select((line, index) => index == 1 ? line with { DueDate = null } : line).ToArray() };
        VoucherDraftView saved;
        using (var response = await client.PutAsJsonAsync(path, invalid))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        using (var response = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("vencimiento", await response.Content.ReadAsStringAsync());
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", id));
        using (var response = await client.PutAsJsonAsync(path, capture with { RowVersion = saved.RowVersion }))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id", id));

        // Fail after the shared opening batch, during its journal binding. The
        // source stays accepted, but neither debt nor opening GL may survive.
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
            beforeExecute: command =>
            {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                var line = journal.Single(item => item!["SourceLineId"]!.GetValue<Guid>() == capture.Lines[2].LineId);
                line!["SourceLineId"] = Guid.NewGuid();
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var response = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal(1, fault.Count);
        }
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id", id));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id", id));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", id));
        var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Equal(2, accepted.FinancialEffects!.Positions.Count);
        Assert.Empty(accepted.FinancialEffects.Payments);
        using (var response = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id", id));
        using (var writes = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.Payables", StringComparison.Ordinal)))
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
        Assert.Equal(2, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Payables p
            JOIN dbo.Suppliers supplier ON supplier.SupplierId=p.SupplierId
            JOIN dbo.AccountingEntryLines line ON line.EntryId=p.OpeningEntryId AND line.LineNumber=p.OpeningEntryLineNumber
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId AND entry.SourceDocumentId=p.SourceDocumentId
            WHERE p.SourceDocumentId=@Id AND p.SourceLineId=line.SourceLineId AND p.OriginalAmount=line.Credit
              AND line.PartyId=supplier.PartyId AND line.Debit=0
            """, id));
        Assert.Equal(2, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Opening'", id));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", id));
        foreach (var effect in posted.FinancialEffects.Positions)
        {
            Assert.Equal(capture.Lines.Single(line => line.LineId == effect.SourceLineId).DueDate, effect.DueDate);
            var page = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&subledgerId={effect.PositionId}&page=1&pageSize=10");
            var position = Assert.Single(page!.Items);
            Assert.Null(position.OpeningLinkIssue);
            Assert.Equal(effect.AccountId, position.AccountId);
            Assert.Equal(effect.Amount, position.AvailableAmount);
        }
        var payment = new ConfirmSupplierPaymentRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId,
            at.AddDays(1), "COP", "Pago asistido de obligaciones manuales",
            posted.FinancialEffects.Positions.Select(item => new SupplierPaymentAllocationRequest(item.PositionId, 100m)).ToArray(),
            [new(SupplierPaymentMethods.Cash, 200m, 200m)]);
        using (var writes = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture))
        using (var response = await SendAsync(client, "/api/commerce/v1/payable-payments/confirm", payment, payment.PaymentId.ToString("N")))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal(1, writes.Count);
        }
        foreach (var opening in posted.FinancialEffects.Positions)
            Assert.Equal(opening.Amount - 100m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", opening.PositionId));
        await PortfolioOriginalAccountScenario.AssertOriginalLinesAsync(fixture, false, payment.PaymentId, differentAccounts ? 2 : 1);

    }
}
