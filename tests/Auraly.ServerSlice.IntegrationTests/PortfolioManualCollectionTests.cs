using System.Net.Http.Json;
using Auraly.BuildingBlocks.Application.Synchronization;
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
    public async Task Manual_collections_apply_all_groups_and_notify_each_customer_once_in_one_batch(bool sameCustomer)
    {
        var (firstCustomer, userId, _) = await ConfigureAsync();
        var secondCustomer = sameCustomer ? firstCustomer : (await ConfigureAsync()).CustomerId;
        using var client = fixture.CreateUserClient(userId, ReceivablesPermissionCodes.ManageCredit,
            ReceivablesPermissionCodes.Read, ReceivablesPermissionCodes.RegisterPayment, AccountingPermissionCodes.Read,
            AccountingPermissionCodes.Configure, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            WorkSessionPermissionCodes.Read);
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var search = $"MAN-COLLECT-{Guid.NewGuid():N}";
        var counterpart = await AccountIdByCodeAsync("413595");
        using (var response = await client.PostAsJsonAsync("/api/commerce/v1/receivables/preexisting/import",
            new ImportPreexistingReceivablesRequest(fixture.BusinessId,
                [new(Guid.NewGuid(), firstCustomer, null, null, search + "-A", at, at.AddDays(30), 1_000m, counterpart, null),
                 new(Guid.NewGuid(), secondCustomer, null, null, search + "-B", at, at.AddDays(30), 1_000m, counterpart, null)])))
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var page = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=Receivable&search={search}&page=1&pageSize=10");
        var positions = page!.Items.OrderBy(item => item.DocumentNumber, StringComparer.Ordinal).ToArray();
        Assert.Equal(2, positions.Length);
        Assert.All(positions, item => Assert.Null(item.OpeningLinkIssue));
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var session = await fixture.OpenWorkSessionAsync(client);
        var lines = new List<VoucherDraftLine>();
        for (var index = 0; index < positions.Length; index++)
        {
            var position = positions[index];
            var amount = 100m * (index + 1);
            var group = Guid.NewGuid();
            lines.Add(new(cash.AccountId, null, null, "Recaudo en caja", amount, 0, LineId: Guid.NewGuid(),
                FinancialGroupId: group, PaymentEvidence: new(TenderedAmount: amount)));
            lines.Add(new(position.AccountId, position.PartyId, null, "Abono a la cuenta por cobrar", 0, amount,
                LineId: Guid.NewGuid(), ExistingPosition: new("Receivable", position.SubledgerId), FinancialGroupId: group));
        }
        var documentId = Guid.NewGuid();
        // Import recognizes the opening at its accounting acceptance date. The
        // invoice's historical issue date is not that recognition timestamp.
        var paymentAt = positions.Max(item => item.OccurredAt).AddMinutes(1);
        var capture = new SaveVoucherDraftRequest(documentId, AccountingManualDocumentTypes.ManualVoucher, paymentAt,
            "MANUAL_VOUCHER", "Recaudos agrupados por cuentas", search, lines, null, null, MainPartyId: positions[0].PartyId,
            PayloadVersion: 4, VoucherTypeCode: "Accounting", WorkSessionId: session.WorkSessionId);
        VoucherDraftView saved;
        using (var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}", capture))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            saved = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var notices = new SqlCommand("""
            SELECT COUNT(*) FROM dbo.PosSynchronizationOutboxMessages
            WHERE BusinessId=@BusinessId AND Stream=@Stream AND EntityType=N'Customer' AND EntityId IN(@First,@Second);
            """, connection);
        notices.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        notices.Parameters.AddWithValue("@Stream", PosSynchronizationStreams.Customers);
        notices.Parameters.AddWithValue("@First", firstCustomer);
        notices.Parameters.AddWithValue("@Second", secondCustomer);
        var before = (int)(await notices.ExecuteScalarAsync())!;
        VoucherDraftView sent;
        using (var writes = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture))
        using (var notifications = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.PosSynchronizationOutboxMessages", StringComparison.Ordinal)))
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            sent = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", sent.Status);
            Assert.Equal(1, writes.Count);
            Assert.Equal(1, notifications.Count);
        }
        Assert.Equal(sameCustomer ? 1 : 2, (int)(await notices.ExecuteScalarAsync())! - before);
        var originalTransaction = await ScalarAsync<Guid>("""
            SELECT ReceivableTransactionId FROM dbo.ReceivableTransactions
            WHERE ReceivableId=@Id AND TransactionType=N'Payment';
            """, positions[0].SubledgerId);
        var advanceAccount = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "2896", "Liability", "CustomerAdvance", "Credit");
        async Task<VoucherDraftView> PostNoncashAsync(SaveVoucherDraftRequest request)
        {
            var path = $"/api/commerce/v1/accounting/manual/drafts/{request.DocumentId}";
            using var save = await client.PutAsJsonAsync(path, request);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var draft = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
            var result = (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", result.Status);
            Assert.Empty(result.FinancialEffects!.Payments);
            Assert.Empty(result.FinancialEffects.DirectMovements);
            return result;
        }
        var unapplicationId = Guid.NewGuid();
        var unapplication = await PostNoncashAsync(new(unapplicationId, AccountingManualDocumentTypes.ManualVoucher,
            paymentAt.AddDays(1), "MANUAL_VOUCHER", "Desaplicar recaudo conservando el anticipo", search,
            [new(positions[0].AccountId, positions[0].PartyId, null, "Reabrir cuenta por cobrar", 50m, 0m,
                LineId: Guid.NewGuid(), ExistingPosition: new("Receivable", positions[0].SubledgerId, originalTransaction)),
             new(advanceAccount.AccountId, positions[0].PartyId, null, "Anticipo del cliente", 0m, 50m, LineId: Guid.NewGuid())],
            null, null, MainPartyId: positions[0].PartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
        var credit = Assert.Single(unapplication.FinancialEffects!.Positions);
        Assert.Equal(originalTransaction, Assert.Single(unapplication.FinancialEffects.Unapplications!).ReversesTransactionId);
        var reopened = (await client.GetFromJsonAsync<ReceivableDetail>($"/api/commerce/v1/receivables/{positions[0].SubledgerId}"))!;
        Assert.Equal(950m, reopened.OutstandingAmount);
        Assert.Equal(50m, Assert.Single(reopened.Transactions, item => item.Type == "Unapplication").BalanceChange);
        var paidPage = await client.GetFromJsonAsync<ReceivablePage>(
            $"/api/commerce/v1/receivables?search={Uri.EscapeDataString(positions[0].DocumentNumber)}&page=1&pageSize=20");
        Assert.Equal(50m, Assert.Single(paidPage!.Items, item => item.ReceivableId == positions[0].SubledgerId).PaidAmount);
        var report = await client.GetFromJsonAsync<ReceivablesReportPage>(
            $"/api/commerce/v1/receivables/report?search={Uri.EscapeDataString(positions[0].DocumentNumber)}&page=1&pageSize=20&consolidated=false&cutoff=2026-12-31");
        var reportItem = Assert.Single(report!.Items, item => item.ReceivableId == positions[0].SubledgerId);
        Assert.Equal(50m, reportItem.PaidAmount);
        Assert.NotNull(reportItem.Applications);
        Assert.Equal(50m, reportItem.Applications.Sum(item => item.Amount));
        var customerPage = await client.GetFromJsonAsync<CustomerPortfolioPage>(
            $"/api/commerce/v1/receivables/customers?search={Uri.EscapeDataString(positions[0].DocumentNumber)}&page=1&pageSize=20");
        Assert.Equal(50m, Assert.Single(customerPage!.Items).PaidAmount);
        Assert.Contains(reportItem.Applications, item => item.Amount == -50m && item.DocumentNumber.StartsWith("Desaplicación de "));
        Assert.Equal(50m, await ScalarAsync<decimal>("SELECT AvailableAmount FROM dbo.CustomerCredits WHERE CustomerCreditId=@Id", credit.PositionId));
        Assert.Equal((sameCustomer ? 1 : 2) + 1, (int)(await notices.ExecuteScalarAsync())! - before);

        var reapplicationId = Guid.NewGuid();
        var reapplied = await PostNoncashAsync(new(reapplicationId, AccountingManualDocumentTypes.ManualVoucher,
            paymentAt.AddDays(1), "MANUAL_VOUCHER", "Aplicar el anticipo sin recaudar otra vez", search,
            [new(positions[0].AccountId, positions[0].PartyId, null, "Aplicar a la cuenta por cobrar", 0m, 50m,
                LineId: Guid.NewGuid(), ExistingPosition: new("Receivable", positions[0].SubledgerId)),
             new(advanceAccount.AccountId, positions[0].PartyId, null, "Aplicar anticipo", 50m, 0m,
                LineId: Guid.NewGuid(), ExistingPosition: new("CustomerCredit", credit.PositionId))],
            null, null, MainPartyId: positions[0].PartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
        Assert.Single(reapplied.FinancialEffects!.CreditApplications);
        Assert.Equal(900m, (await client.GetFromJsonAsync<ReceivableDetail>($"/api/commerce/v1/receivables/{positions[0].SubledgerId}"))!.OutstandingAmount);
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT AvailableAmount FROM dbo.CustomerCredits WHERE CustomerCreditId=@Id", credit.PositionId));
        Assert.Equal(300m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.WorkSessionMovements WHERE WorkSessionId=@Id AND MovementType=N'ReceivablePayment'", session.WorkSessionId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPayments WHERE ParentSourceDocumentId=@Id", unapplicationId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPayments WHERE ParentSourceDocumentId=@Id", reapplicationId));
        Assert.Equal(2, sent.FinancialEffects!.Payments.Count);
        Assert.All(sent.FinancialEffects.Payments, item => Assert.Equal("Receivable", item.SubledgerKind));
        var history = await client.GetFromJsonAsync<CustomerPaymentHistoryPage>(
            $"/api/commerce/v1/receivable-payments?search={Uri.EscapeDataString(sent.Row.EntryNumber!)}&page=1&pageSize=10");
        Assert.Equal(2, history!.TotalCount);
        Assert.Equal(300m, history.TotalAmount);
        Assert.All(history.Items, item =>
        {
            Assert.Equal(documentId, item.SourceDocumentId);
            Assert.Equal(AccountingManualDocumentTypes.ManualVoucher, item.SourceDocumentType);
            Assert.Equal(sent.Row.EntryNumber, item.DocumentNumber);
        });
        for (var index = 0; index < positions.Length; index++)
        {
            var detail = await client.GetFromJsonAsync<ReceivableDetail>($"/api/commerce/v1/receivables/{positions[index].SubledgerId}");
            Assert.Equal(1_000m - 100m * (index + 1), detail!.OutstandingAmount);
            Assert.Equal(documentId, Assert.Single(detail.Transactions.Where(item => item.Type == "Payment")).SourceDocumentId);
        }
        using (var writes = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture))
        using (var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{documentId}/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            Assert.Equal(0, writes.Count);
        }
        Assert.Equal((sameCustomer ? 1 : 2) + 2, (int)(await notices.ExecuteScalarAsync())! - before);

        // Undo a non-cash application by restoring its actual original credit.
        // The already received money and the credit's identity must not change.
        var originalApplication = Assert.Single(reapplied.FinancialEffects.CreditApplications);
        var creditCapturePath = $"/api/commerce/v1/accounting/unapplication-capture?subledgerKind=Receivable&subledgerId={positions[0].SubledgerId}&transactionId={originalApplication.EffectId}";
        VoucherDraftPreparation creditCapture;
        using (var response = await client.GetAsync(creditCapturePath))
        {
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            creditCapture = (await response.Content.ReadFromJsonAsync<VoucherDraftPreparation>())!;
        }
        var creditCounterpart = creditCapture.Lines[1].Value;
        Assert.Equal(advanceAccount.AccountId, creditCounterpart.AccountId);
        Assert.Equal(new("CustomerCredit", credit.PositionId), creditCounterpart.ExistingPosition);
        var restoredId = Guid.NewGuid();
        var restored = await PostNoncashAsync(new(restoredId, AccountingManualDocumentTypes.ManualVoucher,
            paymentAt.AddDays(2), "MANUAL_VOUCHER", "Restituir parte del saldo a favor original", search,
            [new(positions[0].AccountId, positions[0].PartyId, null, "Reabrir cuenta por cobrar", 25m, 0m,
                LineId: Guid.NewGuid(), ExistingPosition: new("Receivable", positions[0].SubledgerId, originalApplication.EffectId)),
             new(advanceAccount.AccountId, positions[0].PartyId, null, "Restituir saldo a favor original", 0m, 25m,
                LineId: Guid.NewGuid(), ExistingPosition: new("CustomerCredit", credit.PositionId))],
            null, null, MainPartyId: positions[0].PartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
        Assert.Empty(restored.FinancialEffects!.Positions);
        Assert.Empty(restored.FinancialEffects.Payments);
        Assert.Single(restored.FinancialEffects.Unapplications!);
        Assert.Equal(925m, (await client.GetFromJsonAsync<ReceivableDetail>($"/api/commerce/v1/receivables/{positions[0].SubledgerId}"))!.OutstandingAmount);
        Assert.Equal(25m, await ScalarAsync<decimal>("SELECT AvailableAmount FROM dbo.CustomerCredits WHERE CustomerCreditId=@Id", credit.PositionId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerCredits WHERE SourceDocumentId=@Id", restoredId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.CustomerPayments WHERE ParentSourceDocumentId=@Id", restoredId));
        Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", restoredId));
        Assert.Equal(originalApplication.EffectId, await ScalarAsync<Guid>("SELECT ReversesTransactionId FROM dbo.ReceivableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'CreditUnapplication'", restoredId));
        Assert.Equal(300m, await ScalarAsync<decimal>("SELECT SUM(Amount) FROM dbo.WorkSessionMovements WHERE WorkSessionId=@Id AND MovementType=N'ReceivablePayment'", session.WorkSessionId));
        var afterRestore = (await client.GetFromJsonAsync<ReceivablePage>(
            $"/api/commerce/v1/receivables?search={Uri.EscapeDataString(positions[0].DocumentNumber)}&page=1&pageSize=20"))!;
        Assert.Equal(50m, Assert.Single(afterRestore.Items).PaidAmount);
        var restoredReport = (await client.GetFromJsonAsync<ReceivablesReportPage>(
            $"/api/commerce/v1/receivables/report?search={Uri.EscapeDataString(positions[0].DocumentNumber)}&page=1&pageSize=20&consolidated=false&cutoff=2026-12-31"))!;
        var restoredReportItem = Assert.Single(restoredReport.Items);
        Assert.Equal(925m, restoredReportItem.OutstandingAmount);
        Assert.Equal(50m, restoredReportItem.PaidAmount);
    }
}
