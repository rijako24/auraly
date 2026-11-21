using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Payables;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class PortfolioManualAdvanceTests
{
    [Theory]
    [InlineData(false, false, 1, false)]
    [InlineData(true, false, 1, false)]
    [InlineData(false, true, 1, false)]
    [InlineData(true, true, 1, false)]
    [InlineData(false, false, 498, false)]
    [InlineData(true, false, 498, false)]
    [InlineData(false, false, 1, true)]
    [InlineData(true, false, 1, true)]
    public async Task Explicit_invoice_reductions_share_reservations_atomic_posting_and_replay(bool supplier, bool adjustmentOnly, int conceptLines, bool useCredit)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Retry, CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            ReceivablesPermissionCodes.RegisterPayment, PayablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) active.EnsureSuccessStatusCode();
        var party = supplier ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(true);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "2292" : "1392", supplier ? "Liability" : "Asset", supplier ? "Payable" : "Receivable", supplier ? "Credit" : "Debit");
        var general = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "4192" : "5192", supplier ? "Revenue" : "Expense", "General", supplier ? "Credit" : "Debit");
        var sides = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/accounting-balance-side"))!;
        var concept = new SaveFinancialConceptRequest(Guid.NewGuid(), $"RED-{Guid.NewGuid():N}", "Reducción elegida",
            general.AccountId, sides.Single(item => item.Code == (supplier ? "Credit" : "Debit")).Id, !supplier, supplier, true, true, 10);
        using (var savedConcept = await client.PutAsJsonAsync($"/api/commerce/v1/financial-concepts/{concept.FinancialConceptId}", concept))
            savedConcept.EnsureSuccessStatusCode();
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var invoice = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Obligación antes de reducción", "OBL-RED",
            [new(control.AccountId, party, null, "Obligación", supplier ? 0 : 100, supplier ? 100 : 0,
                "OBL-RED", Guid.NewGuid(), DueDate: at.AddDays(30)),
             new(general.AccountId, party, null, "Contrapartida", supplier ? 100 : 0, supplier ? 0 : 100, LineId: Guid.NewGuid())],
            null, null, MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var invoiceDraft = await Save(invoice);
        using var invoiceResponse = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{invoice.DocumentId}/send", new SendVoucherDraftRequest(invoiceDraft.RowVersion));
        Assert.True(invoiceResponse.IsSuccessStatusCode, await invoiceResponse.Content.ReadAsStringAsync());
        var opened = (await invoiceResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        var obligation = Assert.Single(opened.FinancialEffects!.Positions);
        var group = Guid.NewGuid(); var targetLineId = Guid.NewGuid(); var reduction = adjustmentOnly ? 100m : 10m;
        Guid? creditId = null;
        VoucherDraftLine[] settlement = adjustmentOnly ? [] : [new(cash.AccountId, party, null, "Dinero efectivo", supplier ? 0 : 90, supplier ? 90 : 0,
            LineId: Guid.NewGuid(), FinancialGroupId: group)];
        if (useCredit)
        {
            var advance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                supplier ? "1393" : "2393", supplier ? "Asset" : "Liability", supplier ? "SupplierAdvance" : "CustomerAdvance", supplier ? "Debit" : "Credit");
            var advanceCapture = invoice with { DocumentId = Guid.NewGuid(), VoucherTypeCode = supplier ? "Disbursement" : "CashReceipt",
                Lines = [new(advance.AccountId, party, null, "Anticipo elegido", supplier ? 90 : 0, supplier ? 0 : 90, LineId: Guid.NewGuid()),
                    new(cash.AccountId, party, null, "Dinero del anticipo", supplier ? 0 : 90, supplier ? 90 : 0, LineId: Guid.NewGuid())] };
            var advanceDraft = await Save(advanceCapture);
            using var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{advanceCapture.DocumentId}/send", new SendVoucherDraftRequest(advanceDraft.RowVersion));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            var advancePosted = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", advancePosted.Status);
            creditId = Assert.Single(advancePosted.FinancialEffects!.Positions).PositionId;
            settlement = [new(advance.AccountId, party, null, "Anticipo existente", supplier ? 0 : 90, supplier ? 90 : 0,
                LineId: Guid.NewGuid(), FinancialGroupId: group, ExistingPosition: new(supplier ? "SupplierCredit" : "CustomerCredit", creditId.Value))];
        }
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Reducción y saldo realmente pagado", "RED-EXPLICITA",
            [new(control.AccountId, party, null, "Documento ajustado", supplier ? 100 : 0, supplier ? 0 : 100,
                LineId: targetLineId, ExistingPosition: new(supplier ? "Payable" : "Receivable", obligation.PositionId), FinancialGroupId: group),
             .. Enumerable.Range(0, conceptLines).Select(index => new VoucherDraftLine(general.AccountId, party, null, "Concepto explícito",
                supplier ? 0 : index == 0 ? reduction - (conceptLines - 1) * .01m : .01m,
                supplier ? index == 0 ? reduction - (conceptLines - 1) * .01m : .01m : 0,
                LineId: Guid.NewGuid(), FinancialGroupId: group, FinancialConceptId: concept.FinancialConceptId, FinancialConceptTargetLineId: targetLineId)),
             .. settlement],
            null, null, MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: supplier ? "Disbursement" : "CashReceipt");
        var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        var saved = await Save(capture);
        Assert.Equal(targetLineId, saved.Lines[1].Value.FinancialConceptTargetLineId);
        string? timedOutStatement = null;
        using var sqlFailures = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            onCommandError: (command, error) => {
                if (error is SqlException { Number: -2 }) timedOutStatement = command.CommandText[..Math.Min(1000, command.CommandText.Length)];
            });
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
            beforeExecute: command => {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                journal.Single(line => line!["SourceLineId"]!.GetValue<Guid>() == capture.Lines[1].LineId)!["Debit"] = -1m;
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var failed = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            var unexpected = failed.IsSuccessStatusCode ? await failed.Content.ReadFromJsonAsync<VoucherDraftView>() : null;
            Assert.True(failed.StatusCode == HttpStatusCode.InternalServerError,
                $"Unexpected status {failed.StatusCode}: {unexpected?.Row.ErrorCode} {unexpected?.Row.ErrorMessage}. SQL timeout: {timedOutStatement}");
            Assert.True(fault.Count == 1, $"The journal fault was not reached. SQL timeout: {timedOutStatement}");
        }
        var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        Assert.Equal(adjustmentOnly || useCredit ? "Accounting" : capture.VoucherTypeCode, accepted.VoucherTypeCode);
        Assert.Equal(conceptLines, accepted.FinancialEffects!.Positions.Count);
        Assert.Equal(reduction, accepted.FinancialEffects.Positions.Sum(item => item.Amount));
        Assert.All(accepted.FinancialEffects.Positions, item => { Assert.Equal("Adjustment", item.EffectKind); Assert.Equal(targetLineId, item.TargetSourceLineId); });
        Assert.Equal(adjustmentOnly || useCredit ? 0 : 1, accepted.FinancialEffects.Payments.Count);
        Assert.Equal(useCredit ? 1 : 0, accepted.FinancialEffects.CreditApplications.Count);
        var kind = supplier ? "Payable" : "Receivable"; var role = supplier ? "Supplier" : "Customer";
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}s WHERE {kind}Id=@Id AND OutstandingAmount=100", obligation.PositionId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM accounting.ObligationAvailability('{fixture.BusinessId}',N'{kind}',@Id,100,NULL) WHERE AvailableAmount=0 AND PendingAdjustmentAmount={reduction} AND PendingPaymentAmount={(useCredit ? 0 : 100 - reduction)} AND PendingApplicationAmount={(useCredit ? 90 : 0)}", obligation.PositionId));
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@Id", capture.DocumentId));
        var competing = capture with { DocumentId = Guid.NewGuid(), Reference = "RED-RESERVA-OCUPADA" };
        var competingDraft = await Save(competing);
        using (var rejected = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{competing.DocumentId}/send", new SendVoucherDraftRequest(competingDraft.RowVersion)))
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", competing.DocumentId));
        using (var batches = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @Adjustments TABLE", StringComparison.Ordinal)))
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{capture.DocumentId}/retry", null))
        {
            Assert.True(retry.IsSuccessStatusCode, $"{await retry.Content.ReadAsStringAsync()}. SQL timeout: {timedOutStatement}"); Assert.Equal(1, batches.Count);
        }
        using (var replay = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(accepted.RowVersion)))
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}s WHERE {kind}Id=@Id AND OutstandingAmount=0", obligation.PositionId));
        Assert.Equal(conceptLines, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'Adjustment' AND SourceLineId IS NOT NULL AND SourceDocumentType=N'ManualAccountingVoucher' AND Amount<0", capture.DocumentId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM accounting.ObligationAvailability('{fixture.BusinessId}',N'{kind}',@Id,0,NULL) WHERE AvailableAmount=0 AND PendingAdjustmentAmount=0 AND PendingPaymentAmount=0", obligation.PositionId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", capture.DocumentId));
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE SourceDocumentId=@Id", capture.DocumentId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", capture.DocumentId));
        if (creditId is { } usedCredit)
        {
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE {role}CreditId=@Id AND AvailableAmount=0", usedCredit));
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'CreditApplication' AND Amount=90", capture.DocumentId));
        }
        foreach (var payment in accepted.FinancialEffects.Payments)
        {
            Assert.Equal(90, Assert.Single(payment.Allocations).Amount); Assert.Equal(90, Assert.Single(payment.Tenders).Amount);
            Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Payments WHERE PaymentId=@Id AND TotalAmount=90 AND Status=N'Processed'", payment.PaymentId));
        }
        if (adjustmentOnly)
        {
            using var scope = fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
            var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
            foreach (var direction in new[] { AccountingAdjustmentDirections.Increase, AccountingAdjustmentDirections.Decrease })
            {
                var individual = new ConfirmAccountAdjustmentRequest(Guid.NewGuid(), fixture.BusinessId, kind, obligation.PositionId,
                    direction, 5m, general.AccountId, null, at, "ACCOUNT_ADJUSTMENT", "Ajuste individual compatible");
                var result = await service.ConfirmAccountAdjustmentAsync(user, individual, CancellationToken.None);
                Assert.Equal(individual.AdjustmentId, result.DocumentId);
                await service.ConfirmAccountAdjustmentAsync(user, individual, CancellationToken.None);
                Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'Adjustment' AND Amount={(direction == AccountingAdjustmentDirections.Increase ? 5 : -5)} AND SourceDocumentType IS NULL AND SourceLineId IS NULL", individual.AdjustmentId));
                Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}s WHERE {kind}Id=@Id AND OutstandingAmount={(direction == AccountingAdjustmentDirections.Increase ? 5 : 0)}", obligation.PositionId));
            }
        }

        async Task<VoucherDraftView> Save(SaveVoucherDraftRequest request)
        {
            using var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{request.DocumentId}", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
    }

    [Theory]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 498, true)]
    [InlineData(true, 498, true)]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 498, false)]
    [InlineData(true, 498, false)]
    public async Task Invoice_and_explicit_additional_concept_share_money_application_rollback_and_replay(bool supplier, int conceptLines, bool catalogueConcept)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            AccountingPermissionCodes.Retry, CatalogPermissionCodes.Read, CatalogPermissionCodes.Update,
            ReceivablesPermissionCodes.RegisterPayment, PayablesPermissionCodes.RegisterPayment);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var active = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) active.EnsureSuccessStatusCode();
        var party = supplier ? fixture.SupplierPartyId : await CreateCustomerPartyAsync(true);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "2291" : "1391", supplier ? "Liability" : "Asset", supplier ? "Payable" : "Receivable", supplier ? "Credit" : "Debit");
        var general = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            supplier ? "5191" : "4191", supplier ? "Expense" : "Revenue", "General", supplier ? "Debit" : "Credit");
        var sides = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/accounting-balance-side"))!;
        var concept = new SaveFinancialConceptRequest(Guid.NewGuid(), $"EXTRA-{Guid.NewGuid():N}", "Ajuste sobrante elegido",
            general.AccountId, sides.Single(item => item.Code == (supplier ? "Debit" : "Credit")).Id, !supplier, supplier, true, true, 10);
        if (catalogueConcept)
        using (var savedConcept = await client.PutAsJsonAsync($"/api/commerce/v1/financial-concepts/{concept.FinancialConceptId}", concept))
            savedConcept.EnsureSuccessStatusCode();
        var at = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        const decimal invoiceAmount = 216_748.03m;
        var invoice = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Obligación antes del concepto", "OBL-CONCEPTO",
            [new(control.AccountId, party, null, "Obligación", supplier ? 0 : invoiceAmount, supplier ? invoiceAmount : 0,
                "OBL-CONCEPTO", Guid.NewGuid(), DueDate: at.AddDays(30)),
             new(general.AccountId, party, null, "Contrapartida", supplier ? invoiceAmount : 0, supplier ? 0 : invoiceAmount, LineId: Guid.NewGuid())],
            null, null, MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var opened = await SaveAndSend(invoice);
        var obligation = Assert.Single(opened.FinancialEffects!.Positions);
        var group = Guid.NewGuid(); var session = await fixture.OpenWebWorkSessionAsync();
        var additional = 51.97m + (conceptLines - 1) * .01m;
        var money = invoiceAmount + additional;
        var moneySql = money.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Factura y concepto en un solo movimiento", "RC-CONCEPTO-MIXTO",
            [new(control.AccountId, party, null, "Abono a factura", supplier ? invoiceAmount : 0, supplier ? 0 : invoiceAmount,
                LineId: Guid.NewGuid(), ExistingPosition: new(supplier ? "Payable" : "Receivable", obligation.PositionId), FinancialGroupId: group),
             .. Enumerable.Range(0, conceptLines).Select(index => new VoucherDraftLine(general.AccountId, party, null, "Ajuste explícito",
                supplier ? index == 0 ? 51.97m : .01m : 0, supplier ? 0 : index == 0 ? 51.97m : .01m,
                LineId: Guid.NewGuid(), FinancialGroupId: group, FinancialConceptId: catalogueConcept ? concept.FinancialConceptId : null)),
             new(cash.AccountId, party, null, "Dinero efectivo", supplier ? 0 : money, supplier ? money : 0,
                LineId: Guid.NewGuid(), FinancialGroupId: group)],
            null, null, MainPartyId: party, PayloadVersion: 4, VoucherTypeCode: supplier ? "Disbursement" : "CashReceipt", WorkSessionId: session);
        var path = $"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        var saved = await Save(capture);
        using (var tamper = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText == "accounting.PortfolioPaymentAccept",
            beforeExecute: command => {
                var payload = JsonNode.Parse((string)command.Parameters["@Payload"].Value)!;
                payload["groups"]![0]!["additionalConceptAmount"] = additional + 1m;
                command.Parameters["@Payload"].Value = payload.ToJsonString();
            }))
        using (var rejected = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Equal(1, tamper.Count);
        }
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", capture.DocumentId));
        using (var dimensions = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @RequestedConcepts TABLE", StringComparison.Ordinal)))
        using (var acceptance = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText == "accounting.PortfolioPaymentAccept"))
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @OpeningLines TABLE", StringComparison.Ordinal),
            beforeExecute: command => {
                var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                journal.Single(line => line!["SourceLineId"]!.GetValue<Guid>() == capture.Lines[1].LineId)!["Debit"] = -1m;
                command.Parameters["@Lines"].Value = journal.ToJsonString();
            }))
        using (var failed = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion)))
        {
            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode); Assert.Equal(1, fault.Count);
            Assert.Equal(1, dimensions.Count); Assert.Equal(1, acceptance.Count);
        }
        var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path))!;
        var payment = Assert.Single(accepted.FinancialEffects!.Payments);
        Assert.Equal(additional, payment.AdditionalConceptAmount);
        Assert.Equal(capture.Lines.Skip(1).Take(conceptLines).Select(line => line.LineId!.Value), payment.AdditionalSourceLineIds);
        if (!catalogueConcept) Assert.Null(accepted.FinancialEffects.Concepts);
        Assert.Equal(invoiceAmount, Assert.Single(payment.Allocations).Amount);
        Assert.Equal(money, Assert.Single(payment.Tenders).Amount);
        Assert.Empty(accepted.FinancialEffects.Positions); Assert.Empty(accepted.FinancialEffects.DirectMovements);
        var role = supplier ? "Supplier" : "Customer"; var kind = supplier ? "Payable" : "Receivable";
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}s WHERE {kind}Id=@Id AND OutstandingAmount=216748.03", obligation.PositionId));
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}PaymentApplications WHERE PaymentId=@Id AND AppliedAt IS NOT NULL", payment.PaymentId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", payment.PaymentId));
        using (var application = new InventoryBalanceProcessingTests.CommandCounter(
            new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
            commandFilter: command => command.CommandText.Contains("DECLARE @Payments TABLE", StringComparison.Ordinal)))
        using (var retry = await client.PostAsync($"/api/commerce/v1/accounting/postings/{capture.DocumentId}/retry", null))
        {
            Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
            Assert.Equal(1, application.Count);
        }
        using (var replay = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(accepted.RowVersion)))
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}s WHERE {kind}Id=@Id AND OutstandingAmount=0", obligation.PositionId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Payments WHERE PaymentId=@Id AND TotalAmount={moneySql} AND Status=N'Processed'", payment.PaymentId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}PaymentTenders WHERE PaymentId=@Id AND Amount={moneySql}", payment.PaymentId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.{kind}Transactions WHERE SourceDocumentId=@Id AND TransactionType=N'Payment' AND Amount=216748.03", payment.PaymentId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id AND Amount={(supplier ? "-" : "")}{moneySql}", payment.PaymentId));
        Assert.Equal(0, await CountAsync($"SELECT COUNT(*) FROM dbo.{role}Credits WHERE SourceDocumentId=@Id", capture.DocumentId));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", capture.DocumentId));
        Assert.Equal(0, await CountAsync("SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id", payment.PaymentId));
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId WHERE entry.SourceDocumentId=@Id AND line.SourceLineId='{capture.Lines[1].LineId}' AND line.AccountId='{general.AccountId}' AND line.{(supplier ? "Debit" : "Credit")}=51.97", capture.DocumentId));

        async Task<VoucherDraftView> Save(SaveVoucherDraftRequest request)
        {
            using var response = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{request.DocumentId}", request);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        async Task<VoucherDraftView> SaveAndSend(SaveVoucherDraftRequest request)
        {
            var draft = await Save(request);
            using var response = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{request.DocumentId}/send", new SendVoucherDraftRequest(draft.RowVersion));
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
    }

    [Theory]
    [InlineData(false,false)]
    [InlineData(false,true)]
    [InlineData(true,false)]
    [InlineData(true,true)]
    public async Task Selected_financial_concepts_share_receipt_and_egress_posting_and_preserve_accepted_evidence(bool supplier,bool advance)
    {
        using var client=fixture.CreateAdminClient(AccountingPermissionCodes.Read,AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate,AccountingPermissionCodes.ManualCreate,AccountingPermissionCodes.ManualSend,
            CatalogPermissionCodes.Read,CatalogPermissionCodes.Update,ReceivablesPermissionCodes.RegisterPayment,PayablesPermissionCodes.RegisterPayment);
        using(var defaults=await client.PutAsync("/api/commerce/v1/accounting/defaults",null))
            Assert.True(defaults.IsSuccessStatusCode,await defaults.Content.ReadAsStringAsync());
        using(var active=await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",new ActivateAccountingRequest(new(2026,1,1),"COP","ZeroDeclared")))
            Assert.True(active.IsSuccessStatusCode,await active.Content.ReadAsStringAsync());
        var cash=await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture,client);
        var counterpart=await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture,client,
            advance?supplier?"1392":"2392":supplier?"5192":"4192",
            advance?supplier?"Asset":"Liability":supplier?"Expense":"Revenue",
            advance?supplier?"SupplierAdvance":"CustomerAdvance":"General",supplier?"Debit":"Credit");
        var sides=(await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/accounting-balance-side"))!;
        var configuration=new SaveFinancialConceptRequest(Guid.NewGuid(),$"CONCEPT-{Guid.NewGuid():N}",
            advance?"Anticipo expresamente seleccionado":"Anticipo en el nombre, cuenta general",counterpart.AccountId,
            sides.Single(value=>value.Code==(supplier?"Debit":"Credit")).Id,!supplier,supplier,true,true,10);
        var configurationPath=$"/api/commerce/v1/financial-concepts/{configuration.FinancialConceptId}";
        using var save=await client.PutAsJsonAsync(configurationPath,configuration);
        Assert.True(save.IsSuccessStatusCode,await save.Content.ReadAsStringAsync());
        var created=(await save.Content.ReadFromJsonAsync<FinancialConceptSaveResult>())!.Concept;
        using(var unauthorized=fixture.CreateAdminClient(AccountingPermissionCodes.Read))
        using(var forbidden=await unauthorized.PutAsJsonAsync(configurationPath,configuration))
            Assert.Equal(HttpStatusCode.Forbidden,forbidden.StatusCode);
        using(var invalid=await client.PutAsJsonAsync($"/api/commerce/v1/financial-concepts/{Guid.Empty}",configuration))
            Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        var bad=configuration with {FinancialConceptId=Guid.NewGuid(),Code=$"BAD-{Guid.NewGuid():N}",AccountId=cash.AccountId};
        using(var wrongKind=await client.PutAsJsonAsync($"/api/commerce/v1/financial-concepts/{bad.FinancialConceptId}",bad))
            Assert.Equal(HttpStatusCode.BadRequest,wrongKind.StatusCode);
        using var scope=fixture.CreateScope();
        var catalog=scope.ServiceProvider.GetRequiredService<FinancialConceptService>();
        var user=new CatalogUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{AccountingPermissionCodes.Read});
        using(var reads=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
        {
            var options=await catalog.ListAsync(user,new(Usage:supplier?"SupplierPayments":"Receipts",FinancialConceptId:created.FinancialConceptId));
            Assert.Equal(created.FinancialConceptId,Assert.Single(options.Items).FinancialConceptId);Assert.Equal(1,reads.Count);
        }
        Assert.Empty((await catalog.ListAsync(user with {TenantId=Guid.NewGuid()},new(FinancialConceptId:created.FinancialConceptId))).Items);
        var party=supplier?fixture.SupplierPartyId:await CreateCustomerPartyAsync(false);
        var group=Guid.NewGuid();var conceptLine=Guid.NewGuid();
        var capture=new SaveVoucherDraftRequest(Guid.NewGuid(),AccountingManualDocumentTypes.ManualVoucher,
            new(2026,8,10,12,0,0,TimeSpan.Zero),"MANUAL_VOUCHER","Concepto financiero explícito","RC-CONCEPTO",
            [new(cash.AccountId,party,null,"Dinero",supplier?0:500,supplier?500:0,LineId:Guid.NewGuid(),FinancialGroupId:group),
             new(counterpart.AccountId,party,null,"Concepto",supplier?500:0,supplier?0:500,LineId:conceptLine,FinancialGroupId:group,FinancialConceptId:created.FinancialConceptId)],
            null,null,MainPartyId:party,PayloadVersion:4,VoucherTypeCode:supplier?"Disbursement":"CashReceipt");
        var draftPath=$"/api/commerce/v1/accounting/manual/drafts/{capture.DocumentId}";
        using var proposal=await client.PutAsJsonAsync(draftPath,capture);
        Assert.True(proposal.IsSuccessStatusCode,await proposal.Content.ReadAsStringAsync());
        var draft=(await proposal.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal(configuration.Name,draft.Lines.Single(line=>line.Value.LineId==conceptLine).FinancialConceptName);
        using(var filter=await client.GetAsync($"/api/commerce/v1/accounting/documents?from=2026-08-01&to=2026-08-31&financialConceptId={created.FinancialConceptId}"))
        {
            Assert.True(filter.IsSuccessStatusCode,await filter.Content.ReadAsStringAsync());
            Assert.Equal(capture.DocumentId,Assert.Single((await filter.Content.ReadFromJsonAsync<AccountingDocumentPage>())!.Items).SourceDocumentId);
        }
        foreach (var wrongAccount in new[] { true, false })
        {
            var invalidLines=capture.Lines.Select(line=>line.LineId!=conceptLine?line:wrongAccount
                ?line with {AccountId=cash.AccountId}
                :line with {Debit=line.Credit,Credit=line.Debit}).ToArray();
            using var savedInvalid=await client.PutAsJsonAsync(draftPath,capture with {Lines=invalidLines,RowVersion=draft.RowVersion});
            Assert.True(savedInvalid.IsSuccessStatusCode,await savedInvalid.Content.ReadAsStringAsync());
            var invalidDraft=(await savedInvalid.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using(var invalidSend=await client.PostAsJsonAsync(draftPath+"/send",new SendVoucherDraftRequest(invalidDraft.RowVersion)))
                Assert.Equal(HttpStatusCode.BadRequest,invalidSend.StatusCode);
            Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",capture.DocumentId));
            Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM accounting.SourceConceptIndex WHERE SourceDocumentId=@Id",capture.DocumentId));
            using var restoreDraft=await client.PutAsJsonAsync(draftPath,capture with {RowVersion=invalidDraft.RowVersion});
            Assert.True(restoreDraft.IsSuccessStatusCode,await restoreDraft.Content.ReadAsStringAsync());
            draft=(await restoreDraft.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        }
        var mismatched=configuration with {RowVersion=created.RowVersion,UseForReceipts=false,UseForSupplierPayments=false};
        using var changed=await client.PutAsJsonAsync(configurationPath,mismatched);
        Assert.True(changed.IsSuccessStatusCode,await changed.Content.ReadAsStringAsync());
        var disabledUsage=(await changed.Content.ReadFromJsonAsync<FinancialConceptSaveResult>())!.Concept;
        using(var rejected=await client.PostAsJsonAsync(draftPath+"/send",new SendVoucherDraftRequest(draft.RowVersion)))
            Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(0,await CountAsync("SELECT COUNT(*) FROM accounting.SourceConceptIndex WHERE SourceDocumentId=@Id",capture.DocumentId));
        using var restored=await client.PutAsJsonAsync(configurationPath,configuration with {RowVersion=disabledUsage.RowVersion});
        Assert.True(restored.IsSuccessStatusCode,await restored.Content.ReadAsStringAsync());
        var current=(await restored.Content.ReadFromJsonAsync<FinancialConceptSaveResult>())!.Concept;
        using var send=await client.PostAsJsonAsync(draftPath+"/send",new SendVoucherDraftRequest(draft.RowVersion));
        Assert.True(send.IsSuccessStatusCode,await send.Content.ReadAsStringAsync());
        var posted=(await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal("Posted",posted.Status);
        Assert.Equal(created.FinancialConceptId,Assert.Single(posted.FinancialEffects!.Concepts!).FinancialConceptId);
        Assert.Equal(configuration.Name,Assert.Single(posted.FinancialEffects.Concepts!).Name);
        Assert.Equal(advance?1:0,posted.FinancialEffects.Positions.Count);
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM accounting.SourceConceptIndex WHERE SourceDocumentId=@Id",capture.DocumentId));
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",capture.DocumentId));
        var opposite=sides.Single(value=>value.Code==(supplier?"Credit":"Debit"));
        using(var semanticChange=await client.PutAsJsonAsync(configurationPath,configuration with {RowVersion=current.RowVersion,SideOptionId=opposite.Id}))
            Assert.Equal(advance?HttpStatusCode.BadRequest:HttpStatusCode.Conflict,semanticChange.StatusCode);
        await using(var connection=new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var lookup=new SqlCommand("SELECT TOP(1) AccountId FROM dbo.AccountingAccounts WHERE TenantId=@Tenant AND FinancialFunction IS NULL ORDER BY Code",connection);
            lookup.Parameters.AddWithValue("@Tenant",fixture.TenantId);
            var unconfigured=Assert.IsType<Guid>(await lookup.ExecuteScalarAsync());
            using var semanticRemoval=await client.PutAsJsonAsync(configurationPath,configuration with {
                RowVersion=current.RowVersion,AccountId=unconfigured,IsActive=false});
            Assert.Equal(HttpStatusCode.Conflict,semanticRemoval.StatusCode);
        }
        using var renamed=await client.PutAsJsonAsync(configurationPath,configuration with {RowVersion=current.RowVersion,Name="Nombre posterior",IsActive=false});
        Assert.True(renamed.IsSuccessStatusCode,await renamed.Content.ReadAsStringAsync());
        using var replay=await client.PostAsJsonAsync(draftPath+"/send",new SendVoucherDraftRequest(posted.RowVersion));
        Assert.True(replay.IsSuccessStatusCode,await replay.Content.ReadAsStringAsync());
        var replayed=(await replay.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal(configuration.Name,replayed.Lines.Single(line=>line.Value.LineId==conceptLine).FinancialConceptName);
        Assert.Equal(1,await CountAsync("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id",capture.DocumentId));
        var accounting=scope.ServiceProvider.GetRequiredService<AccountingService>();
        var identity=new AccountingUserIdentity(fixture.UserId,fixture.TenantId,fixture.BusinessId,new HashSet<string>{AccountingPermissionCodes.Read});
        using(var reads=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
        {
            var result=await accounting.ListDocumentsAsync(identity,new(2026,8,1),new(2026,8,31),null,null,null,1,25,financialConceptId:created.FinancialConceptId);
            Assert.Equal(capture.DocumentId,Assert.Single(result.Items).SourceDocumentId);Assert.Equal(1,reads.Count);
        }
        foreach(var search in new string?[]{null,"RC-CONCEPTO"})
        {
            using var reads=new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
            var result=await accounting.ListDocumentsAsync(identity,new(2026,8,1),new(2026,8,31),null,"Posted",search,1,25,
                partyId:party,voucherTypeCode:capture.VoucherTypeCode,financialConceptId:created.FinancialConceptId);
            Assert.Equal(capture.DocumentId,Assert.Single(result.Items).SourceDocumentId);Assert.Equal(1,reads.Count);
        }
    }
}
