using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Contracts.Purchasing;
using Auraly.Application.DocumentProcessing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GoodsReceiptCostPerformanceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Purchase cost association performance";
}

[Collection(GoodsReceiptCostPerformanceCollection.Name)]
public sealed class GoodsReceiptReferencedCostPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper costOutput)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Performance", "PurchaseCostAssociation")]
    public async Task Referenced_costs_keep_constant_trips_with_499_source_lines_and_twenty_products(bool unallocatedAsset)
    {
        var productId = fixture.ProductId;
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, AccountingPermissionCodes.PeriodsManage, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
            PurchasingPermissionCodes.ReadGoodsReceipts, PurchasingPermissionCodes.CreateGoodsReceipts, PurchasingPermissionCodes.ConfirmGoodsReceipts);
        using (var scope = fixture.CreateScope())
        {
            var accounting = scope.ServiceProvider.GetRequiredService<AccountingService>();
            var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
                new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
                    AccountingPermissionCodes.Activate, AccountingPermissionCodes.PeriodsManage });
            await accounting.EnsureDefaultsAsync(user);
            if (!(await accounting.ListPeriodsAsync(user)).Any(period => period.StartsOn <= new DateOnly(2026, 8, 2) && period.EndsOn >= new DateOnly(2026, 8, 2)))
                await accounting.CreatePeriodAsync(user, new(Guid.NewGuid(), fixture.TenantId,
                    new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "Período de volumen de costos"));
            if ((await accounting.GetReadinessAsync(user)).Status != AccountingActivationStatuses.Ready)
                await accounting.ActivateAsync(user, new(new DateOnly(2026, 1, 1), "COP", "ZeroDeclared"));
        }
        var sourceAccount = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            unallocatedAsset ? "1398" : "5197", unallocatedAsset ? "Asset" : "Expense",
            unallocatedAsset ? "UnallocatedPurchaseCost" : "General", "Debit");
        costOutput.WriteLine("Clasificación de origen: {0}.", unallocatedAsset ? "Costo por distribuir" : "Gasto");
        var payable = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client, "2297", "Liability", "Payable", "Credit");
        var at = new DateTimeOffset(2026, 8, 2, 9, 0, 0, TimeSpan.FromHours(-5));
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        foreach (var count in new[] { 1, 100, 499 })
        {
            var sourceId = Guid.NewGuid();
            var lines = Enumerable.Range(1, count).Select(number => new VoucherDraftLine(sourceAccount.AccountId,
                fixture.SupplierPartyId, null, $"Costo elegible {number}", 20_000m, 0, LineId: Guid.NewGuid()))
                .Append(new(payable.AccountId, fixture.SupplierPartyId, null, "Obligación original", 0, count * 20_000m,
                    LineId: Guid.NewGuid(), DueDate: at.AddDays(30))).ToArray();
            var source = new SaveVoucherDraftRequest(sourceId, AccountingManualDocumentTypes.ManualVoucher, at,
                "MANUAL_VOUCHER", "Origen de costos por lote", $"COST-{sourceId:N}", lines, null, null,
                MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
            var path = $"/api/commerce/v1/accounting/manual/drafts/{sourceId}";
            using var saved = await client.PutAsJsonAsync(path, source);
            Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
            var draft = (await saved.Content.ReadFromJsonAsync<VoucherDraftView>())!;
            using (var posted = await client.PostAsJsonAsync(path + "/send", new SendVoucherDraftRequest(draft.RowVersion)))
                Assert.True(posted.IsSuccessStatusCode, await posted.Content.ReadAsStringAsync());
            var entryId = await ScalarAsync<Guid>("SELECT EntryId FROM dbo.AccountingEntries WHERE SourceDocumentId=@Id AND SourceDocumentType=N'ManualAccountingVoucher'", sourceId);
            var acceptanceSamples = new List<double>();
            var processingSamples = new List<double>();
            var allCommandCounts = new List<int>();

            async Task ConfirmAsync(bool measured, List<double> samples)
            {
                var request = new ConfirmGoodsReceiptRequest(Guid.NewGuid(), fixture.BusinessId, fixture.WarehouseId,
                    fixture.SupplierId, $"PERF-{Guid.NewGuid():N}", at, at, true, at.AddDays(30), "COP", "Reparto por lote",
                    Enumerable.Range(1, 20).Select(number => new GoodsReceiptLineRequest(number, productId,
                        "Producto de reparto", 1, 1_000m, 0, "00", 0, PurchasingTaxTreatments.NotApplicable)).ToArray(),
                    ReferencedCostDocuments: [new(Guid.NewGuid(), entryId, GoodsReceiptCostAssociationModes.Distribute,
                        Enumerable.Range(1, count).Select(number => new GoodsReceiptVoucherCostLineRequest(number, 100m,
                            PurchaseCostAllocationMethods.Value)).ToArray())]);
                using var activity = new Activity("purchase-cost-acceptance").SetIdFormat(ActivityIdFormat.W3C).Start();
                var ownerCommands = 0;
                var commandTimes = new System.Collections.Concurrent.ConcurrentBag<(string Text, double Milliseconds)>();
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(database, beforeExecute: command =>
                {
                    if (command.CommandText.Contains("DECLARE @Requested TABLE(EntryId uniqueidentifier", StringComparison.Ordinal) ||
                        command.CommandText == "purchasing.GoodsReceiptCostDocumentsInsert")
                        Interlocked.Increment(ref ownerCommands);
                }, afterExecute: (command, milliseconds) => commandTimes.Add((
                    string.Concat(command.CommandText.Take(180)).ReplaceLineEndings(" "), milliseconds)));
                var timer = Stopwatch.StartNew();
                using var message = new HttpRequestMessage(HttpMethod.Post, "/api/commerce/v1/goods-receipts/confirm") { Content = JsonContent.Create(request) };
                message.Headers.Add("Idempotency-Key", request.DocumentId.ToString("N"));
                // The real transport accepts the wakeup without running workers
                // inside HTTP. Measure that boundary, then run the same workers
                // explicitly and measure their separate processing stage.
                fixture.PauseDocumentProcessing();
                HttpResponseMessage response;
                try { response = await client.SendAsync(message); }
                finally { fixture.ResumeDocumentProcessing(); }
                using var responseLifetime = response;
                timer.Stop();
                Assert.True(response.StatusCode == HttpStatusCode.Accepted, await response.Content.ReadAsStringAsync());
                if (measured)
                {
                    Assert.True(counter.Count > 0);
                    Assert.Equal(2, ownerCommands);
                    allCommandCounts.Add(counter.Count);
                    samples.Add(timer.Elapsed.TotalMilliseconds);
                    if (timer.Elapsed.TotalMilliseconds >= 2_000)
                        costOutput.WriteLine($"{count} partidas, total {timer.Elapsed.TotalMilliseconds:F1} ms, SQL acumulado {commandTimes.Sum(item => item.Milliseconds):F1} ms: " +
                            string.Join(" | ", commandTimes.OrderByDescending(item => item.Milliseconds).Take(4)
                                .Select(item => $"{item.Milliseconds:F1} ms {item.Text}")));
                }
                var signal = Assert.Single(fixture.DrainDocumentSignals());
                using (var scope = fixture.CreateScope())
                {
                    var processingTimes = new System.Collections.Concurrent.ConcurrentBag<(string Text, double Milliseconds)>();
                    using var processingCounter = new InventoryBalanceProcessingTests.CommandCounter(database,
                        afterExecute: (command, milliseconds) => processingTimes.Add((
                            string.Concat(command.CommandText.Take(180)).ReplaceLineEndings(" "), milliseconds)));
                    timer.Restart();
                    await scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>().ProcessOneAsync(signal);
                    timer.Stop();
                    if (measured) processingSamples.Add(timer.Elapsed.TotalMilliseconds);
                    if (measured && timer.Elapsed.TotalMilliseconds >= 2_000)
                        costOutput.WriteLine($"Motor {count} partidas, total {timer.Elapsed.TotalMilliseconds:F1} ms, SQL acumulado {processingTimes.Sum(item => item.Milliseconds):F1} ms: " +
                            string.Join(" | ", processingTimes.OrderByDescending(item => item.Milliseconds).Take(4)
                                .Select(item => $"{item.Milliseconds:F1} ms {item.Text}")));
                }
                Assert.Equal("Processed", await ScalarAsync<string>("SELECT Status FROM dbo.GoodsReceipts WHERE GoodsReceiptId=@Id", request.DocumentId));
                Assert.Equal(count * 20, await ScalarAsync<int>("""
                    SELECT COUNT(*) FROM purchasing.GoodsReceiptCostAllocations WHERE CostDocumentId=@Id
                    """, request.ReferencedCostDocuments![0].AssociationId));
            }

            await ConfirmAsync(false, acceptanceSamples);
            for (var sample = 0; sample < 20; sample++) await ConfirmAsync(true, acceptanceSamples);
            acceptanceSamples.Sort();
            processingSamples.Sort();
            costOutput.WriteLine($"{count} partidas de origen / 20 productos: 2 SQL del cargo, {allCommandCounts.Min()}–{allCommandCounts.Max()} SQL de aceptación, aceptación p95 {acceptanceSamples[18]:F1} ms.");
            Assert.True(acceptanceSamples[18] < 2_000, $"{count} partidas: p95 {acceptanceSamples[18]:F1} ms.");
            costOutput.WriteLine($"{count} partidas: motores documental/contable p95 {processingSamples[18]:F1} ms.");
            Assert.True(processingSamples[18] < 2_000, $"Procesamiento de {count} partidas: p95 {processingSamples[18]:F1} ms.");
            var readSamples = new List<double>();
            for (var sample = 0; sample < 20; sample++)
            {
                using var activity = new Activity("purchase-cost-voucher-read").SetIdFormat(ActivityIdFormat.W3C).Start();
                using var counter = new InventoryBalanceProcessingTests.CommandCounter(database,
                    commandFilter: command => command.CommandText.Contains("DECLARE @ScopedEntryId uniqueidentifier", StringComparison.Ordinal));
                var timer = Stopwatch.StartNew();
                var voucher = (await client.GetFromJsonAsync<GoodsReceiptCostVoucher>($"/api/commerce/v1/goods-receipts/cost-vouchers/{entryId}"))!;
                timer.Stop();
                Assert.Equal(1, counter.Count);
                Assert.Equal(count * 2_100m, voucher.Lines.Sum(line => line.AssignedFunctionalAmount));
                readSamples.Add(timer.Elapsed.TotalMilliseconds);
            }
            readSamples.Sort();
            costOutput.WriteLine($"{count} partidas: consulta p95 {readSamples[18]:F1} ms.");
            Assert.True(readSamples[18] < 800, $"Consulta de {count} partidas: p95 {readSamples[18]:F1} ms.");
            Assert.Equal(count * 20_000m, await ScalarAsync<decimal>("SELECT OutstandingAmount FROM dbo.Payables WHERE SourceDocumentId=@Id AND SourceDocumentType=N'ManualAccountingVoucher'", sourceId));
        }
    }

    private async Task<T> ScalarAsync<T>(string sql, Guid id)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T));
    }
}
