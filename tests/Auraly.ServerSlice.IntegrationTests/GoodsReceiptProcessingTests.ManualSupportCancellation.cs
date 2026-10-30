using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Xml.Linq;
using Auraly.Application.Fiscal;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Fiscal;
using Auraly.Fiscal.Ubl;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class GoodsReceiptProcessingTests
{
    private async Task AssertManualSupportCancellationAsync(HttpClient client, AccountingService accounting,
        AccountingUserIdentity identity, VoucherDraftView support, bool withTaxes)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        ConfirmManualAccountingVoucherRequest original;
        Guid payableId, siteId, entryId;
        var lines = new List<ManualVoucherLineRequest>();
        await using (var read = new SqlCommand("""
            SELECT source.PayloadJson,payable.PayableId,payable.PartySiteId,payable.OpeningEntryId
            FROM dbo.AccountingSourceDocuments source JOIN dbo.Payables payable
              ON payable.SourceDocumentId=source.SourceDocumentId AND payable.SourceDocumentType=source.SourceDocumentType
            WHERE source.SourceDocumentId=@Id AND source.SourceDocumentType=N'ManualAccountingVoucher';
            SELECT line.SourceLineId,line.AccountId,line.PartyId,line.CostCenterId,line.Debit,line.Credit,line.BaseAmount
            FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'ManualAccountingVoucher' ORDER BY line.LineNumber;
            """, connection))
        {
            read.Parameters.AddWithValue("@Id", support.DocumentId);
            await using var reader = await read.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            original = JsonSerializer.Deserialize<ConfirmManualAccountingVoucherRequest>(reader.GetString(0))!;
            payableId = reader.GetGuid(1); siteId = reader.GetGuid(2); entryId = reader.GetGuid(3);
            var payableLineId = Assert.Single(original.FinancialEffects!.Positions).SourceLineId;
            await reader.NextResultAsync();
            while (await reader.ReadAsync())
                lines.Add(new(reader.GetGuid(1), reader.IsDBNull(2) ? null : reader.GetGuid(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3), "Anulación del soporte", reader.GetDecimal(5),
                    reader.GetDecimal(4), reader.GetGuid(0), reader.IsDBNull(6) ? null : -reader.GetDecimal(6),
                    ExistingPosition: reader.GetGuid(0) == payableLineId ? new("Payable", payableId) : null,
                    PartySiteId: reader.GetGuid(0) == payableLineId ? siteId : null));
        }
        var cancellation = original with { VoucherId = Guid.NewGuid(), OccurredAt = original.OccurredAt.AddDays(1),
            VoucherTypeCode = "SupportDocumentCancellation", Description = "Anulación total del soporte manual",
            Reference = null, Lines = lines, FinancialEffects = null, FiscalCapture = new([]) };
        var notAccepted = await accounting.PreviewFiscalVoucherAsync(identity, cancellation);
        Assert.False(notAccepted.CanConfirm);
        Assert.Contains(notAccepted.Diagnostics, issue => issue.Contains("DIAN"));
        using (var noFiscalAcceptance = await client.GetAsync($"/api/commerce/v1/accounting/support-cancellation-capture?payableId={payableId}"))
        {
            Assert.Equal(HttpStatusCode.BadRequest, noFiscalAcceptance.StatusCode);
            Assert.Contains("DIAN", await noFiscalAcceptance.Content.ReadAsStringAsync());
        }
        using (var forbidden = fixture.CreateAdminClient(AccountingPermissionCodes.Read))
        using (var response = await forbidden.GetAsync($"/api/commerce/v1/accounting/support-cancellation-capture?payableId={payableId}"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var captureOnly = fixture.CreateAdminClient(AccountingPermissionCodes.ManualCreate))
        using (var forbiddenReview = await captureOnly.PostAsJsonAsync("/api/commerce/v1/accounting/manual/fiscal-preview", cancellation))
            Assert.Equal(HttpStatusCode.Forbidden, forbiddenReview.StatusCode);
        await Assert.ThrowsAsync<AccountingValidationException>(() => accounting.GetSupportCancellationCaptureAsync(
            identity with { TenantId = Guid.NewGuid() }, payableId));
        using (var invalidKind = await client.GetAsync("/api/commerce/v1/accounting/open-positions?subledgerKind=Receivable&manualSupportOnly=true&page=1&pageSize=1"))
            Assert.Equal(HttpStatusCode.BadRequest, invalidKind.StatusCode);
        await using (var accepted = new SqlCommand("""
            UPDATE dbo.FiscalDocuments SET FiscalStatus=N'DianAccepted'
            WHERE DocumentId=@Id AND FiscalDocumentType=N'SupportDocument' AND UniqueCode IS NOT NULL;
            """, connection))
        {
            accepted.Parameters.AddWithValue("@Id", support.DocumentId);
            Assert.Equal(1, await accepted.ExecuteNonQueryAsync());
        }
        using (var prepare = await client.GetAsync($"/api/commerce/v1/accounting/support-cancellation-capture?payableId={payableId}"))
        {
            Assert.True(prepare.IsSuccessStatusCode, await prepare.Content.ReadAsStringAsync());
            var prepared = (await prepare.Content.ReadFromJsonAsync<VoucherDraftPreparation>())!;
            Assert.Equal(original.MainPartyId, prepared.MainPartyId);
            Assert.Equal(lines.Count, prepared.Lines.Count);
            cancellation = cancellation with { Description = prepared.Description, Reference = prepared.Reference,
                Lines = prepared.Lines.Select(line => new ManualVoucherLineRequest(line.Value.AccountId!.Value,
                    line.Value.PartyId, line.Value.CostCenterId, line.Value.Description, line.Value.Debit, line.Value.Credit,
                    line.Value.LineId, line.Value.BaseAmount, line.Value.ExistingPosition, line.Value.FinancialGroupId,
                    line.Value.DueDate, line.Value.PartySiteId, line.Value.PaymentEvidence, line.Value.Reference)).ToArray() };
        }
        var preview = await accounting.PreviewFiscalVoucherAsync(identity, cancellation);
        Assert.True(preview.CanConfirm, string.Join("; ", preview.Diagnostics));
        Assert.Equal(original.FinancialEffects!.Positions.Single().Amount, preview.Withholding.NetAmount);
        foreach (var step in new[] { "Capture", "Review" })
        {
            var samples = new List<double>();
            for (var sample = 0; sample <= 20; sample++)
            {
                using var activity = new System.Diagnostics.Activity("SupportCancellation." + step).Start();
                using var count = new InventoryBalanceProcessingTests.CommandCounter(connection.Database, activity.TraceId);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                if (step == "Capture")
                    Assert.Equal(lines.Count, (await accounting.GetSupportCancellationCaptureAsync(identity, payableId)).Lines.Count);
                else
                    Assert.True((await accounting.PreviewFiscalVoucherAsync(identity, cancellation)).CanConfirm);
                watch.Stop();
                Assert.Equal(step == "Capture" ? 1 : 2, count.Count);
                if (sample > 0) samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            var p95 = samples.Order().ElementAt(18);
            output.WriteLine("Support cancellation {0}, {1} lines: {2} SQL, 20 samples, p95 {3:F1} ms.",
                step, lines.Count, step == "Capture" ? 1 : 2, p95);
            Assert.True(p95 < 1500, $"Support cancellation {step} exceeded 1.5 s: {p95:F1} ms.");
        }
        var positions = (await client.GetFromJsonAsync<AccountingOpenPositionPage>(
            $"/api/commerce/v1/accounting/open-positions?subledgerKind=Payable&manualSupportOnly=true&partyId={original.MainPartyId}&page=1&pageSize=1"))!;
        Assert.NotEmpty(positions.Items);
        Assert.All(positions.Items, item => Assert.Equal("ManualAccountingVoucher", item.SourceDocumentType));
        Assert.True(positions.TotalCount >= positions.Items.Count);
        var changed = cancellation with { Lines = cancellation.Lines.Select((line, index) => index == 0
            ? line with { CostCenterId = null } : line).ToArray() };
        // The original uses its resolved center, not today's default or the
        // nullable center captured before the original was posted.
        if (cancellation.Lines[0].CostCenterId is not null)
            Assert.False((await accounting.PreviewFiscalVoucherAsync(identity, changed)).CanConfirm);
        var draft = new SaveVoucherDraftRequest(cancellation.VoucherId, "ManualAccountingVoucher", cancellation.OccurredAt,
            cancellation.ConceptCode, cancellation.Description, cancellation.Reference, cancellation.Lines.Select(line => new VoucherDraftLine(
                line.AccountId, line.PartyId, line.CostCenterId, line.Description, line.Debit, line.Credit,
                line.Reference, line.LineId, line.BaseAmount, line.ExistingPosition, line.FinancialGroupId,
                line.DueDate, line.PartySiteId, line.PaymentEvidence)).ToArray(), null, null, cancellation.MainPartyId,
            PayloadVersion: 4, VoucherTypeCode: cancellation.VoucherTypeCode,
            FiscalCapture: cancellation.FiscalCapture! with { CalculationHash = preview.CalculationHash });
        var saved = await accounting.SaveVoucherDraftAsync(identity, draft);
        var path = $"/api/commerce/v1/accounting/manual/drafts/{saved.DocumentId}/send";
        if (!withTaxes)
        {
            var trigger = $"TestCancellationPostingFailure_{Guid.NewGuid():N}";
            await using (var create = new SqlCommand($"""
                CREATE TRIGGER dbo.[{trigger}] ON dbo.AccountingEntries AFTER INSERT AS
                BEGIN
                  IF EXISTS(SELECT 1 FROM inserted WHERE SourceDocumentId='{saved.DocumentId:D}')
                    THROW 51997,'Injected support cancellation journal failure.',1;
                END;
                """, connection)) await create.ExecuteNonQueryAsync();
            try
            {
                using var pending = await client.PostAsJsonAsync(path, new SendVoucherDraftRequest(saved.RowVersion));
                Assert.Equal(HttpStatusCode.InternalServerError, pending.StatusCode);
                var accepted = (await client.GetFromJsonAsync<VoucherDraftView>(path[..^5]))!;
                Assert.NotEqual("Posted", accepted.Status);
                Assert.NotNull(accepted.SentAt);
                Assert.Empty(fixture.DrainFiscalSignals().Where(item => item.Signal.DocumentId == saved.DocumentId
                    && item.Signal.Stage == FiscalProcessingStage.Generation));
                Assert.Equal(preview.Withholding.NetAmount,
                    await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id", saved.DocumentId));
                Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM accounting.SourcePositionIndex WHERE SourceDocumentId=@Id AND EffectKind=N'Cancellation'", saved.DocumentId));
                var increaseId = Guid.NewGuid();
                var increase = new ConfirmAccountAdjustmentRequest(increaseId, fixture.BusinessId, "Payable", payableId,
                    "Increase", 1m, lines.First(line => line.Credit > 0).AccountId, null, cancellation.OccurredAt,
                    "ACCOUNT_ADJUSTMENT", "Ajuste competidor de una anulación pendiente");
                using var blocked = await client.PostAsJsonAsync("/api/commerce/v1/accounting/manual/account-adjustments", increase);
                Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
                Assert.Contains("anulación", await blocked.Content.ReadAsStringAsync());
                Assert.Equal(0, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id", increaseId));
            }
            finally
            {
                await using var drop = new SqlCommand($"DROP TRIGGER dbo.[{trigger}];", connection);
                await drop.ExecuteNonQueryAsync();
            }
            using var retryClient = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Retry);
            using var retry = await retryClient.PostAsync($"/api/commerce/v1/accounting/postings/{saved.DocumentId}/retry", null);
            Assert.True(retry.IsSuccessStatusCode, await retry.Content.ReadAsStringAsync());
        }
        if (withTaxes)
        {
            var cursor = await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", saved.DocumentId);
            var trigger = $"TestCancellationFiscalFailure_{Guid.NewGuid():N}";
            await using (var create = new SqlCommand($"""
                CREATE TRIGGER fiscal.[{trigger}] ON fiscal.PurchaseSupportFiscalSnapshots AFTER INSERT AS
                BEGIN
                  IF EXISTS(SELECT 1 FROM inserted WHERE DocumentId='{saved.DocumentId:D}')
                    THROW 51997,'Injected manual support adjustment failure.',1;
                END;
                """, connection)) await create.ExecuteNonQueryAsync();
            try
            {
                using var failed = await client.PostAsJsonAsync(path, new SendVoucherDraftRequest(saved.RowVersion));
                Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
                Assert.Equal(0, await ScalarAsync<int>("""
                    SELECT (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments WHERE SourceDocumentId=@Id)
                      +(SELECT COUNT(*) FROM dbo.AccountingPostingJobs WHERE SourceDocumentId=@Id)
                      +(SELECT COUNT(*) FROM accounting.SourcePositionIndex WHERE SourceDocumentId=@Id)
                      +(SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id)
                      +(SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id)
                      +(SELECT COUNT(*) FROM dbo.FiscalDocuments WHERE DocumentId=@Id)
                      +(SELECT COUNT(*) FROM billing.TenantDianDocumentUsages WHERE SourceDocumentId=@Id);
                    """, saved.DocumentId));
                Assert.Equal(cursor, await ScalarAsync<long>("SELECT COALESCE(SUM(NextConsecutive),0) FROM dbo.FiscalSeriesCursors", saved.DocumentId));
                Assert.Equal(preview.Withholding.NetAmount,
                    await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
                var stillDraft = (await client.GetFromJsonAsync<VoucherDraftView>(path[..^5]))!;
                Assert.Null(stillDraft.SentAt);
                Assert.Equal(saved.RowVersion, stillDraft.RowVersion);
            }
            finally
            {
                await using var drop = new SqlCommand($"DROP TRIGGER fiscal.[{trigger}];", connection);
                await drop.ExecuteNonQueryAsync();
            }
        }
        using var send = await client.PostAsJsonAsync(path, new SendVoucherDraftRequest(saved.RowVersion));
        Assert.True(send.IsSuccessStatusCode, await send.Content.ReadAsStringAsync());
        var sent = (await send.Content.ReadFromJsonAsync<VoucherDraftView>())!;
        Assert.Equal("Posted", sent.Status);
        Assert.Equal("SupportDocumentAdjustment", sent.Row.FiscalDocumentType);
        Assert.Equal(0m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal("Cancelled", await ScalarAsync<string>("SELECT Status FROM dbo.Payables WHERE PayableId=@Id", payableId));
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.PayableTransactions WHERE SourceDocumentId=@Id AND TransactionType=N'Credit'", saved.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT (SELECT COUNT(*) FROM dbo.Payables WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.SupplierPayments WHERE PaymentId=@Id)
              +(SELECT COUNT(*) FROM dbo.DocumentWithholdingSnapshots WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.DocumentProcessingJobs WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id);
            """, saved.DocumentId));
        Assert.Equal(lines.Count, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.AccountingEntries entry JOIN dbo.AccountingEntryLines line ON line.EntryId=entry.EntryId
            WHERE entry.SourceDocumentId=@Id AND entry.SourceDocumentType=N'ManualAccountingVoucher';
            """, saved.DocumentId));
        Assert.Equal(0, await ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.AccountingEntries original
            JOIN dbo.AccountingEntryLines prior ON prior.EntryId=original.EntryId
            JOIN dbo.AccountingEntries cancellation ON cancellation.SourceDocumentId=@Id
            JOIN dbo.AccountingEntryLines reversed ON reversed.EntryId=cancellation.EntryId AND reversed.SourceLineId=prior.SourceLineId
            WHERE original.SourceDocumentId=(SELECT payable.SourceDocumentId FROM accounting.SourcePositionIndex target
              JOIN dbo.Payables payable ON payable.PayableId=target.SubledgerId
              WHERE target.SourceDocumentId=@Id AND target.EffectKind=N'Cancellation')
              AND (reversed.AccountId<>prior.AccountId OR reversed.Debit<>prior.Credit OR reversed.Credit<>prior.Debit
                OR ISNULL(reversed.BaseAmount,0)<>-ISNULL(prior.BaseAmount,0));
            """, saved.DocumentId));
        Assert.Equal(FiscalDocumentStatusCodes.AwaitingManualSubmission,
            await ScalarAsync<string>("SELECT Status FROM dbo.FiscalDocumentProcesses WHERE DocumentId=@Id", saved.DocumentId));
        using (var submitted = await client.PostAsJsonAsync(
            $"/api/commerce/v1/fiscal/documents/{saved.DocumentId}/send-manual-support", new { }))
            Assert.True(submitted.IsSuccessStatusCode, await submitted.Content.ReadAsStringAsync());
        await GenerateFiscalAsync(saved.DocumentId);
        var fiscal = await ReadFiscalGenerationAsync(saved.DocumentId);
        Assert.Equal(FiscalDocumentStatusCodes.PendingSubmission, fiscal.Status);
        var xml = XDocument.Parse(await ReadArtifactTextAsync(saved.DocumentId, "SignedXml"));
        Assert.Equal("95", xml.Root!.Element(DianUblNamespaces.Cbc + "CreditNoteTypeCode")!.Value);
        Assert.Empty(xml.Root.Elements(DianUblNamespaces.Cac + "WithholdingTaxTotal"));
        Assert.Equal(withTaxes ? 11_900m : 10_000m, (decimal)xml.Descendants(DianUblNamespaces.Cbc + "PayableAmount").Single());
        Assert.Equal(await ScalarAsync<string>("SELECT UniqueCode FROM dbo.FiscalDocuments WHERE DocumentId=@Id", support.DocumentId),
            xml.Descendants(DianUblNamespaces.Cac + "BillingReference")
            .Descendants(DianUblNamespaces.Cbc + "UUID").Single().Value);
        using var replay = await client.PostAsJsonAsync(path, new SendVoucherDraftRequest(saved.RowVersion));
        Assert.True(replay.IsSuccessStatusCode, await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, await ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id", saved.DocumentId));
        Assert.Equal(1, fixture.DrainFiscalSignals().Count(item => item.Signal.DocumentId == saved.DocumentId
            && item.Signal.Stage == FiscalProcessingStage.Generation));
        Assert.Equal(0m, await ScalarAsync<decimal>("""
            SELECT SUM(EligibleFunctionalAmount) FROM purchasing.ReceiptCostVoucherLines(@Id) WHERE Debit>0;
            """, entryId));
        var another = cancellation with { VoucherId = Guid.NewGuid() };
        Assert.False((await accounting.PreviewFiscalVoucherAsync(identity, another)).CanConfirm);
        if (withTaxes)
        {
            var priorPeriod = (await client.GetFromJsonAsync<Auraly.Commerce.Taxation.Contracts.AppliedWithholdingReportView>(
                "/api/commerce/v1/taxation/withholdings/applied?from=2026-09-30&to=2026-09-30&page=1&pageSize=100"))!;
            Assert.Contains(priorPeriod.Items, line => line.DocumentId == support.DocumentId);
            var cancelledPeriod = (await client.GetFromJsonAsync<Auraly.Commerce.Taxation.Contracts.AppliedWithholdingReportView>(
                "/api/commerce/v1/taxation/withholdings/applied?from=2026-09-30&to=2026-10-01&page=1&pageSize=100"))!;
            Assert.DoesNotContain(cancelledPeriod.Items, line => line.DocumentId == support.DocumentId);
        }
    }
}
