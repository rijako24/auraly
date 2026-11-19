using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Application;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class AccountingVerticalSliceTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(50)]
    [InlineData(500)]
    public async Task Initial_invoice_capture_keeps_three_commands_and_bounded_latency(int count)
    {
        var snapshot = await CaptureOpeningTestSettingsAsync();
        try
        {
            await DisableAccountingAsync();
            using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
            var (partyId, siteId) = await PrepareOpeningInvoicePartyAsync();
            var account = await CreateHistoricalAccountAsync(client, false);
            var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
            var equity = accounts.First(item => item.AccountType == "Equity" && item.AllowsPosting && item.IsActive);
            var batchId = Guid.NewGuid();
            var cut = new DateOnly(2027, 1, count == 2 ? 1 : count == 50 ? 2 : 3);
            var lines = Enumerable.Range(0, count - 1).Select(index => new AccountingOpeningBalanceLineRequest(
                account, partyId, null, "Factura inicial", 0, 1, Guid.NewGuid(),
                NewInvoice: new($"INI-{batchId:N}-{index}", cut.AddDays(-60), cut.AddDays(-30), siteId))).ToList();
            lines.Add(new(equity.AccountId, null, null, "Contrapartida", count - 1, 0));
            var request = new SaveAccountingOpeningBalanceRequest(batchId, fixture.BusinessId, cut, "COP", "Cartera inicial por conjunto", null, lines);
            using var scope = fixture.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<AccountingService>();
            var actor = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { AccountingPermissionCodes.Configure });
            var warmup = await service.SaveOpeningBalanceAsync(actor, request);
            request = request with { RowVersion = warmup.RowVersion };
            var samples = new List<double>();
            for (var attempt = 0; attempt < 20; attempt++)
            {
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(
                    new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var saved = await service.SaveOpeningBalanceAsync(actor, request);
                watch.Stop();
                samples.Add(watch.Elapsed.TotalMilliseconds);
                Assert.Equal(3, counter.Count);
                Assert.Equal(count, saved.Lines.Count);
                Assert.All(saved.Lines.Take(count - 1), line => Assert.NotNull(line.NewInvoice));
                request = request with { RowVersion = saved.RowVersion };
            }
            samples.Sort();
            var p95 = samples[18];
            output.WriteLine("Initial invoices {0} lines: 3 SQL commands; p95 {1:F1} ms; 20 warm samples", count, p95);
            Assert.True(p95 < 1500, $"Initial {count}-line invoice capture p95 was {p95:F1} ms.");
            Assert.Equal(0, await CountAsync("Payables", "SourceDocumentId", batchId));
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", batchId));
        }
        finally { await RestoreOpeningTestSettingsAsync(snapshot); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Initial_invoices_preserve_original_evidence_and_can_be_paid_once(bool receivable)
    {
        var snapshot = await CaptureOpeningTestSettingsAsync();
        var otherBusinesses = await DisableOtherTestBusinessesAsync();
        try
        {
            await DisableAccountingAsync();
            using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
                PayablesPermissionCodes.RegisterPayment, PayablesPermissionCodes.Read,
                ReceivablesPermissionCodes.RegisterPayment, ReceivablesPermissionCodes.Read);
            var (partyId, siteId) = await PrepareOpeningInvoicePartyAsync();
            var account = await CreateHistoricalAccountAsync(client, receivable);
            var accounts = (await client.GetFromJsonAsync<AccountingAccountView[]>("/api/commerce/v1/accounting/accounts"))!;
            var equity = accounts.First(item => item.AccountType == "Equity" && item.AllowsPosting && item.IsActive);
            var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
            var cut = new DateOnly(2026, 12, receivable ? 11 : 10);
            var batchId = Guid.NewGuid();
            var reference = $"INI-{batchId:N}";
            var invoice = new AccountingOpeningInvoice(reference, cut.AddDays(-60), cut.AddDays(-30), siteId);
            var firstLine = new AccountingOpeningBalanceLineRequest(account, partyId, null,
                "Factura anterior al inicio", receivable ? 1_000m : 0m, receivable ? 0m : 1_000m, Guid.NewGuid(), NewInvoice: invoice);
            var secondLine = firstLine with { LineId = Guid.NewGuid(), NewInvoice = invoice with { DocumentNumber = reference + "-2" } };
            var request = new SaveAccountingOpeningBalanceRequest(batchId, fixture.BusinessId, cut, "COP", "Cartera inicial", null,
                [firstLine, secondLine, new(equity.AccountId, null, null, "Contrapartida de apertura",
                    receivable ? 0m : 2_000m, receivable ? 2_000m : 0m)]);
            var path = "/api/commerce/v1/accounting/opening-balances";
            AccountingOpeningBalanceView saved;
            using (var response = await client.PutAsJsonAsync(path, request))
            {
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                saved = (await response.Content.ReadFromJsonAsync<AccountingOpeningBalanceView>())!;
            }
            Assert.Equal(invoice, saved.Lines[0].NewInvoice);
            Assert.False(string.IsNullOrWhiteSpace(saved.Lines[0].PartyName));
            Assert.False(string.IsNullOrWhiteSpace(saved.Lines[0].NewInvoicePartySiteName));
            var table = receivable ? "Receivables" : "Payables";
            var key = receivable ? "ReceivableId" : "PayableId";
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE SourceDocumentId=@Id", batchId));
            foreach (var invalid in new[]
            {
                firstLine with { NewInvoice = invoice with { PartySiteId = Guid.NewGuid() } },
                firstLine with { NewInvoice = invoice with { IssuedOn = cut } },
                firstLine with { NewInvoice = invoice with { DueOn = invoice.IssuedOn.AddDays(-1) } },
                firstLine with { AccountId = equity.AccountId },
                firstLine with { Debit = receivable ? 0m : 1_000m, Credit = receivable ? 1_000m : 0m },
                firstLine with { PartyId = Guid.NewGuid() }
            })
            {
                using var rejected = await client.PutAsJsonAsync(path, request with
                    { RowVersion = saved.RowVersion, Lines = [invalid, request.Lines[2]] });
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            using (var duplicate = await client.PutAsJsonAsync(path, request with
                { RowVersion = saved.RowVersion, Lines = [firstLine, secondLine with { NewInvoice = invoice }, request.Lines[2]] }))
                Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
            using (var approve = await client.PostAsJsonAsync($"{path}/{batchId}/approve", new { }))
                Assert.True(approve.IsSuccessStatusCode, await approve.Content.ReadAsStringAsync());
            using (var fault = new InventoryBalanceProcessingTests.CommandCounter(
                new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains("DECLARE @JournalLines TABLE", StringComparison.Ordinal)
                    && command.Parameters.Contains("@DocumentId") && (Guid)command.Parameters["@DocumentId"].Value == batchId,
                beforeExecute: command =>
                {
                    var journal = JsonNode.Parse((string)command.Parameters["@Lines"].Value)!.AsArray();
                    journal[0]!["SourceLineId"] = Guid.NewGuid();
                    command.Parameters["@Lines"].Value = journal.ToJsonString();
                }))
            using (var failed = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
                new ActivateAccountingRequest(cut, "COP", "ImportedAndApproved")))
            {
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.Equal(1, fault.Count);
            }
            Assert.Equal(0, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE SourceDocumentId=@Id", batchId));
            Assert.Equal(0, await CountAsync("AccountingEntries", "SourceDocumentId", batchId));
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
                    new ActivateAccountingRequest(cut, "COP", "ImportedAndApproved"));
                Assert.True(activate.IsSuccessStatusCode, await activate.Content.ReadAsStringAsync());
                Assert.Equal(AccountingActivationStatuses.Ready, (await activate.Content.ReadFromJsonAsync<AccountingReadinessView>())!.Status);
            }
            Assert.Equal(2, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE SourceDocumentId=@Id", batchId));
            Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", batchId));
            var positions = new List<(Guid Id, string Number)>();
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var query = new SqlCommand($"""
                    SELECT {key},DocumentNumber,OriginalAmount,OutstandingAmount,InvoiceIssuedAt,DueDate,
                      OpeningEntryId,OpeningEntryLineNumber,PartySiteId
                    FROM dbo.{table} WHERE SourceDocumentId=@Id ORDER BY DocumentNumber;
                    """, connection);
                query.Parameters.AddWithValue("@Id", batchId);
                await using var reader = await query.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    positions.Add((reader.GetGuid(0), reader.GetString(1)));
                    Assert.Equal(1_000m, reader.GetDecimal(2)); Assert.Equal(1_000m, reader.GetDecimal(3));
                    Assert.Equal(invoice.IssuedOn, DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(4).DateTime));
                    Assert.Equal(invoice.DueOn, DateOnly.FromDateTime(reader.GetFieldValue<DateTimeOffset>(5).DateTime));
                    Assert.False(reader.IsDBNull(6)); Assert.False(reader.IsDBNull(7)); Assert.Equal(siteId, reader.GetGuid(8));
                }
            }
            Assert.Equal(new[] { reference, reference + "-2" }, positions.Select(item => item.Number));
            var paymentId = Guid.NewGuid();
            var kind = receivable ? "Receivable" : "Payable";
            var amounts = new[] { 125m, 75m };
            var at = new DateTimeOffset(cut.ToDateTime(new TimeOnly(9, 0)), TimeSpan.FromHours(-5));
            var payment = new SaveVoucherDraftRequest(paymentId, AccountingManualDocumentTypes.ManualVoucher, at,
                "MANUAL_VOUCHER", "Abono a facturas iniciales", null,
                [.. positions.Select((position, index) => new VoucherDraftLine(account, partyId, null,
                    position.Number, receivable ? 0 : amounts[index], receivable ? amounts[index] : 0, position.Number,
                    Guid.NewGuid(), ExistingPosition: new(kind, position.Id), FinancialGroupId: paymentId, PartySiteId: siteId)),
                 new(cash.AccountId, partyId, null, "Dinero", receivable ? 200 : 0, receivable ? 0 : 200,
                     LineId: Guid.NewGuid(), FinancialGroupId: paymentId)],
                null, null, MainPartyId: partyId, PayloadVersion: 4,
                VoucherTypeCode: receivable ? "CashReceipt" : "Disbursement");
            var draftPath = $"/api/commerce/v1/accounting/manual/drafts/{paymentId}";
            VoucherDraftView draft;
            using (var save = await client.PutAsJsonAsync(draftPath, payment))
            {
                Assert.True(save.IsSuccessStatusCode, await save.Content.ReadAsStringAsync());
                draft = (await save.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            }
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var send = await client.PostAsJsonAsync(draftPath + "/send", new SendVoucherDraftRequest(draft.RowVersion));
                Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
                draft = (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            }
            for (var index = 0; index < positions.Count; index++)
                Assert.Equal(1_000m - amounts[index], await ScalarAsync<decimal>(
                    $"SELECT OutstandingAmount FROM dbo.{table} WHERE {key}=@Id", positions[index].Id));
            Assert.Equal(1, await CountAsync("AccountingEntries", "SourceDocumentId", paymentId));
            Assert.Equal(1, await ScalarAsync<int>(receivable
                ? "SELECT COUNT(*) FROM dbo.CustomerPayments WHERE ParentSourceDocumentId=@Id"
                : "SELECT COUNT(*) FROM dbo.SupplierPayments WHERE ParentSourceDocumentId=@Id", paymentId));
            Assert.Equal(0m, await ScalarAsync<decimal>("""
                SELECT SUM(line.Debit-line.Credit) FROM dbo.AccountingEntryLines line
                JOIN dbo.AccountingEntries entry ON entry.EntryId=line.EntryId WHERE entry.SourceDocumentId=@Id
                """, batchId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id", batchId));
            using var reportResponse = await client.GetAsync($"/api/commerce/v1/{(receivable ? "receivables" : "payables")}/report" +
                $"?page=1&pageSize=20&consolidated=false&templateVersion=4&dateRangeVersion=2&cutoff={cut:yyyy-MM-dd}&search={reference}");
            Assert.True(reportResponse.IsSuccessStatusCode, await reportResponse.Content.ReadAsStringAsync());
            using var report = System.Text.Json.JsonDocument.Parse(await reportResponse.Content.ReadAsStringAsync());
            var reported = report.RootElement.GetProperty("items").EnumerateArray().OrderBy(item => item.GetProperty("documentNumber").GetString()).ToArray();
            Assert.Equal(2, reported.Length);
            for (var index = 0; index < reported.Length; index++)
            {
                Assert.Equal(positions[index].Number, reported[index].GetProperty("documentNumber").GetString());
                Assert.Equal(1_000m - amounts[index], reported[index].GetProperty("outstandingAmount").GetDecimal());
                Assert.Equal(1_000m - amounts[index], reported[index].GetProperty("overdue1To30Amount").GetDecimal());
                Assert.Equal(invoice.IssuedOn, DateOnly.FromDateTime(reported[index].GetProperty("issuedAt").GetDateTimeOffset().DateTime));
            }
            using var beforeCut = await client.GetAsync($"/api/commerce/v1/{(receivable ? "receivables" : "payables")}/report" +
                $"?page=1&pageSize=20&consolidated=false&templateVersion=4&dateRangeVersion=2&cutoff={cut.AddDays(-1):yyyy-MM-dd}&search={reference}");
            Assert.True(beforeCut.IsSuccessStatusCode, await beforeCut.Content.ReadAsStringAsync());
            using var priorReport = System.Text.Json.JsonDocument.Parse(await beforeCut.Content.ReadAsStringAsync());
            Assert.Empty(priorReport.RootElement.GetProperty("items").EnumerateArray());
        }
        finally
        {
            await RestoreTestBusinessesAsync(otherBusinesses);
            await RestoreOpeningTestSettingsAsync(snapshot);
        }
    }

    private async Task<(Guid PartyId, Guid SiteId)> PrepareOpeningInvoicePartyAsync()
    {
        var partyId = Guid.NewGuid();
        var siteId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            INSERT dbo.Parties(PartyId,TenantId,PartyType,DisplayName,LegalName,CompletionStatus,IsActive,CreatedBy,CreatedAt)
            VALUES(@PartyId,@TenantId,N'Organization',N'Tercero de apertura QA',N'Tercero de apertura QA',N'Incomplete',1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Customers(CustomerId,PartyId,TenantId,RequiresElectronicInvoice,IsActive,CreatedBy,CreatedAt)
            VALUES(NEWID(),@PartyId,@TenantId,0,1,@UserId,SYSDATETIMEOFFSET());
            INSERT dbo.Suppliers(SupplierId,TenantId,PartyId,Identification,Name,IsActive,CreatedAt)
            VALUES(NEWID(),@TenantId,@PartyId,CONVERT(nvarchar(36),@PartyId),N'Proveedor de apertura QA',1,SYSDATETIMEOFFSET());
            INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,CityId,
              AddressLine,PostalCode,IsPrimary,IsActive,CreatedBy,CreatedAt)
            SELECT TOP(1) @SiteId,@PartyId,N'PRINCIPAL',N'Sede de apertura QA',CountryId,AdministrativeDivisionId,CityId,
              AddressLine,PostalCode,1,1,@UserId,SYSDATETIMEOFFSET()
            FROM dbo.PartySites WHERE PartyId=@TemplatePartyId AND IsActive=1 ORDER BY IsPrimary DESC,PartySiteId;
            IF @@ROWCOUNT<>1 THROW 51000,'The opening test requires its isolated party site.',1;
            """, connection);
        command.Parameters.AddWithValue("@PartyId", partyId);
        command.Parameters.AddWithValue("@SiteId", siteId);
        command.Parameters.AddWithValue("@TemplatePartyId", fixture.SupplierPartyId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        command.Parameters.AddWithValue("@UserId", fixture.UserId);
        await command.ExecuteNonQueryAsync();
        return (partyId, siteId);
    }
}
