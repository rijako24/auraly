using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Ubl;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptProcessingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Manual_support_uses_the_same_fiscal_and_accounting_engines_without_an_expense_or_physical_receipt(bool withTaxes)
    {
        await ConfigureSupportDocumentAsync();
        using var scope = fixture.CreateScope();
        var accounting = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var identity = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                AccountingPermissionCodes.Activate, AccountingPermissionCodes.PeriodsManage,
                AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
                FiscalPermissionCodes.SendManualSupport,
                TaxationPermissionCodes.ManageWithholdingRules, TaxationPermissionCodes.ViewWithholdingRules });
        await accounting.EnsureDefaultsAsync(identity);
        if (!(await accounting.ListPeriodsAsync(identity)).Any(period => period.StartsOn <= new DateOnly(2026, 9, 30) && period.EndsOn >= new DateOnly(2026, 9, 30)))
            await accounting.CreatePeriodAsync(identity, new(Guid.NewGuid(), fixture.TenantId,
                new(2026, 1, 1), new(2026, 12, 31), "Período del soporte manual"));
        if ((await accounting.GetReadinessAsync(identity)).Status != AccountingActivationStatuses.Ready)
            await accounting.ActivateAsync(identity, new(new(2026, 1, 1), "COP", "ZeroDeclared"));
        using var client = fixture.CreateAdminClient(identity.Permissions.ToArray());
        var expense = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5198", "Expense", "General", "Debit");
        var payable = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "2298", "Liability", "Payable", "Credit");
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        CounterpartyTaxProfileView? previousProfile;
        using (var profileResponse = await client.GetAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}"))
        {
            Assert.True(profileResponse.IsSuccessStatusCode || profileResponse.StatusCode == HttpStatusCode.NotFound);
            previousProfile = profileResponse.StatusCode == HttpStatusCode.NotFound ? null :
                await profileResponse.Content.ReadFromJsonAsync<CounterpartyTaxProfileView>();
        }
        string? previousPolicy;
        await using (var configure = new SqlCommand("""
            UPDATE dbo.Suppliers SET PurchaseEvidencePolicy=N'BuyerElectronicSupportDocument'
            OUTPUT deleted.PurchaseEvidencePolicy WHERE SupplierId=@Id;
            """, connection))
        {
            configure.Parameters.AddWithValue("@Id", fixture.SupplierId);
            var previous = await configure.ExecuteScalarAsync();
            previousPolicy = previous is null or DBNull ? null : (string)previous;
        }
        WithholdingRuleView? temporaryRule = null;
        SaveWithholdingRuleRequest? ruleRequest = null;
        Guid? alternateSiteId = null;
        try
        {
            using (var profile = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, withTaxes, [], "11001")))
                Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync());
            var documentId = Guid.NewGuid();
            var lineId = Guid.NewGuid();
            var at = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(-5));
            var payableLineId = Guid.NewGuid();
            var vatLineId = Guid.NewGuid();
            var retentionLineId = Guid.NewGuid();
            var fiscalLines = new List<ManualVoucherLineRequest> {
                new(expense.AccountId, fixture.SupplierPartyId, null, "Servicio adquirido", 10_000m, 0, lineId) };
            var net = 10_000m;
            var capture = new VoucherFiscalCapture([new(lineId, 10_000m, null, "CapitalizedCost")]);
            if (withTaxes)
            {
                alternateSiteId = Guid.NewGuid();
                await using (var site = new SqlCommand("""
                    INSERT dbo.PartySites(PartySiteId,PartyId,Code,Name,CountryId,AdministrativeDivisionId,
                      CityId,AddressLine,PostalCode,IsPrimary,IsActive,CreatedBy,CreatedAt)
                    SELECT @Alternate,PartyId,@Code,N'Sede del soporte manual',CountryId,AdministrativeDivisionId,
                      CityId,N'Dirección alterna del soporte',PostalCode,0,1,@UserId,SYSDATETIMEOFFSET()
                    FROM dbo.PartySites WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1;
                    """, connection))
                {
                    site.Parameters.AddWithValue("@Alternate", alternateSiteId.Value);
                    site.Parameters.AddWithValue("@Code", "MS-" + alternateSiteId.Value.ToString("N")[..10]);
                    site.Parameters.AddWithValue("@UserId", fixture.UserId);
                    site.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId);
                    Assert.Equal(1, await site.ExecuteNonQueryAsync());
                }
                var vat = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                    "1395", "Asset", "General", "Debit");
                var retention = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                    "2368", "Liability", "General", "Credit");
                var taxId = await ScalarAsync<Guid>("""
                    SELECT TaxProfileId FROM dbo.TaxProfiles WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                      AND Code=N'IVA-19';
                    """, documentId);
                var concept = "MAN-" + Guid.NewGuid().ToString("N")[..12];
                ruleRequest = new(fixture.BusinessId, concept, "Retención del soporte manual", "IncomeTax", "Purchase",
                    "Accrual", "TaxExclusiveAmount", concept, null, 2.5m, 0, [], new(2026, 1, 1), null, true);
                using var ruleResponse = await client.PostAsJsonAsync("/api/commerce/v1/taxation/withholding-rules", ruleRequest);
                Assert.True(ruleResponse.IsSuccessStatusCode, await ruleResponse.Content.ReadAsStringAsync());
                temporaryRule = (await ruleResponse.Content.ReadFromJsonAsync<WithholdingRuleView>())!;
                capture = new([new(lineId, 10_000m, taxId, "DeductibleInputVat", vatLineId, concept)],
                    [new(retentionLineId, temporaryRule.RuleId)]);
                fiscalLines.Add(new(vat.AccountId, fixture.SupplierPartyId, null, "IVA descontable", 1_900m, 0, vatLineId));
                fiscalLines.Add(new(retention.AccountId, fixture.SupplierPartyId, null, "Retención", 0, 250m, retentionLineId));
                net = 11_650m;
            }
            fiscalLines.Add(new(payable.AccountId, fixture.SupplierPartyId, null, "Obligación", 0, net,
                payableLineId, DueDate: at.AddDays(30), PartySiteId: alternateSiteId));
            var raw = new ConfirmManualAccountingVoucherRequest(documentId, fixture.BusinessId, at,
                "MANUAL_VOUCHER", "Adquisición directa", fiscalLines,
                fixture.SupplierPartyId, 4, VoucherTypeCode: "SupportDocument", CurrencyCode: "COP", FiscalCapture: capture);
            if (!withTaxes)
            {
                foreach (var count in new[] { 1, 100, 499 })
                {
                    var bases = Enumerable.Range(0, count).Select(index => new ManualVoucherLineRequest(expense.AccountId,
                        fixture.SupplierPartyId, null, $"Servicio {index + 1}", 1m, 0, Guid.NewGuid())).ToArray();
                    var volume = raw with { Lines = [.. bases, fiscalLines[^1] with { Credit = count }],
                        FiscalCapture = new(bases.Select(line => new VoucherFiscalAcquisition(line.LineId!.Value, 1m, null, "CapitalizedCost")).ToArray()) };
                    Assert.True((await accounting.PreviewFiscalVoucherAsync(identity, volume)).CanConfirm);
                    var samples = new List<double>();
                    for (var sample = 0; sample < 20; sample++)
                    {
                        using var commands = new InventoryBalanceProcessingTests.CommandCounter(connection.Database);
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        var calculation = await accounting.PreviewFiscalVoucherAsync(identity, volume);
                        watch.Stop();
                        Assert.True(calculation.CanConfirm);
                        Assert.Equal(count, calculation.Withholding.NetAmount);
                        Assert.Equal(4, commands.Count);
                        samples.Add(watch.Elapsed.TotalMilliseconds);
                    }
                    var p95 = samples.Order().ElementAt(18);
                    output.WriteLine("Manual support fiscal review: {0} bases / {1} GL lines, SQL=4, samples=20, p95={2:F1}ms.", count, count + 1, p95);
                    Assert.True(p95 < 1500, $"Manual support review exceeded 1.5 s with {count} bases: p95={p95:F1}ms.");
                }
            }
            var before = await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", documentId);
            if (withTaxes)
            {
                using var pendingPreview = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/fiscal-preview", raw with {
                    Lines = raw.Lines.Where(line => line.LineId != retentionLineId).Select(line => line.LineId == payableLineId
                        ? line with { Credit = 11_900m } : line).ToArray(),
                    FiscalCapture = capture with { WithholdingBindings = null } });
                Assert.True(pendingPreview.IsSuccessStatusCode, await pendingPreview.Content.ReadAsStringAsync());
                var pending = (await pendingPreview.Content.ReadFromJsonAsync<VoucherFiscalPreview>())!;
                Assert.False(pending.CanConfirm);
                Assert.Equal(250m, pending.Withholding.WithholdingTotal);
                Assert.NotEmpty(pending.Diagnostics);
            }
            using var previewResponse = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/fiscal-preview", raw);
            Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
            var preview = (await previewResponse.Content.ReadFromJsonAsync<VoucherFiscalPreview>())!;
            Assert.True(preview.CanConfirm);
            Assert.Equal(net, preview.Withholding.NetAmount);
            Assert.Equal(before, await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", documentId));
            Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", documentId));
            var draft = new SaveVoucherDraftRequest(documentId, AccountingManualDocumentTypes.ManualVoucher, at,
                raw.ConceptCode, raw.Description, null, raw.Lines.Select(line => new VoucherDraftLine(line.AccountId,
                    line.PartyId, line.CostCenterId, line.Description, line.Debit, line.Credit, LineId: line.LineId,
                    DueDate: line.DueDate, PartySiteId: line.PartySiteId)).ToArray(), null, null, MainPartyId: fixture.SupplierPartyId,
                PayloadVersion: 4, VoucherTypeCode: "SupportDocument",
                FiscalCapture: capture with { CalculationHash = preview.CalculationHash });
            var path = $"/api/commerce/v1/accounting/manual/drafts/{documentId}";
            using var savedResponse = await client.PutAsJsonAsync(path, draft);
            Assert.True(savedResponse.IsSuccessStatusCode, await savedResponse.Content.ReadAsStringAsync());
            var saved = (await savedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            if (withTaxes)
            {
                // The review belongs to these exact journal and fiscal inputs.
                using var changedResponse = await client.PutAsJsonAsync(path, draft with {
                    Description = "Descripción posterior a la revisión", RowVersion = saved.RowVersion });
                Assert.True(changedResponse.IsSuccessStatusCode, await changedResponse.Content.ReadAsStringAsync());
                var changed = (await changedResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
                using var stale = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(changed.RowVersion));
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                Assert.Equal(before, await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", documentId));
                using var restoreResponse = await client.PutAsJsonAsync(path, draft with { RowVersion = changed.RowVersion });
                Assert.True(restoreResponse.IsSuccessStatusCode, await restoreResponse.Content.ReadAsStringAsync());
                saved = (await restoreResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
                var trigger = $"TestManualFiscalFailure_{Guid.NewGuid():N}";
                await using (var create = new SqlCommand($"""
                    CREATE TRIGGER fiscal.[{trigger}] ON fiscal.PurchaseSupportFiscalSnapshots AFTER INSERT AS
                    BEGIN
                      IF EXISTS(SELECT 1 FROM inserted WHERE DocumentId='{documentId:D}')
                        THROW 51997,'Injected manual support root failure.',1;
                    END;
                    """, connection)) await create.ExecuteNonQueryAsync();
                try
                {
                    using var failed = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
                    Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                    Assert.Equal(0, await ScalarAsync<int>("""
                        SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
                          +(SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id)
                          +(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
                          +(SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id)
                          +(SELECT COUNT(*) FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id)
                          +(SELECT COUNT(*) FROM billing.TenantDianDocumentUsages WHERE SourceDocumentId=@Id)
                          +(SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id)
                          +(SELECT COUNT(*) FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id)
                          +(SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id);
                        """, documentId));
                    Assert.Equal(before, await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", documentId));
                    using var unchanged = await client.GetAsync(path);
                    var stillDraft = (await unchanged.Content.ReadFromJsonAsync<VoucherDraftView>())!;
                    Assert.Null(stillDraft.SentAt);
                    Assert.Equal(saved.RowVersion, stillDraft.RowVersion);
                }
                finally
                {
                    await using var drop = new SqlCommand($"DROP TRIGGER fiscal.[{trigger}];", connection);
                    await drop.ExecuteNonQueryAsync();
                }
            }
            using var sentResponse = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
            Assert.True(sentResponse.IsSuccessStatusCode, await sentResponse.Content.ReadAsStringAsync());
            var sent = (await sentResponse.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            Assert.Equal("Posted", sent.Status);
            Assert.Equal("SupportDocument", sent.Row.FiscalDocumentType);
            Assert.StartsWith("DS", sent.Row.DianNumber);
            var afterSend = await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", documentId);
            using var replay = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(saved.RowVersion));
            Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
            Assert.Equal(afterSend, await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", documentId));
            foreach (var table in new[] { "AccountingSourceDocuments", "AccountingPostingJobs", "AccountingEntries", "Payables" })
                Assert.Equal(1, await ScalarAsync<int>($"SELECT COUNT(*) FROM dbo.{table} WHERE SourceDocumentId=@Id", documentId));
            Assert.Equal(net, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id", documentId));
            if (alternateSiteId.HasValue)
                Assert.Equal(alternateSiteId.Value, await ScalarAsync<Guid>("SELECT PartySiteId FROM dbo.Payables WHERE SourceDocumentId=@Id", documentId));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id", documentId));
            Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id", documentId));
            Assert.Equal(0, await ScalarAsync<int>("""
                SELECT (SELECT COUNT(*) FROM dbo.Expenses WHERE ExpenseId=@Id)
                  +(SELECT COUNT(*) FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id)
                  +(SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id)
                  +(SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id);
                """, documentId));
            var snapshot = await ScalarAsync<string>("SELECT SnapshotJson FROM fiscal.PurchaseSupportFiscalSnapshots WHERE DocumentId=@Id", documentId);
            using var payload = JsonDocument.Parse(snapshot);
            Assert.Equal(documentId, payload.RootElement.GetProperty("manualAcquisition").GetProperty("documentId").GetGuid());
            Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("expense").ValueKind);
            Assert.Equal(FiscalDocumentStatusCodes.AwaitingManualSubmission,
                await ScalarAsync<string>("SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id", documentId));
            using (var unauthorized = fixture.CreateAdminClient(AccountingPermissionCodes.ManualSend))
            using (var denied = await unauthorized.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/documents/{documentId}/send-manual-support", new { }))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            if (withTaxes)
            {
                int previousLimit;
                await using (var exhaust = new SqlCommand("""
                    UPDATE billing.TenantSubscriptions SET DianDocumentMonthlyLimit=0
                    OUTPUT deleted.DianDocumentMonthlyLimit WHERE TenantId=@TenantId;
                    """, connection))
                {
                    exhaust.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                    previousLimit = (int)(await exhaust.ExecuteScalarAsync())!;
                }
                try
                {
                    using var noQuota = await client.PostAsJsonAsync(
                        $"/api/commerce/v1/fiscal/documents/{documentId}/send-manual-support", new { });
                    Assert.Equal(HttpStatusCode.Conflict, noQuota.StatusCode);
                    Assert.Contains("cupo", await noQuota.Content.ReadAsStringAsync());
                    Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", documentId));
                    Assert.Equal(FiscalDocumentStatusCodes.AwaitingManualSubmission,
                        await ScalarAsync<string>("SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id", documentId));
                }
                finally
                {
                    await using var restoreLimit = new SqlCommand("UPDATE billing.TenantSubscriptions SET DianDocumentMonthlyLimit=@Limit WHERE TenantId=@TenantId;", connection);
                    restoreLimit.Parameters.AddWithValue("@TenantId", fixture.TenantId);
                    restoreLimit.Parameters.AddWithValue("@Limit", previousLimit);
                    await restoreLimit.ExecuteNonQueryAsync();
                }
            }
            using (var submitted = await client.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/documents/{documentId}/send-manual-support", new { }))
            {
                Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
                Assert.Equal(FiscalDocumentStatusCodes.PendingGeneration,
                    (await submitted.Content.ReadFromJsonAsync<FiscalDocumentView>())!.Status);
            }
            using (var replaySubmission = await client.PostAsJsonAsync(
                $"/api/commerce/v1/fiscal/documents/{documentId}/send-manual-support", new { }))
            {
                Assert.True(replaySubmission.IsSuccessStatusCode, await replaySubmission.Content.ReadAsStringAsync());
                Assert.Equal(1, await ScalarAsync<int>(
                    "SELECT COUNT(*) FROM billing.TenantDianDocumentUsages WHERE SourceDocumentId=@Id", documentId));
            }
            await GenerateFiscalAsync(documentId);
            var fiscal = await ReadFiscalGenerationAsync(documentId);
            Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission, fiscal.Status);
            var signed = await ReadArtifactTextAsync(documentId, "SignedXml");
            Assert.Contains("Servicio adquirido", signed);
            if (alternateSiteId.HasValue) Assert.Contains("Dirección alterna del soporte", signed);
            var xml = XDocument.Parse(signed);
            Assert.Equal(withTaxes ? 11_900m : 10_000m,
                (decimal)xml.Descendants(DianUblNamespaces.Cbc + "PayableAmount").Single());
            Assert.Equal(withTaxes ? 250m : 0m, xml.Root!.Elements(DianUblNamespaces.Cac + "WithholdingTaxTotal")
                .Sum(group => (decimal)group.Element(DianUblNamespaces.Cbc + "TaxAmount")!));
            if (withTaxes)
            {
                var applied = (await client.GetFromJsonAsync<AppliedWithholdingReportView>(
                    "/api/commerce/v1/taxation/withholdings/applied?from=2026-09-30&to=2026-09-30&page=1&pageSize=100"))!;
                var retained = Assert.Single(applied.Items.Where(item => item.DocumentId == documentId));
                Assert.Equal(250m, retained.Amount);
                Assert.Equal(AccountingManualDocumentTypes.ManualVoucher, retained.DocumentType);
                Assert.False(string.IsNullOrWhiteSpace(retained.DocumentNumber));
                Assert.NotEqual(retained.DocumentType, retained.DocumentTypeLabel);
                Assert.NotEqual(retained.Kind, retained.KindLabel);
                await AssertManualWithholdingReportVolumeAsync(documentId);
            }
            if (!withTaxes)
            {
                var freshExpense = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                    "5197", "Expense", "General", "Debit");
                var freshPayable = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
                    "2297", "Liability", "Payable", "Credit");
                async Task<VoucherDraftView> PrepareRelatedSupportAsync(Guid nextDocumentId)
                {
                    var identities = raw.Lines.ToDictionary(line => line.LineId!.Value, _ => Guid.NewGuid());
                    var sibling = raw with { VoucherId = nextDocumentId,
                        Lines = raw.Lines.Select(line => line with { LineId = identities[line.LineId!.Value],
                            AccountId = line.LineId == payableLineId ? freshPayable.AccountId : freshExpense.AccountId }).ToArray(),
                        FiscalCapture = new(capture.Acquisitions.Select(item => item with { SourceLineId = identities[item.SourceLineId] }).ToArray()) };
                    var reviewed = await accounting.PreviewFiscalVoucherAsync(identity, sibling);
                    var siblingCapture = draft with { DocumentId = sibling.VoucherId, RowVersion = null,
                        Lines = draft.Lines.Select(line => line with { LineId = identities[line.LineId!.Value],
                            AccountId = line.LineId == payableLineId ? freshPayable.AccountId : freshExpense.AccountId }).ToArray(),
                        FiscalCapture = sibling.FiscalCapture! with { CalculationHash = reviewed.CalculationHash } };
                    return await accounting.SaveVoucherDraftAsync(identity, siblingCapture);
                }
                // Both new sources occupy the same missing-key range. Their
                // auxiliaries have not yet been protected by another entry.
                var siblings = new[] {
                    await PrepareRelatedSupportAsync(Guid.Parse("fffffffd-ffff-ffff-ffff-ffffffffffff")),
                    await PrepareRelatedSupportAsync(Guid.Parse("fffffffe-ffff-ffff-ffff-ffffffffffff")) };
                using var rendezvous = new Barrier(2);
                var arrived = new System.Collections.Concurrent.ConcurrentDictionary<Guid, byte>();
                var siblingIds = siblings.Select(item => item.DocumentId).ToHashSet();
                using var synchronized = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    afterExecute: (command, _) => {
                        if (command.CommandText.Contains("SELECT d.TenantId,d.BusinessId,d.DocumentType,d.RowVersion,d.SentAt", StringComparison.Ordinal) &&
                            command.Parameters.Contains("@Id") && command.Parameters["@Id"].Value is Guid id &&
                            siblingIds.Contains(id) && arrived.TryAdd(id, 0))
                            Assert.True(rendezvous.SignalAndWait(TimeSpan.FromSeconds(20)),
                                "Both sends must reach their draft read before attempting fiscal reservation.");
                    });
                var concurrent = await Task.WhenAll(siblings.Select(item => client.PostAsJsonAsync(
                    $"/api/commerce/v1/accounting/manual/drafts/{item.DocumentId}/send", new SendVoucherDraftRequest(item.RowVersion))));
                try
                {
                    Assert.Equal(2, arrived.Count);
                    foreach (var response in concurrent)
                    {
                        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                        var value = (await response.Content.ReadFromJsonAsync<VoucherDraftView>())!;
                        Assert.Equal("Posted", value.Status);
                        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id", value.DocumentId));
                        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id", value.DocumentId));
                    }
                }
                finally { foreach (var response in concurrent) response.Dispose(); }

                var repeated = await PrepareRelatedSupportAsync(Guid.NewGuid());
                var cursorBeforeRepeat = await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", repeated.DocumentId);
                using var sameDraft = new Barrier(2);
                var attempts = 0;
                using var repeatFence = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    beforeExecute: command => {
                        // Both use cases have loaded the unsent draft before
                        // either acceptance acquires its canonical draft lock.
                        if (command.CommandText.Contains("SELECT d.TenantId,d.BusinessId,d.DocumentType,d.RowVersion,d.SentAt", StringComparison.Ordinal) &&
                            command.Parameters.Contains("@Id") && command.Parameters["@Id"].Value is Guid id &&
                            id == repeated.DocumentId && Interlocked.Increment(ref attempts) <= 2)
                            Assert.True(sameDraft.SignalAndWait(TimeSpan.FromSeconds(20)), "Both sends must capture the same unsent version.");
                    });
                var repeats = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync(
                    $"/api/commerce/v1/accounting/manual/drafts/{repeated.DocumentId}/send", new SendVoucherDraftRequest(repeated.RowVersion))));
                try
                {
                    Assert.Equal(2, attempts);
                    foreach (var response in repeats)
                        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
                    Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", repeated.DocumentId));
                    Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", repeated.DocumentId));
                    Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id", repeated.DocumentId));
                    Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id", repeated.DocumentId));
                    Assert.Equal(cursorBeforeRepeat + 1, await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", repeated.DocumentId));
                    Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM billing.TenantDianDocumentUsages WHERE SourceDocumentId=@Id", repeated.DocumentId));
                }
                finally { foreach (var response in repeats) response.Dispose(); }
            }
            await AssertManualSupportCancellationAsync(client, accounting, identity, sent, withTaxes);
        }
        finally
        {
            if (alternateSiteId.HasValue)
            {
                await using var site = new SqlCommand("UPDATE dbo.PartySites SET IsActive=0 WHERE PartySiteId=@Site;", connection);
                site.Parameters.AddWithValue("@Site", alternateSiteId.Value);
                await site.ExecuteNonQueryAsync();
            }
            await using var restore = new SqlCommand("UPDATE dbo.Suppliers SET PurchaseEvidencePolicy=@Policy WHERE SupplierId=@Id;", connection);
            restore.Parameters.AddWithValue("@Id", fixture.SupplierId);
            restore.Parameters.AddWithValue("@Policy", (object?)previousPolicy ?? DBNull.Value);
            await restore.ExecuteNonQueryAsync();
            if (previousProfile is null)
            {
                await using var remove = new SqlCommand("DELETE dbo.CounterpartyTaxProfiles WHERE TenantId=@Tenant AND CounterpartyId=@Supplier;", connection);
                remove.Parameters.AddWithValue("@Tenant", fixture.TenantId);
                remove.Parameters.AddWithValue("@Supplier", fixture.SupplierId);
                await remove.ExecuteNonQueryAsync();
            }
            else
            {
                using var restoreProfile = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                    new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, previousProfile.AppliesWithholding,
                        previousProfile.Responsibilities, previousProfile.JurisdictionCode));
                Assert.True(restoreProfile.IsSuccessStatusCode, await restoreProfile.Content.ReadAsStringAsync());
            }
            if (temporaryRule is not null && ruleRequest is not null)
            {
                using var deactivate = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/withholding-rules/{temporaryRule.RuleId}",
                    ruleRequest with { IsActive = false });
                Assert.True(deactivate.IsSuccessStatusCode, await deactivate.Content.ReadAsStringAsync());
            }
        }
    }
}
