using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Receivables;
using Auraly.Contracts.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Initial_advances_open_once_and_apply_to_initial_invoices_without_moving_money(bool supplier, bool existing)
    {
        var settings = await CaptureOpeningTestSettingsAsync();
        var businesses = await DisableOtherTestBusinessesAsync();
        try
        {
            await DisableAccountingAsync();
            using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
                ReceivablesPermissionCodes.RegisterPayment, PayablesPermissionCodes.RegisterPayment);
            var (partyId, siteId) = await PrepareOpeningInvoicePartyAsync();
            var advanceAccount = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                supplier ? "1391" : "2391", supplier ? "Asset" : "Liability", supplier ? "SupplierAdvance" : "CustomerAdvance", supplier ? "Debit" : "Credit");
            var invoiceAccount = await CreateHistoricalAccountAsync(client, !supplier);
            var equity = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!
                .First(item => item.AccountType == "Equity" && item.IsActive && item.AllowsPosting);
            var batch = Guid.NewGuid();
            var cut = new DateOnly(2026, 11, existing ? supplier ? 26 : 27 : supplier ? 24 : 25);
            var reference = $"ANT-{batch:N}";
            var advance = new AccountingOpeningBalanceLineRequest(advanceAccount.AccountId, partyId, null,
                reference, supplier ? 1_000.1254m : 0, supplier ? 0 : 1_000.1254m, Guid.NewGuid(),
                NewAdvance: new(reference, cut.AddDays(-70), siteId));
            if (existing)
            {
                var role = await ScalarAsync<Guid>($"SELECT {(supplier ? "SupplierId" : "CustomerId")} FROM dbo.{(supplier ? "Suppliers" : "Customers")} WHERE PartyId=@Id", partyId);
                var creditId = Guid.NewGuid(); var original = Guid.NewGuid();
                await using var connection = new SqlConnection(fixture.ConnectionString); await connection.OpenAsync();
                await using var arrange = new SqlCommand($"""
                    INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                    VALUES(@Source,N'ManualAccountingVoucher',@Tenant,@Business,@Payload,CONVERT(binary(32),0),'2025-01-01','2025-01-01',0);
                    INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                      SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
                    VALUES(NEWID(),@Tenant,@Business,@Source,N'ManualAccountingVoucher',CONVERT(binary(32),0),'2025-01-01',0,N'CommercialEffectsApplied',1,'2025-01-01');
                    INSERT dbo.{(supplier ? "SupplierCredits" : "CustomerCredits")}({(supplier ? "SupplierCreditId" : "CustomerCreditId")},BusinessId,
                      {(supplier ? "SupplierId" : "CustomerId")},PartySiteId,SourceDocumentId,SourceDocumentType,SourceLineId,CreditNature,CurrencyCode,OriginalAmount,AvailableAmount,Status,CreatedAt)
                    VALUES(@Credit,@Business,@Role,@Site,@Source,N'ManualAccountingVoucher',NEWID(),N'Advance',N'COP',1100.1254,1000.1254,N'{(supplier ? "PartiallyApplied" : "Open")}','2025-01-01');
                    INSERT dbo.{(supplier ? "SupplierCreditTransactions" : "CustomerCreditTransactions")}({(supplier ? "SupplierCreditTransactionId" : "CustomerCreditTransactionId")},
                      {(supplier ? "SupplierCreditId" : "CustomerCreditId")},TransactionType,Amount,SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
                    VALUES(NEWID(),@Credit,N'Opening',1100.1254,@Source,N'ManualAccountingVoucher','2025-01-01','2025-01-01');
                    DECLARE @PastPosition uniqueidentifier=NEWID(),@PastApplication uniqueidentifier=NEWID();
                    INSERT dbo.{(supplier ? "Payables" : "Receivables")}({(supplier ? "PayableId" : "ReceivableId")},BusinessId,
                      {(supplier ? "SupplierId" : "CustomerId")},PartySiteId,SourceDocumentId,SourceDocumentType,SourceLineId,DocumentNumber,
                      CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
                    VALUES(@PastPosition,@Business,@Role,@Site,@Source,N'ManualAccountingVoucher',NEWID(),N'Anterior ya saldada',
                      N'COP',100,0,'2025-01-02',N'Paid','2025-01-01');
                    INSERT dbo.{(supplier ? "PayableTransactions" : "ReceivableTransactions")}({(supplier ? "PayableTransactionId" : "ReceivableTransactionId")},
                      {(supplier ? "PayableId" : "ReceivableId")},TransactionType,Amount,SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
                    VALUES(NEWID(),@PastPosition,N'Opening',100,@Source,N'ManualAccountingVoucher','2025-01-01','2025-01-01'),
                      (@PastApplication,@PastPosition,N'CreditApplication',-100,@Source,N'ManualAccountingVoucher','2025-01-02','2025-01-02');
                    INSERT dbo.{(supplier ? "SupplierCreditTransactions" : "CustomerCreditTransactions")}({(supplier ? "SupplierCreditTransactionId" : "CustomerCreditTransactionId")},
                      {(supplier ? "SupplierCreditId" : "CustomerCreditId")},TransactionType,Amount,SourceDocumentId,SourceDocumentType,SourceLineId,
                      AppliedPositionTransactionId,OccurredAt,CreatedAt)
                    VALUES(NEWID(),@Credit,N'Application',100,@Source,N'ManualAccountingVoucher',NEWID(),@PastApplication,@PastAt,'2025-01-02');
                    """, connection);
                arrange.Parameters.AddWithValue("@Payload", "{}"); arrange.Parameters.AddWithValue("@Source", original); arrange.Parameters.AddWithValue("@Credit", creditId);
                arrange.Parameters.AddWithValue("@Role", role); arrange.Parameters.AddWithValue("@Site", siteId);
                arrange.Parameters.AddWithValue("@Tenant", fixture.TenantId); arrange.Parameters.AddWithValue("@Business", fixture.BusinessId);
                // Still the preceding day in Bogotá, despite the UTC date.
                arrange.Parameters.AddWithValue("@PastAt", new DateTimeOffset(cut.ToDateTime(new TimeOnly(4, 59)), TimeSpan.Zero));
                await arrange.ExecuteNonQueryAsync();
                advance = advance with { NewAdvance = null, ExistingPosition = new(supplier ? "SupplierCredit" : "CustomerCredit", creditId) };
            }
            var invoice = new AccountingOpeningBalanceLineRequest(invoiceAccount, partyId, null, "Factura inicial",
                supplier ? 0 : 2_000.1254m, supplier ? 2_000.1254m : 0, Guid.NewGuid(),
                NewInvoice: new($"FAC-{batch:N}", cut.AddDays(-60), cut.AddDays(-30), siteId));
            var request = new SaveAccountingOpeningBalanceRequest(batch, fixture.BusinessId, cut, "COP", "Apertura con anticipo", null,
                [advance, invoice, new(equity.AccountId, null, null, "Contrapartida", supplier ? 1_000 : 0, supplier ? 0 : 1_000)]);
            const string path = "/api/commerce/v1/accounting/opening-balances";
            using var savedResponse = await client.PutAsJsonAsync(path, request);
            Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
            var saved = (await savedResponse.Content.ReadFromJsonAsync<AccountingOpeningBalanceView>())!;
            Assert.Equal(advance.NewAdvance, saved.Lines[0].NewAdvance);
            if (!existing) Assert.False(string.IsNullOrWhiteSpace(saved.Lines[0].NewAdvancePartySiteName));
            else Assert.Equal(advance.ExistingPosition, saved.Lines[0].ExistingPosition);
            if(existing)
            {
                await using var boundaryConnection=new SqlConnection(fixture.ConnectionString);await boundaryConnection.OpenAsync();
                await using var boundary=new SqlCommand($"""
                    UPDATE dbo.{(supplier?"SupplierCreditTransactions":"CustomerCreditTransactions")}
                    SET OccurredAt=@At WHERE {(supplier?"SupplierCreditId":"CustomerCreditId")}=@Credit AND TransactionType=N'Application';
                    """,boundaryConnection);
                boundary.Parameters.AddWithValue("@Credit",advance.ExistingPosition!.SubledgerId);
                var at=boundary.Parameters.Add("@At",System.Data.SqlDbType.DateTimeOffset);
                at.Value=new DateTimeOffset(cut.ToDateTime(new TimeOnly(5,0)),TimeSpan.Zero);
                await boundary.ExecuteNonQueryAsync();
                using(var rejected=await client.PutAsJsonAsync(path,request with{RowVersion=saved.RowVersion}))
                    Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
                at.Value=new DateTimeOffset(cut.ToDateTime(new TimeOnly(4,59)),TimeSpan.Zero);
                await boundary.ExecuteNonQueryAsync();
            }
            var creditTable = supplier ? "SupplierCredits" : "CustomerCredits";
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{creditTable} WHERE SourceDocumentId=@Id", batch));
            var invalidEvidence = existing ? new[] {
                advance with {ExistingPosition=new(supplier?"SupplierCredit":"CustomerCredit",Guid.NewGuid())},
                advance with {ExistingPosition=new(supplier?"CustomerCredit":"SupplierCredit",advance.ExistingPosition!.SubledgerId)},
                advance with {Debit=supplier?999:0,Credit=supplier?0:999},
                advance with {AccountId=equity.AccountId},
                advance with {ExistingPosition=null}
            } : new[] {
                advance with { NewAdvance = advance.NewAdvance! with { IssuedOn = cut } },
                advance with { NewAdvance = advance.NewAdvance! with { PartySiteId = Guid.NewGuid() } },
                advance with { AccountId = equity.AccountId },
                advance with { Debit = advance.Credit, Credit = advance.Debit },
                advance with { NewAdvance = null },
                advance with { PartyId = Guid.NewGuid() }
            };
            foreach (var invalid in invalidEvidence)
            {
                using var rejected = await client.PutAsJsonAsync(path, request with { RowVersion = saved.RowVersion, Lines = [invalid, invoice, request.Lines[2]] });
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            using (var duplicate = await client.PutAsJsonAsync(path, request with
            {
                RowVersion = saved.RowVersion,
                Lines = [advance, advance with { LineId = Guid.NewGuid() }, invoice, request.Lines[2]]
            }))
                Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            if (!existing)
            {
                var otherAdvance = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                    supplier ? "1392" : "2392", supplier ? "Asset" : "Liability", supplier ? "SupplierAdvance" : "CustomerAdvance", supplier ? "Debit" : "Credit");
                using var duplicateAcrossAccounts = await client.PutAsJsonAsync(path, request with
                {
                    RowVersion = saved.RowVersion,
                    Lines = [advance, advance with { AccountId = otherAdvance.AccountId, LineId = Guid.NewGuid() }, invoice, request.Lines[2]]
                });
                Assert.Equal(HttpStatusCode.BadRequest, duplicateAcrossAccounts.StatusCode);
            }
            else
            {
                foreach (var invalid in new[] { (Column: "Status", Value: supplier ? "Cancelled" : "Exhausted", Restore: supplier ? "PartiallyApplied" : "Open"),
                    (Column: "CreditNature", Value: "Credit", Restore: "Advance") })
                {
                    await using var connection = new SqlConnection(fixture.ConnectionString);
                    await connection.OpenAsync();
                    await using var update = new SqlCommand($"UPDATE dbo.{creditTable} SET {invalid.Column}=@Value WHERE {(supplier ? "SupplierCreditId" : "CustomerCreditId")}=@Id", connection);
                    update.Parameters.AddWithValue("@Id", advance.ExistingPosition!.SubledgerId);
                    update.Parameters.AddWithValue("@Value", invalid.Value);
                    await update.ExecuteNonQueryAsync();
                    using var rejected = await client.PutAsJsonAsync(path, request with { RowVersion = saved.RowVersion });
                    Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
                    update.Parameters["@Value"].Value = invalid.Restore;
                    await update.ExecuteNonQueryAsync();
                }
            }
            using (var approve = await client.PostAsJsonAsync($"{path}/{batch}/approve", new { }))
                Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
            // Fail after the native credit writer: journal, portfolio and batch
            // must roll back together, retaining the accepted identities for retry.
            using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal)
                    && command.Parameters.Contains("@DocumentId") && (Guid)command.Parameters["@DocumentId"].Value == batch,
                beforeExecute: command =>
                {
                    var rows = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                    rows[1]!["SourceLineId"] = Guid.NewGuid();
                    command.Parameters["@Lines"].Value = rows.ToJsonString();
                }))
            using (var failed = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate", new ActivateAccountingRequest(cut, "COP", "ImportedAndApproved")))
            {
                Assert.True(failed.StatusCode == HttpStatusCode.InternalServerError, await failed.Content.ReadAsStringAsync());
                Assert.Equal(1, fault.Count);
            }
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{creditTable} WHERE SourceDocumentId=@Id", batch));
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", batch));
            for (var retry = 0; retry < 2; retry++)
            {
                using var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate", new ActivateAccountingRequest(cut, "COP", "ImportedAndApproved"));
                Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync());
            }
            var creditKind = supplier ? "SupplierCredit" : "CustomerCredit";
            var positionKind = supplier ? "Payable" : "Receivable";
            var credits = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind={creditKind}&partyId={partyId}&advancesOnly=true&page=1&pageSize=10"))!;
            var credit = Assert.Single(credits.Items);
            Assert.Equal(1_000.1254m, credit.AvailableAmount);
            Assert.Equal("Advance", credit.CreditNature);
            Assert.Equal(advanceAccount.AccountId, credit.AccountId);
            Assert.NotNull(credit.OpeningEntryId);
            if (existing) Assert.Equal(advance.ExistingPosition!.SubledgerId, credit.SubledgerId);
            else Assert.Equal(reference, credit.DocumentNumber);
            Assert.Equal(existing ? 2 : 1, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{(supplier ? "SupplierCreditTransactions" : "CustomerCreditTransactions")} WHERE {(supplier ? "SupplierCreditId" : "CustomerCreditId")}=@Id", credit.SubledgerId));
            var debt = Assert.Single((await client.GetFromJsonAsync<AccountingOpenPositionPage>(
                $"/api/commerce/v1/accounting/open-positions?subledgerKind={positionKind}&partyId={partyId}&page=1&pageSize=10"))!.Items);
            var applicationId = Guid.NewGuid();
            var application = new SaveVoucherDraftRequest(applicationId, AccountingManualDocumentTypes.ManualVoucher,
                new DateTimeOffset(cut.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-5)), "MANUAL_VOUCHER", "Cruce del anticipo inicial", null,
                [new(advanceAccount.AccountId, partyId, null, "Aplicar anticipo", supplier ? 0 : 250, supplier ? 250 : 0,
                    LineId: Guid.NewGuid(), ExistingPosition: new(creditKind, credit.SubledgerId)),
                    new(invoiceAccount, partyId, null, "Saldar factura", supplier ? 250 : 0, supplier ? 0 : 250,
                    LineId: Guid.NewGuid(), ExistingPosition: new(positionKind, debt.SubledgerId))],
                null, null, MainPartyId: partyId, PayloadVersion: 4, VoucherTypeCode: supplier ? "Disbursement" : "CashReceipt");
            var draftPath = $"/api/commerce/v1/accounting/manual/drafts/{applicationId}";
            using var draftResponse = await client.PutAsJsonAsync(draftPath, application);
            Assert.True(draftResponse.IsSuccessStatusCode, await draftResponse.Content.ReadAsStringAsync());
            var draft = (await draftResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            for (var retry = 0; retry < 2; retry++)
            {
                using var send = await client.PostAsJsonAsync(draftPath + "/send", new SendVoucherDraftRequest(draft.RowVersion));
                Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
                draft = (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            }
            Assert.Equal(750.1254m, await ScalarAsync<decimal>($"SELECT AvailableAmount FROM dbo.{creditTable} WHERE {(supplier ? "SupplierCreditId" : "CustomerCreditId")}=@Id", credit.SubledgerId));
            Assert.Equal(1_750.1254m, await ScalarAsync<decimal>($"SELECT OutstandingAmount FROM dbo.{(supplier ? "Payables" : "Receivables")} WHERE {(supplier ? "PayableId" : "ReceivableId")}=@Id", debt.SubledgerId));
            var finalId = Guid.NewGuid();
            var final = application with
            {
                DocumentId = finalId,
                Lines = application.Lines.Select(line => line with
                {
                    LineId = Guid.NewGuid(),
                    Debit = line.Debit > 0 ? 750.1254m : 0,
                    Credit = line.Credit > 0 ? 750.1254m : 0
                }).ToArray()
            };
            using var finalSave = await client.PutAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{finalId}", final);
            Assert.True(finalSave.IsSuccessStatusCode, await finalSave.Content.ReadAsStringAsync());
            var finalDraft = (await finalSave.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            for (var retry = 0; retry < 2; retry++)
            {
                using var sent = await client.PostAsJsonAsync($"/api/commerce/v1/accounting/manual/drafts/{finalId}/send", new SendVoucherDraftRequest(finalDraft.RowVersion));
                Assert.True(sent.IsSuccessStatusCode, await sent.Content.ReadAsStringAsync());
                finalDraft = (await sent.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            }
            Assert.Equal(0m, await ScalarAsync<decimal>($"SELECT AvailableAmount FROM dbo.{creditTable} WHERE {(supplier ? "SupplierCreditId" : "CustomerCreditId")}=@Id", credit.SubledgerId));
            Assert.Equal(1_000m, await ScalarAsync<decimal>($"SELECT OutstandingAmount FROM dbo.{(supplier ? "Payables" : "Receivables")} WHERE {(supplier ? "PayableId" : "ReceivableId")}=@Id", debt.SubledgerId));
            Assert.Equal(existing ? 4 : 3, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{(supplier ? "SupplierCreditTransactions" : "CustomerCreditTransactions")} WHERE {(supplier ? "SupplierCreditId" : "CustomerCreditId")}=@Id", credit.SubledgerId));
            foreach (var id in new[] { batch, applicationId, finalId })
            {
                Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", id));
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.WorkSessionMovements WHERE DocumentId=@Id", id));
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id", id));
                Assert.Equal(0m, await ScalarAsync<decimal>("SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId WHERE entry.SourceDocumentId=@Id", id));
            }
        }
        finally { await RestoreTestBusinessesAsync(businesses); await RestoreOpeningTestSettingsAsync(settings); }
    }
}
