using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Application.Expenses;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class ExpenseCaptureTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task Direct_accounts_preview_post_replay_and_cancel_without_a_hidden_concept()
    {
        using var client = fixture.CreateAdminClient(ExpensePermissionCodes.Read, ExpensePermissionCodes.Create,
            ExpensePermissionCodes.Cancel, TaxationPermissionCodes.ManageWithholdingRules, TaxationPermissionCodes.ViewWithholdingRules);
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new SqlCommand("""
                IF NOT EXISTS(SELECT 1 FROM dbo.TaxProfiles WHERE TenantId=@Tenant AND DianTaxCode=N'01' AND Rate=19 AND IsActive=1)
                  INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,DianTaxCode,Name,Rate,IsActive,CreatedAt)
                  VALUES(NEWID(),@Tenant,N'EXP-IVA19',N'01',N'IVA 19%',19,1,SYSDATETIMEOFFSET());
                """, connection);
            command.Parameters.AddWithValue("@Tenant", fixture.TenantId);
            await command.ExecuteNonQueryAsync();
        }
        var options = (await client.GetFromJsonAsync<ExpenseWorkspaceOptions>("/api/commerce/v1/expenses/options"))!;
        var account = options.ExpenseAccounts.First();
        var tax = options.Taxes!.First(tax => tax.Rate == 19m);
        using var previousResponse = await client.GetAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}");
        var previous = previousResponse.StatusCode == HttpStatusCode.NotFound ? null :
            await previousResponse.Content.ReadFromJsonAsync<CounterpartyTaxProfileView>();
        var classification = "EXP-" + Guid.NewGuid().ToString("N")[..12];
        var rule = new SaveWithholdingRuleRequest(fixture.BusinessId, classification, "Retención gastos de prueba",
            "IncomeTax", "Purchase", "Accrual", "TaxExclusiveAmount", classification, null, 2.5m,
            100_000m, [], new DateOnly(2026, 1, 1), null, true);
        using var ruleResponse = await client.PostAsJsonAsync("/api/commerce/v1/taxation/withholding-rules", rule);
        ruleResponse.EnsureSuccessStatusCode();
        var savedRule = (await ruleResponse.Content.ReadFromJsonAsync<WithholdingRuleView>())!;
        try
        {
            using (var profile = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId, true, [], "11001")))
                profile.EnsureSuccessStatusCode();
            var date = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.FromHours(-5));
            var lines = new[] {
                new ExpenseLineInput(account.AccountId, null, options.CostCenters.First().CostCenterId,
                    "Servicio distribuido A", 60_000m, tax.TaxProfileId, "DeductibleInputVat", classification),
                new ExpenseLineInput(account.AccountId, null, null,
                    "Servicio distribuido B", 60_000m, tax.TaxProfileId, "CapitalizedCost", classification) };
            var request = new ConfirmExpenseRequest(Guid.NewGuid(), fixture.BusinessId, fixture.SupplierId, null,
                null, "EXP-" + Guid.NewGuid().ToString("N"), date, date.AddDays(30), "COP", "Gasto directo",
                0, 0, null, null, Lines: lines);
            using var creator = fixture.CreateAdminClient(ExpensePermissionCodes.Create);
            using var previewResponse = await creator.PostAsJsonAsync("/api/commerce/v1/expenses/preview", request);
            Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
            var preview = (await previewResponse.Content.ReadFromJsonAsync<ExpensePreview>())!;
            Assert.True(preview.CanConfirm);
            Assert.Equal(3_000m, preview.Withholding.WithholdingTotal);
            Assert.Equal(142_800m, preview.Withholding.GrossAmount);
            Assert.Equal(139_800m, preview.Withholding.NetAmount);

            // One batch resolution + rules + profiles, independent of the number of lines.
            using var scope = fixture.Services.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ExpenseService>();
            var identity = new ExpenseUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { ExpensePermissionCodes.Create, ExpensePermissionCodes.Read });
            await service.PreviewAsync(identity, request);
            int? acceptanceCommands = null;
            foreach (var size in new[] { 1, 20, 100 })
            {
                var batch = request with { ExpenseId = Guid.NewGuid(), SupplierDocumentNumber = Guid.NewGuid().ToString("N"),
                    Lines = Enumerable.Range(0, size).Select(_ => lines[0] with { TaxExclusiveAmount = 120_000m / size }).ToArray() };
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog);
                var watch = Stopwatch.StartNew();
                var calculated = await service.PreviewAsync(identity, batch);
                watch.Stop();
                output.WriteLine($"Preview {size} líneas: {counter.Count} SQL, {watch.ElapsedMilliseconds} ms.");
                Assert.Equal(3, counter.Count);
                Assert.True(watch.ElapsedMilliseconds < (size == 100 ? 1500 : 800));
                Assert.Equal(3_000m, calculated.Withholding.WithholdingTotal);
                var before = counter.Count;
                watch.Restart();
                await service.ConfirmAsync(identity, batch.ExpenseId.ToString("N"), batch with { CalculationHash = calculated.CalculationHash });
                watch.Stop();
                var commands = counter.Count - before;
                output.WriteLine($"Confirmación {size} líneas, incluido motor financiero: {commands} SQL, {watch.ElapsedMilliseconds} ms.");
                acceptanceCommands ??= commands;
                // More lines must never increase the SQL budget of the complete
                // financial operation (including first-use initialization).
                Assert.InRange(commands, 1, acceptanceCommands.Value);
                Assert.True(watch.ElapsedMilliseconds < (size == 100 ? 3000 : 2000));
                Assert.Equal("Processed", (await service.GetAsync(identity, batch.ExpenseId))!.Status);
            }
            using (var noPreview = await SendAsync(client, request)) Assert.Equal(HttpStatusCode.Conflict, noPreview.StatusCode);
            request = request with { CalculationHash = preview.CalculationHash };
            var watchAccept = Stopwatch.StartNew();
            using (var acceptedResponse = await SendAsync(client, request))
            {
                watchAccept.Stop();
                Assert.True(acceptedResponse.StatusCode == HttpStatusCode.Accepted, await acceptedResponse.Content.ReadAsStringAsync());
            }
            output.WriteLine($"Confirmación con contabilización: {watchAccept.ElapsedMilliseconds} ms.");
            Assert.True(watchAccept.ElapsedMilliseconds < 2000);
            var detail = (await client.GetFromJsonAsync<ExpenseDetail>($"/api/commerce/v1/expenses/{request.ExpenseId}"))!;
            Assert.Null(detail.ConceptId);
            Assert.Equal(2, detail.Lines!.Count);
            Assert.Equal(139_800m, detail.Payable!.OriginalAmount);
            Assert.Equal(3_000m, detail.Withholding!.WithholdingTotal);
            Assert.Equal("Processed", detail.Status);
            var page = (await client.GetFromJsonAsync<ExpensePage>($"/api/commerce/v1/expenses?search={request.SupplierDocumentNumber}&page=1&pageSize=1"))!;
            Assert.Equal(request.ExpenseId, Assert.Single(page.Items).ExpenseId);
            await using (var connection = new SqlConnection(fixture.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new SqlCommand("""
                    SELECT SUM(l.Debit),SUM(l.Credit),SUM(CASE WHEN l.AccountId=@Account THEN l.Debit ELSE 0 END)
                    FROM dbo.AccountingEntryLines l JOIN dbo.AccountingEntries e ON e.EntryId=l.EntryId
                    WHERE e.SourceDocumentId=@Expense AND e.SourceDocumentType=N'Expense';
                    """, connection);
                command.Parameters.AddWithValue("@Account", account.AccountId);
                command.Parameters.AddWithValue("@Expense", request.ExpenseId);
                await using var reader = await command.ExecuteReaderAsync(); await reader.ReadAsync();
                Assert.Equal(142_800m, reader.GetDecimal(0)); Assert.Equal(reader.GetDecimal(0), reader.GetDecimal(1));
                Assert.Equal(131_400m, reader.GetDecimal(2));
            }
            // Current tax configuration cannot change an accepted replay.
            using (var changed = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/withholding-rules/{savedRule.RuleId}", rule with { Rate = 3m }))
                changed.EnsureSuccessStatusCode();
            using (var replay = await SendAsync(client, request))
            {
                replay.EnsureSuccessStatusCode(); Assert.True((await replay.Content.ReadFromJsonAsync<ExpenseAcceptance>())!.IdempotentReplay);
            }
            using (var stale = await SendAsync(client, request with { ExpenseId = Guid.NewGuid(), SupplierDocumentNumber = Guid.NewGuid().ToString("N") }))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using (var cancel = await client.PostAsJsonAsync($"/api/commerce/v1/expenses/{request.ExpenseId}/cancel",
                new CancelExpenseRequest(Guid.NewGuid(), "Servicio no realizado")))
                Assert.True(cancel.IsSuccessStatusCode, await cancel.Content.ReadAsStringAsync());
            var cancelled = (await client.GetFromJsonAsync<ExpenseDetail>($"/api/commerce/v1/expenses/{request.ExpenseId}"))!;
            Assert.Equal("Cancelled", cancelled.Status);
            Assert.Equal(0m, cancelled.Payable!.OutstandingAmount);
            using (var internalVat = await client.PostAsJsonAsync("/api/commerce/v1/expenses/preview", request with { PurchaseEvidenceType = "InternalReceiptVoucher" }))
                Assert.Equal(HttpStatusCode.BadRequest, internalVat.StatusCode);
            using (var legacyInternalVat = await SendAsync(client, request with { ExpenseId=Guid.NewGuid(), Lines=null,
                ConceptId=Guid.NewGuid(), TaxExclusiveAmount=100_000m,VatAmount=19_000m,
                PurchaseEvidenceType="InternalReceiptVoucher",CalculationHash=null }))
            {
                Assert.Equal(HttpStatusCode.BadRequest, legacyInternalVat.StatusCode);
                var problem = await legacyInternalVat.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                Assert.Contains("Registra este comprobante interno por líneas", problem.GetProperty("detail").GetString());
            }
            using (var foreignAccount = await client.PostAsJsonAsync("/api/commerce/v1/expenses/preview", request with { Lines = [lines[0] with { ExpenseAccountId = Guid.NewGuid() }] }))
                Assert.Equal(HttpStatusCode.BadRequest, foreignAccount.StatusCode);
            using (var forbidden = fixture.CreateAdminClient(ExpensePermissionCodes.Read))
            using (var response = await forbidden.PostAsJsonAsync("/api/commerce/v1/expenses/preview", request))
                Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        finally
        {
            using var deactivate = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/withholding-rules/{savedRule.RuleId}", rule with { IsActive = false });
            deactivate.EnsureSuccessStatusCode();
            using var restore = await client.PutAsJsonAsync($"/api/commerce/v1/taxation/counterparty-profiles/{fixture.SupplierId}",
                new SaveCounterpartyTaxProfileRequest(fixture.BusinessId, fixture.SupplierId,
                    previous?.AppliesWithholding ?? false, previous?.Responsibilities ?? [], previous?.JurisdictionCode));
            restore.EnsureSuccessStatusCode();
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, ConfirmExpenseRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/expenses/confirm") { Content = JsonContent.Create(request) };
        message.Headers.Add("Idempotency-Key", request.ExpenseId.ToString("N"));
        return await client.SendAsync(message);
    }
}
