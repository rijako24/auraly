using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Application.Expenses;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(AccountingSliceCollection.Name)]
public sealed partial class PortfolioManualAdvanceTests(ServerSliceFixture fixture)
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Advance_and_optional_abono_share_acceptance_application_session_and_parent(bool supplier, bool withObligation, bool customerRole)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Retry, PayablesPermissionCodes.Read, PayablesPermissionCodes.RegisterPayment,
            ReceivablesPermissionCodes.Read, ReceivablesPermissionCodes.RegisterPayment,
            ExpensePermissionCodes.Create, ExpensePermissionCodes.Configure, ExpensePermissionCodes.Cancel,
            TaxationPermissionCodes.ManageWithholdingRules, TaxationPermissionCodes.ViewWithholdingRules);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var partyId = supplier ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(withObligation || customerRole);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "1396" : "2396", supplier ? "Asset" : "Liability", supplier ? "SupplierAdvance" : "CustomerAdvance",
            supplier ? "Debit" : "Credit");
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var lines = new List<VoucherDraftLine>();
        Guid? obligationId = null;
        Guid? obligationSourceId = null;
        AccountingAccountView? control = null;
        if (withObligation)
        {
            control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                supplier ? "2296" : "1395", supplier ? "Liability" : "Asset", supplier ? "Payable" : "Receivable", supplier ? "Credit" : "Debit");
            var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                supplier ? "5196" : "4196", supplier ? "Expense" : "Revenue", "General", supplier ? "Debit" : "Credit");
            var obligation = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
                "MANUAL_VOUCHER", "Obligación antes del abono y anticipo", "OBL-ANTICIPO",
                [new(control.AccountId, partyId, null, "Obligación", supplier ? 0 : 1_000m, supplier ? 1_000m : 0,
                    "OBL-ANTICIPO", Guid.NewGuid(), DueDate: at.AddDays(30)),
                 new(counterpart.AccountId, partyId, null, "Contrapartida", supplier ? 1_000m : 0, supplier ? 0 : 1_000m, LineId: Guid.NewGuid())],
                null, null, MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
            var opened = await SaveAndSendAsync(obligation);
            obligationSourceId = obligation.DocumentId;
            obligationId = Assert.Single(opened.FinancialEffects!.Positions).PositionId;
            lines.Add(new(control.AccountId, partyId, null, "Abono a la obligación", supplier ? 250m : 0, supplier ? 0 : 250m,
                LineId: Guid.NewGuid(), ExistingPosition: new(supplier ? "Payable" : "Receivable", obligationId.Value)));
        }
        lines.Add(new(advance.AccountId, partyId, null, "Anticipo sin factura", supplier ? 500m : 0, supplier ? 0 : 500m, LineId: Guid.NewGuid()));
        // Treasury first proves that implicit groups are not identified by the position row order.
        lines.Insert(0, new(cash.AccountId, partyId, null, "Dinero del comprobante", supplier ? 0 : withObligation ? 750m : 500m,
            supplier ? withObligation ? 750m : 500m : 0, LineId: Guid.NewGuid()));
        var session = await fixture.OpenWebWorkSessionAsync();
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Anticipo desde cuentas", "ANTICIPO", lines, null, null, MainPartyId: partyId,
            PayloadVersion: 4, VoucherTypeCode: supplier ? "Disbursement" : "Accounting", WorkSessionId: session);
        var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        VoucherDraftView saved;
        using (var save = await client.PutAsJsonAsync(path, capture))
        {
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            saved = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        using (var unauthorized = fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend))
        using (var send = await unauthorized.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.Equal(HttpStatusCode.Forbidden, send.StatusCode);
        // A failure after the payment updates must roll back money, credit and any abono together.
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
            beforeExecute: command =>
            {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                journal.Single(line => line!["SourceLineId"]!.GetValue<Guid>() == capture.Lines[^1].LineId)!["SourceLineId"] = Guid.NewGuid();
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var send = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, send.StatusCode);
            Assert.Equal(1, fault.Count);
        }
        var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        var payment = Assert.Single(accepted.FinancialEffects!.Payments);
        var opening = Assert.Single(accepted.FinancialEffects.Positions);
        Assert.Equal(opening.PositionId, payment.AdvancePositionId);
        Assert.Equal(500m, payment.AdvanceAmount);
        Assert.Equal(withObligation ? 1 : 0, payment.Allocations.Count);
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{(supplier ? "Supplier" : "Customer")}Credits WHERE SourceDocumentId=@Id", capture.DocumentId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", payment.PaymentId));
        using (var applications = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture))
        {
            using var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{capture.DocumentId}/retry", null);
            Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
            Assert.Equal(1, applications.Count);
        }
        var role = supplier ? "Supplier" : "Customer";
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        string entryNumber;
        Guid creditOpeningEntry;
        int creditOpeningLine;
        await using (var verify = new SqlCommand($"""
            SELECT credit.CreditNature,credit.CurrencyCode,credit.OriginalAmount,credit.AvailableAmount,
              line.AccountId,line.Debit,line.Credit,entry.SourceDocumentId,
              (SELECT COUNT(*) FROM dbo.{role}CreditTransactions history WHERE history.{role}CreditId=credit.{role}CreditId),entry.EntryNumber,
              credit.OpeningEntryId,credit.OpeningEntryLineNumber
            FROM dbo.{role}Credits credit JOIN dbo.AccountingEntryLines line
              ON line.EntryId=credit.OpeningEntryId AND line.LineNumber=credit.OpeningEntryLineNumber
            JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId
            WHERE credit.SourceDocumentId=@Id AND credit.SourceLineId=line.SourceLineId AND line.PartyId=@PartyId;
            """, connection))
        {
            verify.Parameters.AddWithValue("@Id", capture.DocumentId); verify.Parameters.AddWithValue("@PartyId", partyId);
            await using var reader = await verify.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("Advance", reader.GetString(0)); Assert.Equal("COP", reader.GetString(1));
            Assert.Equal(500m, reader.GetDecimal(2)); Assert.Equal(500m, reader.GetDecimal(3));
            Assert.Equal(advance.AccountId, reader.GetGuid(4));
            Assert.Equal(supplier ? 500m : 0, reader.GetDecimal(5)); Assert.Equal(supplier ? 0 : 500m, reader.GetDecimal(6));
            Assert.Equal(capture.DocumentId, reader.GetGuid(7)); Assert.Equal(1, reader.GetInt32(8));
            entryNumber = reader.GetString(9);
            creditOpeningEntry = reader.GetGuid(10); creditOpeningLine = reader.GetInt32(11);
            Assert.False(await reader.ReadAsync());
        }
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Payments WHERE PaymentId=@Id AND Status=N'Processed'", payment.PaymentId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id AND Amount=" +
            (supplier ? "-" : "") + (withObligation ? "750" : "500"), payment.PaymentId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", capture.DocumentId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", payment.PaymentId));
        var history = await client.GetFromJsonAsync<JsonElement>($"/api/commerce/v1/{(supplier ? "payable" : "receivable")}-payments?search={Uri.EscapeDataString(entryNumber)}&page=1&pageSize=10");
        var row = Assert.Single(history.GetProperty("items").EnumerateArray());
        Assert.Equal(payment.PaymentId, row.GetProperty("paymentId").GetGuid());
        Assert.Equal(capture.DocumentId, row.GetProperty("sourceDocumentId").GetGuid());
        Assert.Equal(payment.GroupId, row.GetProperty("sourceGroupId").GetGuid());
        Assert.Equal(withObligation ? 750m : 500m, row.GetProperty("totalAmount").GetDecimal());
        Assert.Equal(withObligation ? 1 : 0, row.GetProperty("appliedDocumentCount").GetInt32());
        Assert.Single(row.GetProperty("payments").EnumerateArray());
        if (payment.PartySiteId is { } paymentSite)
        {
            var historyPath = $"/api/commerce/v1/{(supplier ? "payable" : "receivable")}-payments" +
                $"?search={Uri.EscapeDataString(entryNumber)}&page=1&pageSize=1&partySiteId=";
            var siteHistory = await client.GetFromJsonAsync<JsonElement>(historyPath + paymentSite);
            var siteRow = Assert.Single(siteHistory.GetProperty("items").EnumerateArray());
            Assert.Equal(payment.PaymentId, siteRow.GetProperty("paymentId").GetGuid());
            Assert.Equal(withObligation ? 750m : 500m, siteRow.GetProperty("totalAmount").GetDecimal());
            if (!supplier) Assert.Equal(withObligation ? 750m : 500m, siteHistory.GetProperty("totalAmount").GetDecimal());
            var otherSite = await client.GetFromJsonAsync<JsonElement>(historyPath + Guid.NewGuid());
            Assert.Empty(otherSite.GetProperty("items").EnumerateArray());
            Assert.Equal(0, otherSite.GetProperty("totalCount").GetInt32());
            if (!supplier) Assert.Equal(0m, otherSite.GetProperty("totalAmount").GetDecimal());
            // Schema compatibility copies only the frozen group dimension. A
            // payment replay must not repair metadata or repeat financial work.
            await using (var legacy = new SqlCommand($"UPDATE dbo.{role}Payments SET PartySiteId=NULL WHERE PaymentId=@Id;", connection))
            {
                legacy.Parameters.AddWithValue("@Id", payment.PaymentId);
                Assert.Equal(1, await legacy.ExecuteNonQueryAsync());
            }
            using (var replayWithoutRepair = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
                replayWithoutRepair.EnsureSuccessStatusCode();
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Payments WHERE PaymentId=@Id AND PartySiteId IS NULL", payment.PaymentId));
            await fixture.ApplyMigrationAsync("20261023_ManualPaymentSites.sql", connection);
            byte[] siteVersion;
            await using (var restored = new SqlCommand($"SELECT PartySiteId,RowVersion FROM dbo.{role}Payments WHERE PaymentId=@Id;", connection))
            {
                restored.Parameters.AddWithValue("@Id", payment.PaymentId);
                await using var restoredRow = await restored.ExecuteReaderAsync();
                Assert.True(await restoredRow.ReadAsync()); Assert.Equal(paymentSite, restoredRow.GetGuid(0));
                siteVersion = (byte[])restoredRow[1];
            }
            await fixture.ApplyMigrationAsync("20261023_ManualPaymentSites.sql", connection);
            await using (var unchanged = new SqlCommand($"SELECT RowVersion FROM dbo.{role}Payments WHERE PaymentId=@Id;", connection))
            {
                unchanged.Parameters.AddWithValue("@Id", payment.PaymentId);
                Assert.Equal(siteVersion, (byte[])(await unchanged.ExecuteScalarAsync())!);
            }
        }
        if (obligationId is { } target)
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{(supplier ? "Payables" : "Receivables")} WHERE {(supplier ? "Payable" : "Receivable")}Id=@Id AND OutstandingAmount=750", target));
        using (var replay = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{capture.DocumentId}/retry", null)) retry.EnsureSuccessStatusCode();
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", payment.PaymentId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}CreditTransactions WHERE SourceDocumentId=@Id", capture.DocumentId));

        // An advance can precede its invoices. One credit line also supports
        // several explicit obligations, without manufacturing cash payments.
        var targets = new List<Guid>();
        Guid? nativeExpenseId = null;
        if (obligationId is { } existing) targets.Add(existing);
        else
        {
            control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                supplier ? "2296" : "1395", supplier ? "Liability" : "Asset", supplier ? "Payable" : "Receivable", supplier ? "Credit" : "Debit");
            var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                supplier ? "5196" : "4196", supplier ? "Expense" : "Revenue", "General", supplier ? "Debit" : "Credit");
            if (supplier)
            {
                using (var profile = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                    new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, false, [], "11001")))
                    profile.EnsureSuccessStatusCode();
                var conceptId = Guid.NewGuid();
                using (var expenseScope = fixture.CreateScope())
                    await expenseScope.ServiceProvider.GetRequiredService<ExpenseService>().SaveConceptAsync(
                        new(fixture.UserId, fixture.TenantId, fixture.BusinessId, new HashSet<string> { ExpensePermissionCodes.Configure }),
                        new(conceptId, fixture.BusinessId, "Concepto automático para cruce", counterpart.AccountId, null, null, true));
                var expense = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId, null,
                    null, Guid.NewGuid().ToString("N"), at.AddDays(1), at.AddDays(30), "COP", "Gasto asistido posterior al anticipo",
                    0, 0, null, null, Lines: [new(null, conceptId, null, "Servicio", 1_000m, null, PurchasingTaxTreatments.CapitalizedCost, null)]);
                using var previewResponse = await client.PostAsJsonAsync("/api/commerce/v1/expenses/preview", expense);
                Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
                var preview = (await previewResponse.Content.ReadFromJsonAsync<ExpensePreview>())!;
                using var expenseMessage = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/expenses/confirm")
                    { Content = JsonContent.Create(expense with { CalculationHash = preview.CalculationHash }) };
                expenseMessage.Headers.Add("Idempotency-Key", expense.ExpenseId.ToString("N"));
                using var expenseResponse = await client.SendAsync(expenseMessage);
                Assert.True(expenseResponse.IsSuccessStatusCode, await expenseResponse.Content.ReadAsStringAsync());
                var expenseAccepted = (await expenseResponse.Content.ReadFromJsonAsync<ExpenseAcceptance>())!;
                var nativePositions = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                    $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&partyId={partyId}&search={Uri.EscapeDataString(expenseAccepted.DocumentNumber)}&page=1&pageSize=2");
                var nativePosition = Assert.Single(nativePositions!.Items);
                Assert.Equal(expense.ExpenseId, nativePosition.SourceDocumentId);
                Assert.Equal(1_000m, nativePosition.OutstandingAmount);
                Assert.Null(nativePosition.OpeningLinkIssue);
                control = Assert.Single((await client.GetFromJsonAsync<AccountingAccountOptionPage>(
                    $"/api/commerce/v1/accounting/account-options?accountId={nativePosition.AccountId}&page=1&pageSize=1"))!.Items);
                targets.Add(nativePosition.SubledgerId);
                obligationSourceId = expense.ExpenseId;
                nativeExpenseId = expense.ExpenseId;
            }
            else
            {
                var invoice = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1),
                    "MANUAL_VOUCHER", "Obligaciones posteriores al anticipo", "APLICAR-ANTICIPO",
                    [new(control.AccountId, partyId, null, "Primera obligación", 500m, 0,
                        "PRIMERA", Guid.NewGuid(), DueDate: at.AddDays(30)),
                     new(control.AccountId, partyId, null, "Segunda obligación", 500m, 0,
                        "SEGUNDA", Guid.NewGuid(), DueDate: at.AddDays(30)),
                     new(counterpart.AccountId, partyId, null, "Contrapartida", 0, 1_000m, LineId: Guid.NewGuid())],
                    null, null, MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
                var invoiced = await SaveAndSendAsync(invoice);
                obligationSourceId = invoice.DocumentId;
                targets.AddRange(invoiced.FinancialEffects!.Positions.Select(item => item.PositionId));
            }
        }
        var positionTable = supplier ? "Payables" : "Receivables";
        var positionKind = supplier ? "Payable" : "Receivable";
        var creditKind = role + "Credit";
        var appliedPerPosition = 300m / targets.Count;
        var crossLines = targets.Select(targetId => new VoucherDraftLine(control!.AccountId, partyId, null,
            "Aplicar anticipo a obligación", supplier ? appliedPerPosition : 0, supplier ? 0 : appliedPerPosition,
            LineId: Guid.NewGuid(), ExistingPosition: new(positionKind, targetId))).ToList();
        crossLines.Add(new(advance.AccountId, partyId, null, "Consumir saldo a favor", supplier ? 0 : 300m,
            supplier ? 300m : 0, LineId: Guid.NewGuid(), ExistingPosition: new(creditKind, opening.PositionId)));
        var cross = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1),
            "MANUAL_VOUCHER", "Aplicación sin dinero", "CRUCE-ANTICIPO", crossLines, null, null,
            MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var crossPath = $"/api/commerce/v1/accounting/manual/drafts/{cross.DocumentId}";
        VoucherDraftView crossSaved;
        using (var save = await client.PutAsJsonAsync(crossPath, cross))
        {
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            crossSaved = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM accounting.SourcePositionIndex WHERE SourceDocumentId=@Id", cross.DocumentId));
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal),
            beforeExecute: command =>
            {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                journal[0]!["AccountId"] = Guid.Empty;
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var send = await client.PostAsJsonAsync(crossPath + "/send", new SendVoucherDraftRequest(crossSaved.RowVersion)))
        {
            Assert.True(send.StatusCode == HttpStatusCode.InternalServerError, await send.Content.ReadAsStringAsync());
            Assert.Equal(1, fault.Count);
        }
        var crossAccepted = (await client.GetFromJsonAsync<VoucherDraftView>(crossPath))!;
        Assert.Empty(crossAccepted.FinancialEffects!.Payments);
        Assert.Equal(targets.Count, crossAccepted.FinancialEffects.CreditApplications.Count);
        Assert.Equal(targets.Count * 2, await CountAsync("SELECT COUNT(*) FROM accounting.SourcePositionIndex WHERE SourceDocumentId=@Id", cross.DocumentId));
        await AssertCrossStateAsync(applied: false);
        if (nativeExpenseId is { } expenseToCancel)
        {
            using var cancellation = await client.PostAsJsonAsync($"/api/commerce/v1/expenses/{expenseToCancel}/cancel",
                new CancelExpenseRequest(Guid.NewGuid(), "Debe respetar el cruce reservado"));
            Assert.Equal(HttpStatusCode.Conflict, cancellation.StatusCode);
            Assert.Contains("pendiente", await cancellation.Content.ReadAsStringAsync());
        }
        Assert.Equal(1, await CountAsync($"""
            SELECT COUNT(*) FROM dbo.{role}Credits credit CROSS APPLY accounting.ObligationAvailability(credit.BusinessId,
              N'{creditKind}',credit.{creditKind}Id,credit.AvailableAmount,NULL) available
            WHERE credit.{creditKind}Id=@Id AND available.PendingApplicationAmount=300 AND available.AvailableAmount=200;
            """, opening.PositionId));

        // A competing money payment sees exactly that same reservation.
        var excessive = (withObligation ? 750m : 1_000m / targets.Count) - appliedPerPosition + 1m;
        var rival = cross with { DocumentId = Guid.NewGuid(), VoucherTypeCode = supplier ? "Disbursement" : "Accounting",
            Description = "Abono que supera el disponible reservado", Lines = [
                new(control!.AccountId, partyId, null, "Abono rival", supplier ? excessive : 0, supplier ? 0 : excessive,
                    LineId: Guid.NewGuid(), ExistingPosition: new(positionKind, targets[0])),
                new(cash.AccountId, partyId, null, "Tesorería rival", supplier ? 0 : excessive, supplier ? excessive : 0,
                    LineId: Guid.NewGuid())] };
        var rivalPath = $"/api/commerce/v1/accounting/manual/drafts/{rival.DocumentId}";
        using (var save = await client.PutAsJsonAsync(rivalPath, rival))
        {
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            var rivalSaved = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using var send = await client.PostAsJsonAsync(rivalPath + "/send", new SendVoucherDraftRequest(rivalSaved.RowVersion));
            Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", rival.DocumentId));

        // A lost original account remains an explicit pending configuration,
        // even after the financial reduction stage; all those effects roll back.
        await using (var missing = new SqlCommand($"UPDATE dbo.{role}Credits SET OpeningEntryId=NULL,OpeningEntryLineNumber=NULL WHERE {creditKind}Id=@Id;", connection))
        {
            missing.Parameters.AddWithValue("@Id", opening.PositionId);
            Assert.Equal(1, await missing.ExecuteNonQueryAsync());
        }
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{cross.DocumentId}/retry", null)) retry.EnsureSuccessStatusCode();
        await AssertCrossStateAsync(applied: false, originalLinked: false);
        Assert.Equal(1, await CountAsync("""
            SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id AND Status=N'AccountingPendingConfiguration'
              AND LastErrorCode=N'OriginalPositionAccountMissing' AND LastErrorMessage LIKE N'No se aplicó%';
            """, cross.DocumentId));
        await using (var restore = new SqlCommand($"UPDATE dbo.{role}Credits SET OpeningEntryId=@Entry,OpeningEntryLineNumber=@Line WHERE {creditKind}Id=@Id;", connection))
        {
            restore.Parameters.AddWithValue("@Id", opening.PositionId); restore.Parameters.AddWithValue("@Entry", creditOpeningEntry);
            restore.Parameters.AddWithValue("@Line", creditOpeningLine);
            Assert.Equal(1, await restore.ExecuteNonQueryAsync());
        }
        using (var cashWrites = PortfolioOriginalAccountScenario.CountApplicationWrites(fixture))
        using (var applications = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
            commandFilter: command => command.CommandText.Contains("The credit application batch is incomplete.", StringComparison.Ordinal)))
        {
            using var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{cross.DocumentId}/retry", null);
            Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
            Assert.Equal(0, cashWrites.Count); Assert.Equal(1, applications.Count);
        }
        await AssertCrossStateAsync(applied: true);
        var crossPosted = (await client.GetFromJsonAsync<VoucherDraftView>(crossPath))!;
        Assert.Equal("Posted", crossPosted.Status);
        Assert.Equal(crossAccepted.FinancialEffects.CreditApplications, crossPosted.FinancialEffects!.CreditApplications);
        foreach (var targetId in targets)
        {
            var detail = await client.GetFromJsonAsync<JsonElement>($"/api/commerce/v1/{(supplier ? "payables" : "receivables")}/{targetId}");
            var applied = Assert.Single(detail.GetProperty("transactions").EnumerateArray(), item =>
                item.GetProperty("type").GetString() == "CreditApplication" && item.GetProperty("sourceDocumentId").GetGuid() == cross.DocumentId);
            Assert.Equal(-appliedPerPosition, applied.GetProperty("balanceChange").GetDecimal());
            Assert.Equal(appliedPerPosition, applied.GetProperty("amount").GetDecimal());
        }
        using (var replay = await client.PostAsJsonAsync(crossPath + "/send", new SendVoucherDraftRequest(crossSaved.RowVersion))) replay.EnsureSuccessStatusCode();
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{cross.DocumentId}/retry", null)) retry.EnsureSuccessStatusCode();
        await AssertCrossStateAsync(applied: true);

        if (supplier && !withObligation)
        {
            var anotherCredit = await SaveAndSendAsync(capture with { DocumentId = Guid.NewGuid(), OccurredAt = at.AddDays(2),
                Description = "Otro anticipo del mismo tercero y auxiliar", Lines = [
                    new(advance.AccountId, partyId, null, "Otro anticipo", 100m, 0m, LineId: Guid.NewGuid()),
                    new(cash.AccountId, partyId, null, "Salida real independiente", 0m, 100m, LineId: Guid.NewGuid())] });
            var wrongCreditId = Assert.Single(anotherCredit.FinancialEffects!.Positions).PositionId;
            var originalApplication = Assert.Single(crossPosted.FinancialEffects!.CreditApplications);
            var hijacked = cross with { DocumentId = Guid.NewGuid(), OccurredAt = at.AddDays(3),
                Description = "No restituir un crédito distinto aunque coincidan cuenta y tercero", Lines = [
                    new(control!.AccountId, partyId, null, "Reabrir obligación", 0m, 1m, LineId: Guid.NewGuid(),
                        ExistingPosition: new(positionKind, originalApplication.PositionId, originalApplication.EffectId)),
                    new(advance.AccountId, partyId, null, "Crédito incorrecto", 1m, 0m, LineId: Guid.NewGuid(),
                        ExistingPosition: new(creditKind, wrongCreditId))] };
            var badDraft = await SaveDraftAsync(hijacked);
            using (var send = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{hijacked.DocumentId}/send", new SendVoucherDraftRequest(badDraft.RowVersion)))
            {
                Assert.Equal(HttpStatusCode.BadRequest, send.StatusCode);
                Assert.Contains("No se desaplicó", await send.Content.ReadAsStringAsync());
            }
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", hijacked.DocumentId));
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.SupplierCredits WHERE SupplierCreditId=@Id AND AvailableAmount=100", wrongCreditId));
        }

        // Undo the original crossing in two parts, restoring that same credit.
        // This covers supplier/customer and customer positions without a role.
        for (var part = 0; part < 2; part++)
        {
            var originalApplication = crossPosted.FinancialEffects.CreditApplications[0];
            if (part == 0)
            {
                // A legal entity can have only LegalName. Missing DisplayName
                // must not make the original-facts reader throw for that party.
                await using var names = (SqlTransaction)await connection.BeginTransactionAsync();
                try
                {
                    await using var command = new SqlCommand("""
                        UPDATE dbo.Parties SET LegalName=COALESCE(LegalName,DisplayName),DisplayName=NULL WHERE PartyId=@PartyId;
                        SELECT COUNT(*) FROM accounting.AppliedPortfolioSettlements original JOIN dbo.Parties party ON party.PartyId=original.PartyId
                        WHERE original.TransactionId=@TransactionId AND original.SubledgerKind=@Kind AND original.PartyName=party.LegalName;
                        """, connection, names);
                    command.Parameters.AddWithValue("@PartyId", partyId); command.Parameters.AddWithValue("@TransactionId", originalApplication.EffectId);
                    command.Parameters.AddWithValue("@Kind", positionKind);
                    Assert.Equal(1, (int)(await command.ExecuteScalarAsync())!);
                }
                finally { await names.RollbackAsync(); }
            }
            var capturePath = $"/api/commerce/v1/accounting/unapplication-capture?subledgerKind={positionKind}&subledgerId={originalApplication.PositionId}&transactionId={originalApplication.EffectId}";
            var preparation = (await client.GetFromJsonAsync<VoucherDraftPreparation>(capturePath))!;
            Assert.Equal(new(creditKind, opening.PositionId), preparation.Lines[1].Value.ExistingPosition);
            Assert.Equal(advance.AccountId, preparation.Lines[1].Value.AccountId);
            Assert.Equal(appliedPerPosition / (part == 0 ? 1m : 2m), supplier ? preparation.Lines[0].Value.Credit : preparation.Lines[0].Value.Debit);
            var restoreLines = crossPosted.FinancialEffects.CreditApplications.Select(application =>
                new VoucherDraftLine(control!.AccountId, partyId, null, "Reabrir obligación del cruce", supplier ? 0 : application.Amount / 2,
                    supplier ? application.Amount / 2 : 0, LineId: Guid.NewGuid(),
                    ExistingPosition: new(positionKind, application.PositionId, application.EffectId))).ToList();
            restoreLines.Add(new(advance.AccountId, partyId, null, "Restituir crédito original", supplier ? 150m : 0,
                supplier ? 0 : 150m, LineId: Guid.NewGuid(), ExistingPosition: new(creditKind, opening.PositionId)));
            var restoreCapture = cross with { DocumentId = Guid.NewGuid(), OccurredAt = at.AddDays(2 + part),
                Description = "Desaplicar cruce sin otro pago ni crédito", Lines = restoreLines };
            var restorePath = $"/api/commerce/v1/accounting/manual/drafts/{restoreCapture.DocumentId}";
            var restoreSaved = await SaveDraftAsync(restoreCapture);
            if (part == 0)
            {
                using (var fault = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal),
                    beforeExecute: command =>
                    {
                        var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                        journal[0]!["AccountId"] = Guid.Empty;
                        command.Parameters["@Lines"].Value = journal.ToJsonString();
                    }))
                using (var send = await client.PostAsJsonAsync(restorePath + "/send", new SendVoucherDraftRequest(restoreSaved.RowVersion)))
                {
                    Assert.Equal(HttpStatusCode.InternalServerError, send.StatusCode);
                    Assert.Equal(1, fault.Count);
                }
                await AssertCrossStateAsync(applied: true);
                Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}CreditTransactions WHERE SourceDocumentId=@Id", restoreCapture.DocumentId));
                Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{positionKind}Transactions WHERE SourceDocumentId=@Id", restoreCapture.DocumentId));
                Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", restoreCapture.DocumentId));
                var reserved = (await client.GetFromJsonAsync<VoucherDraftPreparation>(capturePath))!;
                Assert.Equal(appliedPerPosition / 2, supplier ? reserved.Lines[0].Value.Credit : reserved.Lines[0].Value.Debit);
                var excessiveRestore = restoreCapture with { DocumentId = Guid.NewGuid(),
                    Lines = restoreLines.Select(line => line with { Debit = line.Debit * 2, Credit = line.Credit * 2 }).ToArray() };
                var excessSaved = await SaveDraftAsync(excessiveRestore);
                using (var send = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{excessiveRestore.DocumentId}/send", new SendVoucherDraftRequest(excessSaved.RowVersion)))
                    Assert.Equal(HttpStatusCode.Conflict, send.StatusCode);
                Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", excessiveRestore.DocumentId));
                using var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{restoreCapture.DocumentId}/retry", null);
                Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
            }
            else
            {
                using var send = await client.PostAsJsonAsync(restorePath + "/send", new SendVoucherDraftRequest(restoreSaved.RowVersion));
                Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
            }
            using (var replay = await client.PostAsJsonAsync(restorePath + "/send", new SendVoucherDraftRequest(restoreSaved.RowVersion))) replay.EnsureSuccessStatusCode();
            using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{restoreCapture.DocumentId}/retry", null)) retry.EnsureSuccessStatusCode();
            var restored = (await client.GetFromJsonAsync<VoucherDraftView>(restorePath))!;
            Assert.Equal("Posted", restored.Status);
            Assert.Empty(restored.FinancialEffects!.Positions);
            Assert.Empty(restored.FinancialEffects.Payments);
            Assert.Equal(targets.Count, restored.FinancialEffects.Unapplications!.Count);
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE {creditKind}Id=@Id AND AvailableAmount={350 + part * 150}", opening.PositionId));
            Assert.Equal(targets.Count, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}CreditTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Unapplication' AND ReversesTransactionId IS NOT NULL", restoreCapture.DocumentId));
            Assert.Equal(targets.Count, await CountAsync($"""
                SELECT COUNT(*) FROM dbo.{role}CreditTransactions restored
                JOIN dbo.{role}CreditTransactions original ON original.{creditKind}TransactionId=restored.ReversesTransactionId
                  AND original.{creditKind}Id=restored.{creditKind}Id AND original.TransactionType=N'Application'
                JOIN dbo.{positionKind}Transactions debt ON debt.{positionKind}TransactionId=restored.AppliedPositionTransactionId
                  AND debt.ReversesTransactionId=original.AppliedPositionTransactionId AND debt.TransactionType=N'CreditUnapplication'
                WHERE restored.SourceDocumentId=@Id;
                """, restoreCapture.DocumentId));
            Assert.Equal(targets.Count, await CountAsync($"SELECT COUNT(*) FROM dbo.{positionKind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'CreditUnapplication'", restoreCapture.DocumentId));
            Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE SourceDocumentId=@Id", restoreCapture.DocumentId));
            Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Payments WHERE ParentSourceDocumentId=@Id", restoreCapture.DocumentId));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", restoreCapture.DocumentId));
            Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", restoreCapture.DocumentId));
        }
        var reapplied = await SaveAndSendAsync(cross with { DocumentId = Guid.NewGuid(), OccurredAt = at.AddDays(4), Description = "Volver a aplicar el crédito restituido" });
        await AssertCrossStateAsync(applied: true);
        var competingOriginal = reapplied.FinancialEffects!.CreditApplications[0];
        var competitors = new List<VoucherDraftView>();
        for (var index = 0; index < 2; index++)
            competitors.Add(await SaveDraftAsync(cross with { DocumentId = Guid.NewGuid(), OccurredAt = at.AddDays(5),
                Description = "Restituciones concurrentes del mismo movimiento", Lines = [
                    new(control!.AccountId, partyId, null, "Reabrir obligación", supplier ? 0 : competingOriginal.Amount,
                        supplier ? competingOriginal.Amount : 0, LineId: Guid.NewGuid(),
                        ExistingPosition: new(positionKind, competingOriginal.PositionId, competingOriginal.EffectId)),
                    new(advance.AccountId, partyId, null, "Restituir el mismo crédito", supplier ? competingOriginal.Amount : 0,
                        supplier ? 0 : competingOriginal.Amount, LineId: Guid.NewGuid(), ExistingPosition: new(creditKind, opening.PositionId))] }));
        var concurrent = await Task.WhenAll(competitors.Select(draft => client.PostAsJsonAsync(
            $"/api/commerce/v1/accounting/manual/drafts/{draft.DocumentId}/send", new SendVoucherDraftRequest(draft.RowVersion))));
        try
        {
            Assert.Single(concurrent, response => response.IsSuccessStatusCode);
            Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in concurrent) response.Dispose(); }
        Assert.Equal(1, await CountAsync(FormattableString.Invariant($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE {creditKind}Id=@Id AND AvailableAmount={200m + competingOriginal.Amount}"), opening.PositionId));

        async Task AssertCrossStateAsync(bool applied, bool originalLinked = true)
        {
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE {creditKind}Id=@Id AND AvailableAmount={(applied ? 200 : 500)}", opening.PositionId));
            Assert.Equal(applied ? targets.Count : 0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}CreditTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Application'", cross.DocumentId));
            Assert.Equal(applied ? targets.Count : 0, await CountAsync($"SELECT COUNT(*) FROM dbo.{positionKind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'CreditApplication'", cross.DocumentId));
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM(SELECT SUM(OutstandingAmount) Amount FROM dbo.{positionTable} WHERE SourceDocumentId=@Id) totals WHERE Amount={(withObligation ? 750 : 1000) - (applied ? 300 : 0)}", obligationSourceId!.Value));
            Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Payments WHERE ParentSourceDocumentId=@Id", cross.DocumentId));
            Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", cross.DocumentId));
            Assert.Equal(applied ? 1 : 0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", cross.DocumentId));
            Assert.Equal(1, await CountAsync($"""
                SELECT COUNT(*) FROM dbo.{role}Credits credit CROSS APPLY accounting.ObligationAvailability(credit.BusinessId,
                  N'{creditKind}',credit.{creditKind}Id,credit.AvailableAmount,NULL) available
                WHERE credit.{creditKind}Id=@Id AND available.PendingApplicationAmount={(applied ? 0 : 300)};
                """, opening.PositionId));
            var accountFilter = originalLinked ? $"&accountId={advance.AccountId}" : "";
            var positionPage = await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind={creditKind}&partyId={partyId}&subledgerId={opening.PositionId}&page=1&pageSize=1{accountFilter}");
            var creditPosition = Assert.Single(positionPage!.Items);
            Assert.Equal(1, positionPage.TotalCount);
            Assert.Equal("Advance", creditPosition.CreditNature);
            Assert.Equal("COP", creditPosition.CurrencyCode);
            Assert.Null(creditPosition.DueDate);
            if (originalLinked) { Assert.Null(creditPosition.OpeningLinkIssue); Assert.Equal(advance.AccountId, creditPosition.AccountId); }
            else { Assert.NotNull(creditPosition.OpeningLinkIssue); Assert.Null(creditPosition.AccountId); }
            Assert.Equal(200m, creditPosition.AvailableAmount);
            Assert.Equal(applied ? 0m : 300m, creditPosition.PendingApplicationAmount);
            Assert.Equal(capture.DocumentId, creditPosition.SourceDocumentId);
        }

        async Task<VoucherDraftView> SaveAndSendAsync(SaveVoucherDraftRequest input)
        {
            var draft = await SaveDraftAsync(input);
            var documentPath = $"/api/commerce/v1/accounting/manual/drafts/{input.DocumentId}";
            using var send = await client.PostAsJsonAsync(documentPath + "/send", new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
            return (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }

        async Task<VoucherDraftView> SaveDraftAsync(SaveVoucherDraftRequest input)
        {
            using var save = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{input.DocumentId}", input);
            Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
            return (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
    }

    private async Task<Guid> CreateCustomerPartyAsync(bool withRole)
    {
        var id = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@Id,@TenantId,N'Organization',N'Tercero de anticipo',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            IF @WithRole=1
            BEGIN
              INSERT dbo.Customers(CustomerId,PartyId,TenantId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
              VALUES(NEWID(),@Id,@TenantId,0,1,@UserId,SYSDATETIMEOFFSET());
              INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,AddressLine,IsPrimary,IsActive,CreatedBy,CreatedAt)
              SELECT TOP(1) NEWID(),@Id,N'PRINCIPAL',N'Principal',CountryId,AdministrativeDivisionId,CityId,AddressLine,1,1,@UserId,SYSDATETIMEOFFSET()
              FROM dbo.PartySites WHERE PartyId=@SupplierPartyId AND IsPrimary=1 AND IsActive=1 ORDER BY PartySiteId;
            END;
            """, connection);
        command.Parameters.AddWithValue("@Id", id); command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId); command.Parameters.AddWithValue("@WithRole", withRole);
        command.Parameters.AddWithValue("@SupplierPartyId", fixture.SupplierPartyId);
        await command.ExecuteNonQueryAsync();
        return id;
    }

    private async Task<int> CountAsync(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
