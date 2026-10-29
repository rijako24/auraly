using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Payment_annulment_shares_original_budget_rollback_and_replay_without_opening_an_advance(
        bool supplier, bool manualPayment)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Retry, PayablesPermissionCodes.Read, ReceivablesPermissionCodes.Read,
            PayablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.RegisterPayment,
            WorkSessionPermissionCodes.ManageCash);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var partyId = supplier ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(true);
        var kind = supplier ? "Payable" : "Receivable";
        var role = supplier ? "Supplier" : "Customer";
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "2296" : "1395", supplier ? "Liability" : "Asset", kind, supplier ? "Credit" : "Debit");
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "5196" : "4196", supplier ? "Expense" : "Revenue", "General", supplier ? "Debit" : "Credit");
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var at = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.FromHours(-5));
        var opening = await SaveAndSendAsync(new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            at, "MANUAL_VOUCHER", "Obligación antes del dinero devuelto", "DEVOLUCION-PAGO",
            [new(control.AccountId, partyId, null, "Obligación", supplier ? 0m : 1000m, supplier ? 1000m : 0m,
                LineId: Guid.NewGuid(), DueDate: at.AddDays(30)),
             new(counterpart.AccountId, partyId, null, "Contrapartida", supplier ? 1000m : 0m, supplier ? 0m : 1000m,
                LineId: Guid.NewGuid())], null, null, MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
        var debt = Assert.Single(opening.FinancialEffects!.Positions);
        var session = await fixture.OpenWebWorkSessionAsync();
        var paymentId = Guid.NewGuid();
        var originalSourceDocumentId = paymentId;
        if (manualPayment)
        {
            var payment = await SaveAndSendAsync(new(paymentId, AccountingManualDocumentTypes.ManualVoucher,
                at.AddMinutes(1), "MANUAL_VOUCHER", "Abono original", "DEVOLUCION-PAGO",
                [new(control.AccountId, partyId, null, "Aplicar abono", supplier ? 500m : 0m, supplier ? 0m : 500m,
                    LineId: Guid.NewGuid(), ExistingPosition: new(kind, debt.PositionId)),
                 new(cash.AccountId, partyId, null, "Dinero original", supplier ? 0m : 500m, supplier ? 500m : 0m,
                    LineId: Guid.NewGuid())], null, null, MainPartyId: partyId, PayloadVersion: 4,
                VoucherTypeCode: supplier ? "Disbursement" : "Accounting", WorkSessionId: session));
            paymentId = Assert.Single(payment.FinancialEffects!.Payments).PaymentId;
        }
        else
        {
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var series = new SqlCommand("""
                    IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=@Type AND IsActive=1)
                      INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DocumentType,Prefix,SeriesCode,Padding,
                        RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
                      VALUES(NEWID(),@BusinessId,@Type,@Prefix,N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
                    """, connection);
                series.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                series.Parameters.AddWithValue("@Type", supplier ? "PayablePayment" : "ReceivablePayment");
                series.Parameters.AddWithValue("@Prefix", Auraly.BuildingBlocks.Domain.Documents.AuralyDocumentTypes.DefaultPrefix(
                    supplier ? "PayablePayment" : "ReceivablePayment"));
                await series.ExecuteNonQueryAsync();
            }
            client.DefaultRequestHeaders.Add("Idempotency-Key", paymentId.ToString("N"));
            using var response = supplier
                ? await client.PostAsJsonAsync("/api/commerce/v1/payable-payments/confirm",
                    new ConfirmSupplierPaymentRequest(paymentId, fixture.BusinessId, fixture.SupplierId, at.AddMinutes(1), "COP",
                        "Abono original", [new(debt.PositionId, 500m)], [new("Cash", 500m)], session))
                : await client.PostAsJsonAsync("/api/commerce/v1/receivable-payments/confirm",
                    new ConfirmCustomerPaymentRequest(paymentId, fixture.BusinessId, debt.CounterpartyRoleId!.Value, session,
                        at.AddMinutes(1), "COP", "Abono original", [new(debt.PositionId, 500m)], [new("Cash", 500m)]));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
        }
        var original = await ScalarAsync<Guid>($"SELECT {kind}TransactionId FROM dbo.{kind}Transactions WHERE {kind}Id=@Id AND TransactionType=N'Payment'", debt.PositionId);
        await AssertBalanceAsync(500m);
        var capturePath = $"/api/commerce/v1/accounting/unapplication-capture?subledgerKind={kind}&subledgerId={debt.PositionId}&transactionId={original}";
        var preparation = (await client.GetFromJsonAsync<VoucherDraftPreparation>(capturePath))!;
        Assert.Null(preparation.Lines[1].Value.AccountId);
        Assert.Equal(original, preparation.Lines[0].Value.ExistingPosition!.ReversesTransactionId);
        var paymentCorrectionPath = $"/api/commerce/v1/accounting/payment-correction-capture?subledgerKind={kind}&paymentId={paymentId}";
        using (var removedAction = await client.GetAsync(paymentCorrectionPath + "&correctionKind=ReturnMoney"))
            Assert.Equal(HttpStatusCode.BadRequest, removedAction.StatusCode);
        var annulPreparation = (await client.GetFromJsonAsync<VoucherDraftPreparation>(paymentCorrectionPath))!;
        Assert.Equal(2, annulPreparation.Lines.Count);
        Assert.Contains("Anulación", annulPreparation.Description);
        Assert.Equal(original, annulPreparation.Lines[0].Value.ExistingPosition!.ReversesTransactionId);
        Assert.NotNull(annulPreparation.Lines[1].Value.AccountId);
        if (manualPayment) Assert.Equal(cash.AccountId, annulPreparation.Lines[1].Value.AccountId);
        var annulment = await SaveAnnulmentAsync(100.0001m);
        var annulmentPath = $"/api/commerce/v1/accounting/manual/drafts/{annulment.DocumentId}";
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
            beforeExecute: command => command.CommandText = "THROW 51999,'Injected payment annulment posting fault.',1;\n" + command.CommandText))
        using (var response = await client.PostAsJsonAsync(annulmentPath + "/send", new SendVoucherDraftRequest(annulment.RowVersion)))
        {
            Assert.True(response.StatusCode == HttpStatusCode.InternalServerError,
                $"Expected injected fault; got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            Assert.Equal(1, fault.Count);
        }
        await AssertBalanceAsync(500m);
        await AssertAnnulmentAsync(annulment.DocumentId, 0);
        var excessive = await SaveAnnulmentAsync(400m);
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{excessive.DocumentId}/send",
            new SendVoucherDraftRequest(excessive.RowVersion)))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", excessive.DocumentId));
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{annulment.DocumentId}/retry", null))
            Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        await AssertBalanceAsync(600.0001m);
        await AssertAnnulmentAsync(annulment.DocumentId, 1);
        var posted = (await client.GetFromJsonAsync<VoucherDraftView>(annulmentPath))!;
        Assert.Equal("Posted", posted.Status);
        Assert.Equal("PaymentReturn", Assert.Single(posted.FinancialEffects!.Unapplications!).ReversedTransactionType);
        Assert.Empty(posted.FinancialEffects.Positions);
        for (var replay = 0; replay < 2; replay++)
        {
            using var response = await client.PostAsJsonAsync(annulmentPath + "/send", new SendVoucherDraftRequest(annulment.RowVersion));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        }
        await AssertAnnulmentAsync(annulment.DocumentId, 1);
        var rest = await SaveAnnulmentAsync(399.9999m);
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{rest.DocumentId}/send",
            new SendVoucherDraftRequest(rest.RowVersion)))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        await AssertBalanceAsync(1000m);
        await AssertAnnulmentAsync(rest.DocumentId, 1);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var bankBalance = new SqlCommand("""
                SELECT COALESCE(SUM(line.Debit-line.Credit),0)
                FROM dbo.AccountingEntries entry
                JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
                WHERE line.AccountId=@AccountId
                  AND entry.SourceDocumentId IN (@OriginalId,@FirstAnnulmentId,@SecondAnnulmentId);
                """, connection);
            bankBalance.Parameters.AddWithValue("@AccountId", annulPreparation.Lines[1].Value.AccountId!.Value);
            bankBalance.Parameters.AddWithValue("@OriginalId", originalSourceDocumentId);
            bankBalance.Parameters.AddWithValue("@FirstAnnulmentId", annulment.DocumentId);
            bankBalance.Parameters.AddWithValue("@SecondAnnulmentId", rest.DocumentId);
            Assert.Equal(0m, (decimal)(await bankBalance.ExecuteScalarAsync())!);
        }
        using (var response = await client.GetAsync(capturePath)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await client.GetAsync(paymentCorrectionPath)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(500m, await ScalarAsync<decimal>($"SELECT SUM(Amount) FROM dbo.{role}PaymentApplications WHERE PaymentId=@Id", paymentId));
        Assert.Equal(supplier ? -500m : 500m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", paymentId));
        Assert.Equal(0m, await ScalarAsync<decimal>($"SELECT SUM(Amount) FROM accounting.PortfolioAppliedAmounts(@BusinessId,N'{kind}') WHERE PositionId=@Id", debt.PositionId));
        if (supplier)
        {
            var history = (await client.GetFromJsonAsync<SupplierPaymentHistoryPage>(
                "/api/commerce/v1/payable-payments?page=1&pageSize=100"))!;
            Assert.Equal(0m, Assert.Single(history.Items, item => item.PaymentId == paymentId).NetAppliedAmount);
        }
        else
        {
            var history = (await client.GetFromJsonAsync<CustomerPaymentHistoryPage>(
                "/api/commerce/v1/receivable-payments?page=1&pageSize=100"))!;
            Assert.Equal(0m, Assert.Single(history.Items, item => item.PaymentId == paymentId).NetAppliedAmount);
        }
        if (!manualPayment)
        {
            var replacementId = Guid.NewGuid();
            client.DefaultRequestHeaders.Add("Idempotency-Key", replacementId.ToString("N"));
            using var replacement = supplier
                ? await client.PostAsJsonAsync("/api/commerce/v1/payable-payments/confirm",
                    new ConfirmSupplierPaymentRequest(replacementId, fixture.BusinessId, fixture.SupplierId, at.AddMinutes(5), "COP",
                        "Nuevo pago tras anulación", [new(debt.PositionId, 500m)], [new("Cash", 500m)], session))
                : await client.PostAsJsonAsync("/api/commerce/v1/receivable-payments/confirm",
                    new ConfirmCustomerPaymentRequest(replacementId, fixture.BusinessId, debt.CounterpartyRoleId!.Value, session,
                        at.AddMinutes(5), "COP", "Nuevo recaudo tras anulación", [new(debt.PositionId, 500m)], [new("Cash", 500m)]));
            Assert.True(replacement.IsSuccessStatusCode, await replacement.Content.ReadAsStringAsync());
            client.DefaultRequestHeaders.Remove("Idempotency-Key");
            await AssertBalanceAsync(500m);
        }

        async Task<VoucherDraftView> SaveAnnulmentAsync(decimal amount)
        {
            var lines = annulPreparation.Lines.Select(item => item.Value).ToArray();
            lines[0] = lines[0] with { Debit = supplier ? 0m : amount, Credit = supplier ? amount : 0m, LineId = Guid.NewGuid() };
            lines[1] = lines[1] with { Debit = supplier ? amount : 0m,
                Credit = supplier ? 0m : amount, LineId = Guid.NewGuid() };
            var id = Guid.NewGuid();
            using var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{id}",
                new SaveVoucherDraftRequest(id, AccountingManualDocumentTypes.ManualVoucher, at.AddMinutes(2), "MANUAL_VOUCHER",
                    "Anulación de pago", "ANULACION-PAGO", lines, null, null, MainPartyId: partyId,
                    PayloadVersion: 4, VoucherTypeCode: "Accounting", WorkSessionId: null));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        async Task<VoucherDraftView> SaveAndSendAsync(SaveVoucherDraftRequest capture)
        {
            var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
            using var save = await client.PutAsJsonAsync(path, capture);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var draft = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
            return (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        async Task AssertBalanceAsync(decimal expected) =>
            Assert.Equal(expected, await ScalarAsync<decimal>($"SELECT OutstandingAmount FROM dbo.{(supplier ? "Payables" : "Receivables")} WHERE {kind}Id=@Id", debt.PositionId));
        async Task AssertAnnulmentAsync(Guid id, int count)
        {
            Assert.Equal(count, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'Unapplication' AND ReversesTransactionId IS NOT NULL", id));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", id));
            Assert.Equal(count, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", id));
            Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE SourceDocumentId=@Id", id));
            Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Payments WHERE ParentSourceDocumentId=@Id", id));
            if (count > 0)
            {
                var amount = await ScalarAsync<decimal>($"SELECT SUM(Amount) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'Unapplication'", id);
                Assert.Equal(amount, await ScalarAsync<decimal>("SELECT SUM(line.Debit) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId WHERE entry.SourceDocumentId=@Id", id));
            }
        }
        async Task<T> ScalarAsync<T>(string sql, Guid id)
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            command.Parameters.AddWithValue("@Id", id);
            command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            return (T)(await command.ExecuteScalarAsync())!;
        }
    }
}
