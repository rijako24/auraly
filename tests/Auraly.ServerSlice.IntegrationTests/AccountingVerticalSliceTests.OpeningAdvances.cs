using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
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
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend);
            var (partyId, siteId) = await PrepareOpeningInvoicePartyAsync();
            var advanceAccount = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                supplier ? "1391" : "2391", supplier ? "Asset" : "Liability", supplier ? "SupplierAdvance" : "CustomerAdvance", supplier ? "Debit" : "Credit");
            var invoiceAccount = await CreateHistoricalAccountAsync(client, !supplier);
            var equity = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!
                .First(item => item.AccountType == "Equity" && item.IsActive && item.AllowsPosting);
            var batch = Guid.NewGuid();
            var cut = new DateOnly(2027, 3, existing ? supplier?3:4 : supplier ? 1 : 2);
            var reference = $"ANT-{batch:N}";
            var advance = new AccountingOpeningBalanceLineRequest(advanceAccount.AccountId, partyId, null,
                reference, supplier ? 1_000.1254m : 0, supplier ? 0 : 1_000.1254m, Guid.NewGuid(),
                NewAdvance: new(reference, cut.AddDays(-70), siteId));
            if(existing)
            {
                var role=await ScalarAsync<Guid>($"SELECT {(supplier?"SupplierId":"CustomerId")} FROM dbo.{(supplier?"Suppliers":"Customers")} WHERE PartyId=@Id",partyId);
                var creditId=Guid.NewGuid();var original=Guid.NewGuid();
                await using var connection=new SqlConnection(fixture.ConnectionString);await connection.OpenAsync();
                await using var arrange=new SqlCommand($"""
                    INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
                    VALUES(@Source,N'ManualAccountingVoucher',@Tenant,@Business,@Payload,CONVERT(binary(32),0),'2025-01-01','2025-01-01',0);
                    INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
                      SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt)
                    VALUES(NEWID(),@Tenant,@Business,@Source,N'ManualAccountingVoucher',CONVERT(binary(32),0),'2025-01-01',0,N'CommercialEffectsApplied',1,'2025-01-01');
                    INSERT dbo.{(supplier?"SupplierCredits":"CustomerCredits")}({(supplier?"SupplierCreditId":"CustomerCreditId")},BusinessId,
                      {(supplier?"SupplierId":"CustomerId")},PartySiteId,SourceDocumentId,SourceDocumentType,SourceLineId,CreditNature,CurrencyCode,OriginalAmount,AvailableAmount,Status,CreatedAt)
                    VALUES(@Credit,@Business,@Role,@Site,@Source,N'ManualAccountingVoucher',NEWID(),N'Advance',N'COP',1000.1254,1000.1254,N'Open','2025-01-01');
                    INSERT dbo.{(supplier?"SupplierCreditTransactions":"CustomerCreditTransactions")}({(supplier?"SupplierCreditTransactionId":"CustomerCreditTransactionId")},
                      {(supplier?"SupplierCreditId":"CustomerCreditId")},TransactionType,Amount,SourceDocumentId,SourceDocumentType,OccurredAt,CreatedAt)
                    VALUES(NEWID(),@Credit,N'Opening',1000.1254,@Source,N'ManualAccountingVoucher','2025-01-01','2025-01-01');
                    """,connection);
                arrange.Parameters.AddWithValue("@Payload","{}");arrange.Parameters.AddWithValue("@Source",original);arrange.Parameters.AddWithValue("@Credit",creditId);
                arrange.Parameters.AddWithValue("@Role",role);arrange.Parameters.AddWithValue("@Site",siteId);
                arrange.Parameters.AddWithValue("@Tenant",fixture.TenantId);arrange.Parameters.AddWithValue("@Business",fixture.BusinessId);
                await arrange.ExecuteNonQueryAsync();
                advance=advance with {NewAdvance=null,ExistingPosition=new(supplier?"SupplierCredit":"CustomerCredit",creditId)};
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
            if(!existing)Assert.False(string.IsNullOrWhiteSpace(saved.Lines[0].NewAdvancePartySiteName));
            else Assert.Equal(advance.ExistingPosition,saved.Lines[0].ExistingPosition);
            var creditTable = supplier ? "SupplierCredits" : "CustomerCredits";
            Assert.Equal(0, await CountAsync(creditTable, "SourceDocumentId", batch));
            var invalidEvidence=existing ? new[] {
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
            using (var duplicate = await client.PutAsJsonAsync(path, request with {
                RowVersion = saved.RowVersion, Lines = [advance, advance with { LineId = Guid.NewGuid() }, invoice, request.Lines[2]] }))
                Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            using (var approve = await client.PostAsJsonAsync($"{path}/{batch}/approve", new { }))
                Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
            // Fail after the native credit writer: journal, portfolio and batch
            // must roll back together, retaining the accepted identities for retry.
            using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal)
                    && command.Parameters.Contains("@DocumentId") && (Guid)command.Parameters["@DocumentId"].Value == batch,
                beforeExecute: command => {
                    var rows = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                    rows[1]!["SourceLineId"] = Guid.NewGuid();
                    command.Parameters["@Lines"].Value = rows.ToJsonString();
                }))
            using (var failed = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate", new ActivateAccountingRequest(cut, "COP", "ImportedAndApproved")))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.Equal(1, fault.Count);
            }
            Assert.Equal(0, await CountAsync(creditTable, "SourceDocumentId", batch));
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
            if(existing)Assert.Equal(advance.ExistingPosition!.SubledgerId,credit.SubledgerId);
            else Assert.Equal(reference,credit.DocumentNumber);
            Assert.Equal(1,await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{(supplier?"SupplierCreditTransactions":"CustomerCreditTransactions")} WHERE {(supplier?"SupplierCreditId":"CustomerCreditId")}=@Id",credit.SubledgerId));
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
            foreach (var id in new[] { batch, applicationId })
            {
                Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", id));
                Assert.Equal(0, await CountAsync("WorkSessionMovements", "DocumentId", id));
                Assert.Equal(0, await CountAsync("FiscalDocumentProcesses", "DocumentId", id));
                Assert.Equal(0m, await ScalarAsync<decimal>("SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntryLines line JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId WHERE entry.SourceDocumentId=@Id", id));
            }
        }
        finally { await RestoreTestBusinessesAsync(businesses); await RestoreOpeningTestSettingsAsync(settings); }
    }
}
