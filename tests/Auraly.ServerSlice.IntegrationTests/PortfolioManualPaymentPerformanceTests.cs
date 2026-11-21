using System.Diagnostics;
using System.Net.Http.Json;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.Payables;
using Auraly.Contracts.Receivables;
using Auraly.Application.Payables;
using Auraly.Application.WorkSessions;
using Auraly.Contracts.WorkSessions;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PortfolioManualPaymentPerformanceCollection : ICollectionFixture<ServerSliceFixture>
{
    public const string Name = "Manual portfolio acceptance performance";
}

[Collection(PortfolioManualPaymentPerformanceCollection.Name)]
[Trait("Performance", "ManualPortfolioAcceptance")]
public sealed class PortfolioManualPaymentPerformanceTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Fact]
    [Trait("Isolation", "FreshProcess")]
    public async Task Configured_receipts_resolve_one_or_ten_forms_in_one_batch_for_up_to_five_hundred_lines()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate, CatalogPermissionCodes.Read, CatalogPermissionCodes.Update);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var revenue = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "4193", "Revenue", "General", "Credit");
        var reductionAccount = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5193", "Expense", "General", "Debit");
        var receivable = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "1393", "Asset", "Receivable", "Debit");
        var type = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/payment-method"))!
            .Single(item => item.Code == "Cash");
        var sides = (await client.GetFromJsonAsync<ReferenceOption[]>("/api/commerce/v1/reference-options/accounting-balance-side"))!;
        var creditSide = sides.Single(item => item.Code == "Credit");
        var concept = new SaveFinancialConceptRequest(Guid.NewGuid(), $"PERF-{Guid.NewGuid():N}",
            "Contrapartida de rendimiento", revenue.AccountId, creditSide.Id, true, false, true, true, 10);
        using (var saved = await client.PutAsJsonAsync($"/api/commerce/v1/financial-concepts/{concept.FinancialConceptId}", concept))
            saved.EnsureSuccessStatusCode();
        var reductionConcept = concept with { FinancialConceptId = Guid.NewGuid(), Code = $"RED-{Guid.NewGuid():N}",
            Name = "Reducción de rendimiento", AccountId = reductionAccount.AccountId,
            SideOptionId = sides.Single(item => item.Code == "Debit").Id };
        using (var saved = await client.PutAsJsonAsync($"/api/commerce/v1/financial-concepts/{reductionConcept.FinancialConceptId}", reductionConcept))
            saved.EnsureSuccessStatusCode();
        var forms = new List<PaymentMethodView>();
        for (var index = 0; index < 10; index++)
        {
            var configuration = new SavePaymentMethodRequest(Guid.NewGuid(), $"PERF-{Guid.NewGuid():N}",
                $"Caja de rendimiento {index}", type.Id, cash.AccountId, false, true, false, false, true, index);
            using var saved = await client.PutAsJsonAsync($"/api/commerce/v1/payment-methods/{configuration.PaymentMethodId}", configuration);
            saved.EnsureSuccessStatusCode();
            forms.Add((await saved.Content.ReadFromJsonAsync<PaymentMethodSaveResult>())!.Method);
        }
        using var scope = fixture.CreateScope();
        var publisher = new RecordingPublisher();
        var coordinator = new AccountingProcessingCoordinator(publisher,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(),
            scope.ServiceProvider.GetRequiredService<IAccountingProcessingSignalGate>());
        var service = new AccountingService(scope.ServiceProvider.GetRequiredService<IAccountingStore>(),
            scope.ServiceProvider.GetRequiredService<IBankReconciliationStore>(), coordinator,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>());
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, ReceivablesPermissionCodes.RegisterPayment });
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        var opened = await service.SaveVoucherDraftAsync(user, new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
            new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Obligación para pagos con conceptos", "PERF-CONCEPTO",
            [new(receivable.AccountId, fixture.SupplierPartyId, null, "Obligación", 100000, 0,
                "PERF-CONCEPTO", Guid.NewGuid(), DueDate: new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero)),
             new(revenue.AccountId, fixture.SupplierPartyId, null, "Contrapartida", 0, 100000, LineId: Guid.NewGuid())],
            null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
        opened = await service.SendVoucherDraftAsync(user, opened.DocumentId, new(opened.RowVersion));
        await processor.ProcessAsync(opened.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
        var obligationId = Assert.Single(opened.FinancialEffects!.Positions).PositionId;
        await using (var resources = new SqlConnection(fixture.ConnectionString))
        {
            await resources.OpenAsync();
            await using var check = new SqlCommand("""
                SELECT systemMemory.available_physical_memory_kb,systemMemory.system_low_memory_signal_state,
                  processMemory.process_physical_memory_low
                FROM sys.dm_os_sys_memory systemMemory CROSS JOIN sys.dm_os_process_memory processMemory;
                """, resources);
            await using var row = await check.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            output.WriteLine("Before measurement: available physical memory={0}KB; OS pressure={1}; SQL pressure={2}",
                row.GetInt64(0),row.GetBoolean(1),row.GetBoolean(2));
            Assert.False(row.GetBoolean(1) || row.GetBoolean(2),
                "SQL Server reports physical memory pressure. Release test resources before measuring latency.");
        }
        var postingCommands = new Dictionary<(bool Obligation, bool Reduction), int>();
        var budgetFailures = new List<string>();
        try
        {
            foreach (var (size, formCount, withConcept, withObligation, withReduction) in new[] {
                (2, 1, false, false, false), (500, 1, false, false, false), (500, 10, false, false, false), (500, 10, true, false, false),
                (3, 1, true, true, false), (500, 10, true, true, false), (3, 1, true, true, true), (500, 10, true, true, true) })
            {
                var acceptanceSamples = new List<double>();
                var postingSamples = new List<double>();
                var sqlTimings = new List<(string Stage, string Command, double Milliseconds)>();
                for (var sample = 0; sample < 20; sample++)
                {
                    var documentId = Guid.NewGuid();
                    var debtLineId = Guid.NewGuid();
                    var credits = size - formCount - (withObligation ? 1 : 0);
                    var lines = Enumerable.Range(0, credits).Select(_ => new VoucherDraftLine(withReduction ? reductionAccount.AccountId : revenue.AccountId,
                        fixture.SupplierPartyId, null, "Concepto del recibo", withReduction ? 1 : 0, withReduction ? 0 : formCount, LineId: Guid.NewGuid(),
                        FinancialConceptId: withConcept ? withReduction ? reductionConcept.FinancialConceptId : concept.FinancialConceptId : null,
                        FinancialConceptTargetLineId: withReduction ? debtLineId : null,
                        FinancialGroupId: withObligation ? documentId : null))
                        .Concat(forms.Take(formCount).Select(form => new VoucherDraftLine(cash.AccountId,
                            fixture.SupplierPartyId, null, "Dinero recibido", withReduction ? 1 : credits + (withObligation ? 1m / formCount : 0m), 0, LineId: Guid.NewGuid(),
                            FinancialGroupId: withObligation ? documentId : null,
                            PaymentEvidence: new(PaymentMethodId: form.PaymentMethodId, PaymentMethodTypeCode: form.MethodCode,
                                PaymentMethodVersion: form.Version, PaymentMethodCode: form.Code, PaymentMethodName: form.Name))))
                        .Concat(withObligation ? [new VoucherDraftLine(receivable.AccountId, fixture.SupplierPartyId, null,
                            "Abono a la obligación", 0, withReduction ? credits + formCount : 1m, LineId: debtLineId,
                            ExistingPosition: new("Receivable", obligationId), FinancialGroupId: documentId)] : Array.Empty<VoucherDraftLine>())
                        .ToArray();
                    var capture = new SaveVoucherDraftRequest(documentId, AccountingManualDocumentTypes.ManualVoucher,
                        new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Recibo con formas por lote",
                        "FORMAS-VOLUMEN", lines, null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "CashReceipt");
                    var draft = await service.SaveVoucherDraftAsync(user, capture);
                    using (var activity = StageActivity("configured-receipt-acceptance"))
                    using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                        afterExecute: (command, elapsed) => sqlTimings.Add(("acceptance", Label(command), elapsed))))
                    {
                        var timer = Stopwatch.StartNew();
                        var accepted = await service.SendVoucherDraftAsync(user, draft.DocumentId, new(draft.RowVersion));
                        acceptanceSamples.Add(timer.Elapsed.TotalMilliseconds);
                        Assert.Equal(withObligation ? 8 : 7, count.Count);
                        Assert.Equal("Pending", accepted.Status);
                        Assert.Equal(withObligation ? 0 : formCount, accepted.FinancialEffects!.DirectMovements.Count);
                        if (withObligation)
                        {
                            var payment = Assert.Single(accepted.FinancialEffects.Payments);
                            Assert.Equal(withReduction ? null : (decimal?)(credits * formCount), payment.AdditionalConceptAmount);
                            Assert.Equal(withReduction ? formCount : 1m, Assert.Single(payment.Allocations).Amount);
                            Assert.Equal(withReduction ? credits : 0, accepted.FinancialEffects.Positions.Count);
                            if (withReduction) Assert.All(accepted.FinancialEffects.Positions, item => {
                                Assert.Equal("Adjustment", item.EffectKind); Assert.Equal(debtLineId, item.TargetSourceLineId); Assert.Equal(1m, item.Amount);
                            });
                        }
                        if (withConcept) Assert.Equal(withReduction ? reductionConcept.FinancialConceptId : concept.FinancialConceptId, Assert.Single(accepted.FinancialEffects.Concepts!).FinancialConceptId);
                        else Assert.Null(accepted.FinancialEffects.Concepts);
                    }
                    using (var activity = StageActivity("configured-receipt-posting"))
                    using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                        afterExecute: (command, elapsed) => sqlTimings.Add(("posting", Label(command), elapsed))))
                    using (var resolution = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                        commandFilter: command => command.CommandText.Contains("DECLARE @Resolved TABLE(SourceLineId", StringComparison.Ordinal)))
                    {
                        var timer = Stopwatch.StartNew();
                        await processor.ProcessAsync(draft.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
                        postingSamples.Add(timer.Elapsed.TotalMilliseconds);
                        postingCommands.TryAdd((withObligation, withReduction), count.Count);
                        Assert.Equal(postingCommands[(withObligation, withReduction)], count.Count);
                        Assert.Equal(1, resolution.Count);
                    }
                    var posted = await service.GetVoucherDraftAsync(user, draft.DocumentId);
                    Assert.NotNull(posted);
                    Assert.Equal("Posted", posted.Status);
                    Assert.Equal(formCount, posted.AppliedPaymentMethods!.Count);
                    Assert.All(posted.AppliedPaymentMethods, applied => Assert.Equal(cash.AccountId, applied.AccountId));
                }
                CheckStage("acceptance", acceptanceSamples, withObligation ? 8 : 7);
                CheckStage("posting", postingSamples, postingCommands[(withObligation, withReduction)]);
                foreach (var timing in sqlTimings.GroupBy(item => (item.Stage, item.Command)))
                    output.WriteLine("lines={0}; forms={1}; {2}/{3}: count={4}; avg={5:F1}ms; max={6:F1}ms",
                        size, formCount, timing.Key.Stage, timing.Key.Command, timing.Count(),
                        timing.Average(item => item.Milliseconds), timing.Max(item => item.Milliseconds));

                void CheckStage(string stage, List<double> samples, int commands)
                {
                    var ordered = samples.Order().ToArray();
                    var p95 = ordered[(int)Math.Ceiling(samples.Count * .95) - 1];
                    output.WriteLine("Configured receipt {0}: lines={1}; forms={2}; first={3:F1}ms; p95={4:F1}ms; SQL={5}; samples=20; concepts={6}; obligation={7}; reduction={8}",
                        stage, size, formCount, samples[0], p95, commands, withConcept, withObligation, withReduction);
                    if (p95 >= 2000) budgetFailures.Add($"{stage}: {size} lines/{formCount} forms/concepts={withConcept}/reduction={withReduction}, p95 {p95:F1} ms");
                }
            }
        }
        catch (SqlException timeout) when (timeout.Number == -2)
        {
            // Acceptance has already rolled back, and all measured stages have
            // unwound. Capture the existing diagnostic only after the failure;
            // never profile concurrently with the timed operation.
            try { await WriteAcceptanceDiagnosticsAsync(); }
            catch (Exception diagnosticError)
            {
                output.WriteLine("Acceptance diagnostics unavailable: {0}", diagnosticError.Message);
            }
            throw;
        }
        Assert.Equal(161, publisher.Count);
        if (budgetFailures.Count > 0) await WriteAcceptanceDiagnosticsAsync();
        Assert.True(budgetFailures.Count == 0, "Configured receipts exceeded 2 s: " + string.Join("; ", budgetFailures));
    }

    private async Task WriteAcceptanceDiagnosticsAsync()
    {
        // Only after every measured stage has finished, on the isolated fixture.
        // Preserve cached plans if another performance regression needs analysis.
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        await using var command = new SqlCommand("""
            SELECT OBJECT_SCHEMA_NAME(stats.object_id)+N'.'+OBJECT_NAME(stats.object_id),
              stats.execution_count,stats.total_elapsed_time/1000.0/stats.execution_count,
              CONVERT(nvarchar(max),planData.query_plan)
            FROM sys.dm_exec_procedure_stats stats OUTER APPLY sys.dm_exec_query_plan(stats.plan_handle) planData
            WHERE stats.database_id=DB_ID() AND stats.object_id IN(OBJECT_ID(N'dbo.AccountingSourcesAccept'),
              OBJECT_ID(N'accounting.BankAccountsResolve'),OBJECT_ID(N'accounting.PortfolioPaymentAccept'));
            SELECT TOP(12) OBJECT_SCHEMA_NAME(text.objectid)+N'.'+OBJECT_NAME(text.objectid),
              stats.total_elapsed_time/1000.0/stats.execution_count,stats.total_logical_reads/stats.execution_count,
              LEFT(SUBSTRING(text.text,stats.statement_start_offset/2+1,
                (CASE stats.statement_end_offset WHEN -1 THEN DATALENGTH(text.text) ELSE stats.statement_end_offset END
                  -stats.statement_start_offset)/2+1),400)
            FROM sys.dm_exec_query_stats stats CROSS APPLY sys.dm_exec_sql_text(stats.sql_handle) text
            WHERE text.dbid=DB_ID() AND text.objectid IN(OBJECT_ID(N'dbo.AccountingSourcesAccept'),
              OBJECT_ID(N'accounting.BankAccountsResolve'),OBJECT_ID(N'accounting.PortfolioPaymentAccept'))
            ORDER BY stats.total_elapsed_time/stats.execution_count DESC;
            SELECT process_physical_memory_low,(SELECT COUNT_BIG(*) FROM sys.dm_exec_procedure_stats WHERE database_id=DB_ID())
            FROM sys.dm_os_process_memory;
            """, connection) { CommandTimeout = 10 };
        await using var reader = await command.ExecuteReaderAsync();
        var index = 0;
        while (await reader.ReadAsync())
        {
            output.WriteLine("Acceptance diagnostic: {0}; executions={1}; average={2:F1}ms", reader.GetString(0), reader.GetInt64(1), reader.GetDecimal(2));
            if (reader.IsDBNull(3)) continue;
            var path = Path.Combine(Path.GetTempPath(), $"auraly-receipt-{connection.Database}-{index++}.sqlplan");
            await File.WriteAllTextAsync(path, reader.GetString(3));
            output.WriteLine("Cached plan: {0}", path);
        }
        await reader.NextResultAsync();
        while (await reader.ReadAsync())
            output.WriteLine("Acceptance statement: {0}; average={1:F1}ms; logical reads={2}; SQL={3}",
                reader.GetString(0),reader.GetDecimal(1),reader.GetInt64(2),reader.GetString(3).Replace('\r',' ').Replace('\n',' '));
        await reader.NextResultAsync();
        Assert.True(await reader.ReadAsync());
        output.WriteLine("After measurement: SQL pressure={0}; cached procedures in fixture={1}", reader.GetBoolean(0),reader.GetInt64(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fifty_and_five_hundred_lines_use_eight_commands_with_ten_thousand_existing_positions(bool advance)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        using var scope = fixture.CreateScope();
        var original = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, PayablesPermissionCodes.RegisterPayment });
        var accounts = await original.ListAccountsAsync(user);
        var payableAccount = accounts.Single(account => account.Code == "220505").AccountId;
        var movementAccount = advance ? (await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "1394", "Asset", "SupplierAdvance", "Debit")).AccountId : payableAccount;
        var expenseAccount = accounts.Single(account => account.Code == "519595").AccountId;
        var runPrefix = "VOL-" + Guid.NewGuid().ToString("N")[..12] + "-";
        var captureDescription = "Aceptación por lote " + runPrefix;
        var publisher = new RecordingPublisher();
        var coordinator = new AccountingProcessingCoordinator(publisher,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(),
            scope.ServiceProvider.GetRequiredService<IAccountingProcessingSignalGate>());
        // Simulate the production asynchronous transport boundary. Acceptance,
        // gate and authoritative response stay real; posting is a separate stage.
        var service = new AccountingService(scope.ServiceProvider.GetRequiredService<IAccountingStore>(),
            scope.ServiceProvider.GetRequiredService<IBankReconciliationStore>(), coordinator,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>());
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var positions = await SeedExistingPositionsAsync(connection, payableAccount, expenseAccount, runPrefix);
        Assert.Equal(250, positions.Count);
        foreach (var lineCount in new[] { 50, 500 })
        {
            var samples = new List<double>();
            var timings = new List<(string Command, double Milliseconds)>();
            for (var sample = 0; sample < 20; sample++)
            {
                var lines = new List<VoucherDraftLine>();
                foreach (var positionId in positions.Take(lineCount / 2))
                {
                    var group = Guid.NewGuid();
                    lines.Add(new(movementAccount, fixture.SupplierPartyId, null, advance ? "Anticipo de rendimiento" : "Abono de rendimiento", 1m, 0m,
                        LineId: Guid.NewGuid(), ExistingPosition: advance ? null : new("Payable", positionId), FinancialGroupId: group));
                    lines.Add(new(cash.AccountId, null, null, "Tesorería de rendimiento", 0m, 1m,
                        LineId: Guid.NewGuid(), FinancialGroupId: group));
                }
                var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                    new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", captureDescription, null, lines, null, null,
                    MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Disbursement");
                var saved = await service.SaveVoucherDraftAsync(user, capture);
                using var count = new InventoryBalanceProcessingTests.CommandCounter(connection.Database,
                    afterExecute: (command, milliseconds) => timings.Add((Label(command), milliseconds)));
                var stopwatch = Stopwatch.StartNew();
                var accepted = await service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
                stopwatch.Stop();
                samples.Add(stopwatch.Elapsed.TotalMilliseconds);
                Assert.Equal(8, count.Count);
                Assert.Equal("Pending", accepted.Status);
                Assert.NotNull(accepted.SentAt);
                Assert.Equal(lineCount / 2, accepted.FinancialEffects!.Payments.Count);
                Assert.Equal(advance ? lineCount / 2 : 0, accepted.FinancialEffects.Positions.Count);
                Assert.All(accepted.FinancialEffects.Payments, group => Assert.Equal(advance ? 1m : 0m, group.AdvanceAmount));
                Assert.Equal((decimal)(lineCount / 2), accepted.Lines.Sum(line => line.Value.Debit));
                Assert.Null(accepted.Row.EntryId);
                Assert.Null(accepted.Row.DebitTotal);
            }
            var ordered = samples.Order().ToArray();
            var p95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
            output.WriteLine("{0} lines / {1} groups: first={2:F1}ms, p95={3:F1}ms; SQL=8; samples=20; seeded positions=10000; advance={4}",
                lineCount, lineCount / 2, samples[0], p95, advance);
            Assert.Equal(160, timings.Count);
            foreach (var timing in timings.GroupBy(item => item.Command))
                output.WriteLine("{0} lines: {1}; count={2}; avg={3:F1}ms; max={4:F1}ms", lineCount, timing.Key,
                    timing.Count(), timing.Average(item => item.Milliseconds), timing.Max(item => item.Milliseconds));
            Assert.True(p95 < (lineCount == 50 ? 2000 : 3000), $"Acceptance exceeded its budget: {lineCount} lines, p95 {p95:F1}ms.");
        }
        Assert.Equal(40, publisher.Count);
        await using var state = new SqlCommand("""
            SELECT (SELECT SUM(OutstandingAmount) FROM dbo.Payables WHERE BusinessId=@BusinessId
                AND SourceDocumentType=N'FinancialVolumeFixture' AND DocumentNumber LIKE @RunPrefix+N'%'),
              (SELECT COUNT(*) FROM dbo.SupplierPayments WHERE BusinessId=@BusinessId AND Status=N'Accepted' AND Notes=@Notes),
              (SELECT COUNT(*) FROM dbo.SupplierPaymentApplications application JOIN dbo.SupplierPayments payment
                ON payment.PaymentId=application.PaymentId WHERE payment.BusinessId=@BusinessId AND payment.Notes=@Notes AND application.AppliedAt IS NOT NULL),
              (SELECT COUNT(*) FROM dbo.AccountingEntries WHERE BusinessId=@BusinessId AND SourceDocumentType=N'ManualAccountingVoucher' AND Description=@Notes),
              (SELECT COUNT(*) FROM dbo.SupplierPaymentApplications application JOIN dbo.SupplierPayments payment
                ON payment.PaymentId=application.PaymentId WHERE payment.BusinessId=@BusinessId AND payment.Notes=@Notes),
              (SELECT COUNT(*) FROM dbo.SupplierCredits credit WHERE credit.BusinessId=@BusinessId AND EXISTS(
                SELECT 1 FROM dbo.SupplierPayments payment WHERE payment.ParentSourceDocumentId=credit.SourceDocumentId
                  AND payment.ParentSourceDocumentType=credit.SourceDocumentType AND payment.BusinessId=@BusinessId AND payment.Notes=@Notes));
            """, connection);
        state.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        state.Parameters.AddWithValue("@RunPrefix", runPrefix);
        state.Parameters.AddWithValue("@Notes", captureDescription);
        await using var final = await state.ExecuteReaderAsync();
        Assert.True(await final.ReadAsync());
        Assert.Equal(10_000_000m, final.GetDecimal(0));
        Assert.Equal(5_500, final.GetInt32(1));
        Assert.Equal(0, final.GetInt32(2));
        Assert.Equal(0, final.GetInt32(3));
        Assert.Equal(advance ? 0 : 5_500, final.GetInt32(4));
        Assert.Equal(0, final.GetInt32(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Obligation_openings_accept_and_post_fifty_and_five_hundred_lines_with_constant_round_trips(bool receivable)
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure);
        var counterpart = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            receivable ? "4198" : "5198", receivable ? "Revenue" : "Expense", "General", receivable ? "Credit" : "Debit");
        var control = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            receivable ? "1398" : "2298", receivable ? "Asset" : "Liability", receivable ? "Receivable" : "Payable", receivable ? "Debit" : "Credit");
        using var scope = fixture.CreateScope();
        var publisher = new RecordingPublisher();
        var coordinator = new AccountingProcessingCoordinator(publisher,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(),
            scope.ServiceProvider.GetRequiredService<IAccountingProcessingSignalGate>());
        var service = new AccountingService(scope.ServiceProvider.GetRequiredService<IAccountingStore>(),
            scope.ServiceProvider.GetRequiredService<IBankReconciliationStore>(), coordinator,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>());
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend });
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        int? processingCommands = null;
        foreach (var size in new[] { 50, 500 })
        {
            var acceptanceSamples = new List<double>();
            var postingSamples = new List<double>();
            for (var sample = 0; sample < 20; sample++)
            {
                var at = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
                var lines = Enumerable.Range(1, size - 1).Select(index => new VoucherDraftLine(control.AccountId,
                    fixture.SupplierPartyId, null, "Obligación por lote", receivable ? 1m : 0m, receivable ? 0m : 1m,
                    $"OBL-{sample}-{index}", Guid.NewGuid(), DueDate: at.AddDays(30)))
                    .Prepend(new(counterpart.AccountId, fixture.SupplierPartyId, null, "Contrapartida del lote",
                        receivable ? 0m : size - 1, receivable ? size - 1 : 0m, LineId: Guid.NewGuid())).ToArray();
                var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
                    "MANUAL_VOUCHER", "Reconocimiento por lote", "AP-VOLUMEN", lines, null, null,
                    MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
                var saved = await service.SaveVoucherDraftAsync(user, capture);
                using (var activity = StageActivity("obligation-opening-acceptance"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                {
                    var timer = Stopwatch.StartNew();
                    var accepted = await service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
                    timer.Stop();
                    acceptanceSamples.Add(timer.Elapsed.TotalMilliseconds);
                    Assert.Equal(7, count.Count);
                    Assert.Equal("Pending", accepted.Status);
                    Assert.Equal(size - 1, accepted.FinancialEffects!.Positions.Count);
                    Assert.Empty(accepted.FinancialEffects.Payments);
                }
                using (var activity = StageActivity("obligation-opening-posting"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                using (var openings = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    commandFilter: command => command.CommandText.Contains(receivable ? "INSERT dbo.Receivables" : "INSERT dbo.Payables", StringComparison.Ordinal)))
                using (var journal = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    commandFilter: command => command.CommandText.Contains("INSERT dbo.AccountingEntryLines", StringComparison.Ordinal)))
                {
                    var timer = Stopwatch.StartNew();
                    await processor.ProcessAsync(saved.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
                    timer.Stop();
                    postingSamples.Add(timer.Elapsed.TotalMilliseconds);
                    processingCommands ??= count.Count;
                    Assert.Equal(processingCommands.Value, count.Count);
                    Assert.Equal(1, openings.Count);
                    Assert.Equal(1, journal.Count);
                }
                var entry = await service.GetEntryAsync(user, saved.DocumentId);
                Assert.NotNull(entry);
                Assert.Equal(size, entry.Lines.Count);
                await using var connection = new SqlConnection(fixture.ConnectionString);
                await connection.OpenAsync();
                await using var verify = new SqlCommand($"""
                    SELECT COUNT(*) FROM dbo.{(receivable ? "Receivables" : "Payables")} p
                    JOIN dbo.AccountingEntryLines line ON line.EntryId=p.OpeningEntryId AND line.LineNumber=p.OpeningEntryLineNumber
                    WHERE p.SourceDocumentId=@DocumentId AND p.BusinessId=@BusinessId
                      AND p.SourceLineId=line.SourceLineId AND {(receivable ? "line.Debit" : "line.Credit")}=p.OriginalAmount AND line.AccountId=@AccountId;
                    """, connection);
                verify.Parameters.AddWithValue("@DocumentId", saved.DocumentId);
                verify.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
                verify.Parameters.AddWithValue("@AccountId", control.AccountId);
                Assert.Equal(size - 1, (int)(await verify.ExecuteScalarAsync())!);
            }
            CheckBudget("acceptance", acceptanceSamples, 7);
            CheckBudget("posting", postingSamples, processingCommands!.Value);

            void CheckBudget(string stage, List<double> samples, int commands)
            {
                var ordered = samples.Order().ToArray();
                var p95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
                output.WriteLine("{0} {1} openings: {2} lines / {3} positions; first={4:F1}ms, p95={5:F1}ms; SQL={6}; samples=20",
                    receivable ? "Receivable" : "Payable", stage, size, size - 1, samples[0], p95, commands);
                Assert.True(p95 < (size == 50 ? 2000 : 3000), $"{stage} exceeded its budget: {size} lines, p95 {p95:F1}ms.");
            }
        }
        Assert.Equal(40, publisher.Count);
    }

    [Fact]
    public async Task Direct_treasury_keeps_one_session_write_for_two_and_five_hundred_journal_lines()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Read, AccountingPermissionCodes.Configure,
            AccountingPermissionCodes.Activate);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        var cash = await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client);
        var expense = await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5193", "Expense", "General", "Debit");
        var session = await fixture.OpenWebWorkSessionAsync();
        using var scope = fixture.CreateScope();
        var publisher = new RecordingPublisher();
        var coordinator = new AccountingProcessingCoordinator(publisher,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(),
            scope.ServiceProvider.GetRequiredService<IAccountingProcessingSignalGate>());
        var service = new AccountingService(scope.ServiceProvider.GetRequiredService<IAccountingStore>(),
            scope.ServiceProvider.GetRequiredService<IBankReconciliationStore>(), coordinator,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>());
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var sessions = scope.ServiceProvider.GetRequiredService<WorkSessionService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate,
                AccountingPermissionCodes.ManualSend, WorkSessionPermissionCodes.ManageCash });
        var sessionUser = new WorkSessionIdentity(fixture.UserId, fixture.TenantId,
            new HashSet<string> { WorkSessionPermissionCodes.Read, WorkSessionPermissionCodes.Close,
                WorkSessionPermissionCodes.ReadCashDifferences }, fixture.BusinessId);
        var startingCash = (await sessions.PreviewClosureAsync(sessionUser, session)).ExpectedCash;
        var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
        decimal total = 0;
        int? postingCommands = null;
        int? previewCommands = null;
        foreach (var size in new[] { 2, 500 })
        {
            var acceptanceTimes = new List<double>();
            var postingTimes = new List<double>();
            Guid lastId = default;
            for (var sample = 0; sample <= 20; sample++)
            {
                var lines = Enumerable.Range(1, size - 1).Select(index => new VoucherDraftLine(cash.AccountId,
                    null, null, "Salida directa por partida", 0, 1m, LineId: Guid.NewGuid()))
                    .Prepend(new(expense.AccountId, null, null, "Contrapartida de las salidas", size - 1, 0,
                        LineId: Guid.NewGuid())).ToArray();
                var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                    new(2026, 8, 3, 12, 0, 0, TimeSpan.Zero), "MANUAL_VOUCHER", "Tesorería directa por lote",
                    "TESORERIA-VOLUMEN", lines, null, null, MainPartyId: fixture.SupplierPartyId,
                    PayloadVersion: 4, VoucherTypeCode: "Disbursement", WorkSessionId: session);
                var saved = await service.SaveVoucherDraftAsync(user, capture);
                lastId = saved.DocumentId;
                using (var activity = StageActivity("direct-acceptance"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                {
                    var timer = Stopwatch.StartNew();
                    var accepted = await service.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
                    timer.Stop();
                    Assert.Equal(7, count.Count);
                    Assert.Equal("Pending", accepted.Status);
                    Assert.Equal(size - 1, accepted.FinancialEffects!.DirectMovements.Count);
                    Assert.Empty(accepted.FinancialEffects.Payments);
                    Assert.Empty(accepted.FinancialEffects.Positions);
                    if (sample > 0) acceptanceTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                using (var activity = StageActivity("direct-posting"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                using (var writes = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    commandFilter: command => command.CommandText.Contains("INSERT dbo.WorkSessionMovements", StringComparison.Ordinal)))
                {
                    var timer = Stopwatch.StartNew();
                    await processor.ProcessAsync(saved.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
                    timer.Stop();
                    postingCommands ??= count.Count;
                    Assert.Equal(postingCommands.Value, count.Count);
                    Assert.Equal(1, writes.Count);
                    if (sample > 0) postingTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                var entry = await service.GetEntryAsync(user, saved.DocumentId);
                Assert.NotNull(entry);
                Assert.Equal(size, entry.Lines.Count);
                total += size - 1;
            }
            using (var activity = StageActivity("direct-replay"))
            using (var replayWrites = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                commandFilter: command => command.CommandText.Contains("INSERT dbo.WorkSessionMovements", StringComparison.Ordinal) ||
                    command.CommandText.Contains("INSERT dbo.AccountingEntryLines", StringComparison.Ordinal)))
            {
                await processor.ProcessAsync(lastId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
                Assert.Equal(0, replayWrites.Count);
            }
            var previewTimes = new List<double>();
            var previewSqlTimes = new List<(string Stage, double Milliseconds)>();
            for (var sample = 0; sample <= 20; sample++)
            {
                using var activity = StageActivity("direct-preview");
                var queries = new List<string>();
                using var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    beforeExecute: command => queries.Add(command.CommandText[..Math.Min(command.CommandText.Length, 100)]),
                    afterExecute: (command, milliseconds) =>
                    {
                        if (sample == 0) return;
                        var stage = command.CommandText.Contains("PaymentMovements", StringComparison.Ordinal)
                            ? "totals-and-details" : command.CommandText.Contains("DECLARE @ManualDirect", StringComparison.Ordinal)
                                ? "details" : "session";
                        previewSqlTimes.Add((stage, milliseconds));
                    });
                var timer = Stopwatch.StartNew();
                var preview = await ClosurePlanOnTimeoutAsync(() => sessions.PreviewClosureAsync(sessionUser, session), "preview");
                timer.Stop();
                previewCommands ??= count.Count;
                Assert.True(count.Count == 2, $"Preview SQL: {count.Count}; {string.Join(" | ", queries)}");
                Assert.Equal(startingCash - total, preview.ExpectedCash);
                Assert.Equal((int)total, preview.CashMovements!.Count);
                if (sample > 0) previewTimes.Add(timer.Elapsed.TotalMilliseconds);
            }
            foreach (var stage in previewSqlTimes.GroupBy(value => value.Stage))
            {
                var ordered = stage.Select(value => value.Milliseconds).Order().ToArray();
                output.WriteLine("Preview {0} partidas, SQL {1}: p95 {2:F1} ms, {3} muestras.",
                    size, stage.Key, ordered[(int)Math.Ceiling(ordered.Length * .95) - 1], ordered.Length);
            }
            Budget("aceptación", acceptanceTimes, 7);
            Budget("posting", postingTimes, postingCommands!.Value);
            Budget("preview", previewTimes, previewCommands!.Value);

            void Budget(string stage, List<double> samples, int commands)
            {
                var ordered = samples.Order().ToArray();
                var p95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
                output.WriteLine("Tesorería directa, {0} partidas, {1}: p95 {2:F1} ms, {3} SQL, 20 muestras, {4} movimientos en sesión.",
                    size, stage, p95, commands, total);
                Assert.True(p95 < 2000, $"{stage}, {size} partidas: p95 {p95:F1} ms supera 2 segundos.");
            }
        }
        Assert.Equal(42, publisher.Count);

        var closed = await sessions.CloseAsync(sessionUser, session, Guid.NewGuid().ToString("N"),
            new CloseWorkSessionRequest(0, "Medición de páginas del mismo cierre", PaymentCounts:
                [new("Cash", 0), new("Card", 0), new("Transfer", 0)]));
        foreach (var (pageNumber, pageSize) in new[] { (1, 1), (1, 50), (2, 50) })
        {
            var times = new List<double>();
            for (var sample = 0; sample <= 20; sample++)
            {
                using var activity = StageActivity("direct-verification-page");
                using var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId);
                var timer = Stopwatch.StartNew();
                var page = await ClosurePlanOnTimeoutAsync(() => sessions.ListClosurePaymentVerificationPageAsync(sessionUser,
                    closed.WorkSessionClosureId, pageSize == 1 ? null : "Cash",
                    pageSize == 1 ? null : "CashOut", pageNumber, pageSize), "verification");
                timer.Stop();
                Assert.Equal(2, count.Count);
                Assert.Equal((int)total, page.TotalItems);
                Assert.Equal(pageSize, page.Items.Count);
                Assert.All(page.Items, item => Assert.Equal("ManualAccountingVoucher", item.SourceDocumentType));
                if (sample > 0) times.Add(timer.Elapsed.TotalMilliseconds);
            }
            var ordered = times.Order().ToArray();
            var p95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
            output.WriteLine("Verificación de {0} movimientos, página {1}: p95 {2:F1} ms, 2 SQL, {3} filas, 20 muestras.", total, pageNumber, p95, pageSize);
            if (p95 >= 2000) await WriteClosureDiagnosticsAsync("verification-budget");
            Assert.True(p95 < 2000, $"Verificación página {pageNumber}/{pageSize}: p95 {p95:F1} ms supera 2 segundos.");
        }

        var correctionPage = await sessions.ListClosurePaymentVerificationPageAsync(sessionUser,
            closed.WorkSessionClosureId, "Cash", "CashOut", 1, 50);
        var reviewer = sessionUser with { Permissions = sessionUser.Permissions
            .Append(WorkSessionPermissionCodes.ReconcileClosures).Append(Auraly.Contracts.Authorization.CommercePermissionCodes.SalesCorrectPaymentMethods).ToHashSet() };
        foreach (var correctionCount in new[] { 1, 50 })
        {
            var corrections = correctionPage.Items.Take(correctionCount).Select(item =>
                new WorkSessionPaymentCorrection(item.VerificationKey, "Cash", item.Amount,
                    "Verificación de evidencia sin cambiar el asiento", "Cash", Reference: "VALIDATED-REFERENCE")).ToArray();
            var times = new List<double>();
            for (var sample = 0; sample <= 20; sample++)
            {
                using var activity = StageActivity("direct-correction-preview");
                using var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId);
                var timer = Stopwatch.StartNew();
                await sessions.ValidatePaymentCorrectionsAsync(reviewer, closed.WorkSessionClosureId, corrections);
                timer.Stop();
                Assert.Equal(2, count.Count);
                if (sample > 0) times.Add(timer.Elapsed.TotalMilliseconds);
            }
            var ordered = times.Order().ToArray();
            var p95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
            output.WriteLine("Preview de {0} correcciones sobre {1} movimientos: p95 {2:F1} ms, 2 SQL, 20 muestras.",
                correctionCount, total, p95);
            if (p95 >= 2000) await WriteClosureDiagnosticsAsync("correction-budget");
            Assert.True(p95 < 2000, $"Preview de {correctionCount} correcciones: p95 {p95:F1} ms supera dos segundos.");
        }

    }

    [Fact]
    public async Task Native_supplier_payment_accepts_and_posts_one_and_one_hundred_positions_with_constant_round_trips()
    {
        using var client = fixture.CreateAdminClient(AccountingPermissionCodes.Configure, AccountingPermissionCodes.Activate);
        using (var defaults = await client.PutAsync("/api/commerce/v1/accounting/defaults", null)) defaults.EnsureSuccessStatusCode();
        using (var activate = await client.PostAsJsonAsync("/api/commerce/v1/accounting/activate",
            new ActivateAccountingRequest(new(2026, 1, 1), "COP", "ZeroDeclared"))) activate.EnsureSuccessStatusCode();
        using var scope = fixture.CreateScope();
        var original = scope.ServiceProvider.GetRequiredService<AccountingService>();
        var user = new AccountingUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { AccountingPermissionCodes.Read, AccountingPermissionCodes.ManualCreate, AccountingPermissionCodes.ManualSend,
                PayablesPermissionCodes.RegisterPayment });
        var payable = (await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "2293", "Liability", "Payable", "Credit")).AccountId;
        var expense = (await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "5193", "Expense", "General", "Debit")).AccountId;
        var at = new DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero);
        var capture = new SaveVoucherDraftRequest(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at,
            "MANUAL_VOUCHER", "Obligaciones para medir pagos nativos", "NATIVO-VOLUMEN",
            Enumerable.Range(1, 499).Select(index => new VoucherDraftLine(payable, fixture.SupplierPartyId, null,
                "Obligación para pago nativo", 0, 1000m, $"NAT-{index}", Guid.NewGuid(), DueDate: at.AddDays(30)))
                .Prepend(new(expense, fixture.SupplierPartyId, null, "Contrapartida", 499000m, 0, LineId: Guid.NewGuid())).ToArray(),
            null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting");
        var saved = await original.SaveVoucherDraftAsync(user, capture);
        var opened = await original.SendVoucherDraftAsync(user, saved.DocumentId, new(saved.RowVersion));
        var positions = opened.FinancialEffects!.Positions.Select(position => position.PositionId).ToArray();
        Assert.Equal(499, positions.Length);
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using (var series = new SqlCommand("""
            IF NOT EXISTS(SELECT 1 FROM dbo.DocumentSeries WHERE BusinessId=@BusinessId AND DocumentType=N'PayablePayment' AND IsActive=1)
              INSERT dbo.DocumentSeries(DocumentSeriesId,BusinessId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,
                IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,N'PayablePayment',N'PGP',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """, connection))
        {
            series.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            await series.ExecuteNonQueryAsync();
        }
        var publisher = new RecordingPublisher(PayablesDocumentTypes.Payment);
        var coordinator = new AccountingProcessingCoordinator(publisher,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(),
            scope.ServiceProvider.GetRequiredService<IAccountingProcessingSignalGate>());
        var payments = new PayablesService(scope.ServiceProvider.GetRequiredService<IPayablesStore>(), coordinator);
        var paymentUser = new PayablesUserIdentity(fixture.UserId, fixture.TenantId, fixture.BusinessId,
            new HashSet<string> { PayablesPermissionCodes.RegisterPayment });
        var processor = scope.ServiceProvider.GetRequiredService<SqlAccountingPostingProcessor>();
        var database = connection.Database;
        Assert.Equal(250, (await SeedExistingPositionsAsync(connection, payable, expense,
            "NB-" + Guid.NewGuid().ToString("N")[..12] + "-")).Count);
        int? acceptanceCommands = null, postingCommands = null;
        int? unapplicationAcceptanceCommands = null, unapplicationPostingCommands = null;
        int? returnAcceptanceCommands = null, returnPostingCommands = null;
        var returnCash = (await PortfolioOriginalAccountScenario.CreateCashAuxiliaryAsync(fixture, client)).AccountId;
        int? creditRestoreAcceptanceCommands = null, creditRestorePostingCommands = null;
        var advanceAccount = (await PortfolioOriginalAccountScenario.CreateConfiguredAuxiliaryAsync(fixture, client,
            "1392", "Asset", "SupplierAdvance", "Debit")).AccountId;
        var unapplicationPublisher = new RecordingPublisher();
        var unapplicationCoordinator = new AccountingProcessingCoordinator(unapplicationPublisher,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>(),
            scope.ServiceProvider.GetRequiredService<IAccountingProcessingSignalGate>());
        var unapplications = new AccountingService(scope.ServiceProvider.GetRequiredService<IAccountingStore>(),
            scope.ServiceProvider.GetRequiredService<IBankReconciliationStore>(), unapplicationCoordinator,
            scope.ServiceProvider.GetRequiredService<IAuralyIdGenerator>());
        foreach (var size in new[] { 1, 100 })
        {
            var acceptTimes = new List<double>();
            var postTimes = new List<double>();
            var unapplySqlTimes = new List<(string Command, double Milliseconds)>();
            var lastPaymentId = Guid.Empty;
            for (var sample = 0; sample <= 20; sample++)
            {
                var id = Guid.NewGuid();
                var request = new ConfirmSupplierPaymentRequest(id, fixture.BusinessId, fixture.SupplierId, at,
                    "COP", "Medición del pago nativo", positions.Take(size).Select(position => new SupplierPaymentAllocationRequest(position, 1m)).ToArray(),
                    [new("Cash", size, size)]);
                using (var activity = StageActivity("native-acceptance"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                {
                    var timer = Stopwatch.StartNew();
                    var accepted = await payments.ConfirmPaymentAsync(paymentUser, id.ToString("N"), request);
                    timer.Stop();
                    Assert.Equal("Accepted", accepted.Status);
                    acceptanceCommands ??= count.Count;
                    Assert.Equal(acceptanceCommands.Value, count.Count);
                    if (sample > 0) acceptTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                using (var activity = StageActivity("native-posting"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                using (var writes = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    commandFilter: command => command.CommandText.Contains("INSERT dbo.AccountingEntryLines", StringComparison.Ordinal)))
                {
                    var timer = Stopwatch.StartNew();
                    await processor.ProcessAsync(id, PayablesDocumentTypes.Payment, fixture.BusinessId, default);
                    timer.Stop();
                    postingCommands ??= count.Count;
                    Assert.Equal(postingCommands.Value, count.Count);
                    Assert.Equal(1, writes.Count);
                    if (sample > 0) postTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                var entry = await original.GetEntryAsync(user, id);
                Assert.NotNull(entry);
                Assert.Equal(size + 1, entry.Lines.Count);
                Assert.Equal((decimal)size, entry.DebitTotal);
                lastPaymentId = id;
            }
            Budget("aceptación", acceptTimes, acceptanceCommands!.Value);
            Budget("posting", postTimes, postingCommands!.Value);

            using (var activity = StageActivity("payment-correction-capture"))
            using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
            {
                var timer = Stopwatch.StartNew();
                var fullCorrection = await unapplications.GetPaymentCorrectionCaptureAsync(user,
                    "Payable", lastPaymentId);
                timer.Stop();
                Assert.NotNull(fullCorrection);
                Assert.Equal(size + 1, fullCorrection.Lines.Count);
                Assert.Equal(size, fullCorrection.Lines.Count(line => line.Value.ExistingPosition is not null));
                Assert.Equal(1, count.Count);
                output.WriteLine("Corrección de pago, {0} obligaciones: {1:F1} ms, {2} consulta SQL.",
                    size, timer.Elapsed.TotalMilliseconds, count.Count);
                Assert.True(timer.ElapsedMilliseconds < 2000,
                    $"Preparar corrección de {size} obligaciones tardó {timer.ElapsedMilliseconds} ms.");
            }

            var paidPositions = new List<(Guid PositionId, Guid TransactionId)>();
            await using (var paid = new SqlCommand("""
                SELECT PayableId,PayableTransactionId FROM dbo.PayableTransactions
                WHERE SourceDocumentId=@Id AND TransactionType=N'Payment' ORDER BY PayableId;
                """, connection))
            {
                paid.Parameters.AddWithValue("@Id", lastPaymentId);
                await using var reader = await paid.ExecuteReaderAsync();
                while (await reader.ReadAsync()) paidPositions.Add((reader.GetGuid(0), reader.GetGuid(1)));
            }
            Assert.Equal(size, paidPositions.Count);
            var unapplyAcceptTimes = new List<double>();
            var unapplyPostTimes = new List<double>();
            Guid lastUnapplicationId = default;
            for (var sample = 0; sample <= 20; sample++)
            {
                var reverseLines = new List<VoucherDraftLine>(size * 2);
                foreach (var paid in paidPositions)
                {
                    var groupId = Guid.NewGuid();
                    reverseLines.Add(new(payable, fixture.SupplierPartyId, null, "Reabrir obligación", 0, .04m,
                        LineId: Guid.NewGuid(), ExistingPosition: new("Payable", paid.PositionId, paid.TransactionId), FinancialGroupId: groupId));
                    reverseLines.Add(new(advanceAccount, fixture.SupplierPartyId, null, "Conservar anticipo", .04m, 0,
                        LineId: Guid.NewGuid(), FinancialGroupId: groupId));
                }
                var reverse = await unapplications.SaveVoucherDraftAsync(user,
                    new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher, at.AddDays(1), "MANUAL_VOUCHER",
                        "Desaplicación medida por lote", "DESAP-VOLUMEN", reverseLines, null, null,
                        MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
                using (var activity = StageActivity("unapplication-acceptance"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                {
                    var timer = Stopwatch.StartNew();
                    var accepted = await unapplications.SendVoucherDraftAsync(user, reverse.DocumentId, new(reverse.RowVersion));
                    timer.Stop();
                    Assert.Equal(size, accepted.FinancialEffects!.Unapplications!.Count);
                    unapplicationAcceptanceCommands ??= count.Count;
                    Assert.Equal(unapplicationAcceptanceCommands.Value, count.Count);
                    if (sample > 0) unapplyAcceptTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                using (var activity = StageActivity("unapplication-posting"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    afterExecute: (command, elapsed) => unapplySqlTimes.Add((Label(command), elapsed))))
                using (var writes = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    commandFilter: command => command.CommandText.Contains("DECLARE @Unapplications TABLE", StringComparison.Ordinal)))
                {
                    var timer = Stopwatch.StartNew();
                    await processor.ProcessAsync(reverse.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
                    timer.Stop();
                    unapplicationPostingCommands ??= count.Count;
                    Assert.Equal(unapplicationPostingCommands.Value, count.Count);
                    Assert.Equal(1, writes.Count);
                    if (sample > 0) unapplyPostTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                lastUnapplicationId = reverse.DocumentId;
            }
            Budget("desaplicación/aceptación", unapplyAcceptTimes, unapplicationAcceptanceCommands!.Value);
            Budget("desaplicación/posting", unapplyPostTimes, unapplicationPostingCommands!.Value);

            var lastUnapplication = (await unapplications.GetVoucherDraftAsync(user, lastUnapplicationId))!;
            var crossLines = new List<VoucherDraftLine>(size * 2);
            foreach (var unapplied in lastUnapplication.FinancialEffects!.Unapplications!)
            {
                var group = Guid.NewGuid();
                crossLines.Add(new(payable, fixture.SupplierPartyId, null, "Aplicar crédito para medir su restitución", .04m, 0,
                    LineId: Guid.NewGuid(), ExistingPosition: new("Payable", unapplied.PositionId), FinancialGroupId: group));
                crossLines.Add(new(advanceAccount, fixture.SupplierPartyId, null, "Crédito original", 0, .04m,
                    LineId: Guid.NewGuid(), ExistingPosition: new("SupplierCredit", unapplied.AdvancePositionId!.Value), FinancialGroupId: group));
            }
            var crossDraft = await unapplications.SaveVoucherDraftAsync(user, new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                at.AddDays(2), "MANUAL_VOUCHER", "Cruce para medición", "CRUCE-VOLUMEN", crossLines, null, null,
                MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
            var crossed = await unapplications.SendVoucherDraftAsync(user, crossDraft.DocumentId, new(crossDraft.RowVersion));
            await processor.ProcessAsync(crossDraft.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
            var restoreAcceptTimes = new List<double>();
            var restorePostTimes = new List<double>();
            var restoreCaptureTimes = new List<double>();
            for (var sample = 0; sample <= 20; sample++)
            {
                var originalApplication = crossed.FinancialEffects!.CreditApplications[0];
                using (var activity = StageActivity("credit-restoration-capture"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                {
                    var timer = Stopwatch.StartNew();
                    var prepared = await unapplications.GetUnapplicationCaptureAsync(user, "Payable", originalApplication.PositionId, originalApplication.EffectId);
                    timer.Stop();
                    Assert.NotNull(prepared);
                    Assert.Equal(originalApplication.CreditId, prepared.Lines[1].Value.ExistingPosition!.SubledgerId);
                    Assert.Equal(1, count.Count);
                    if (sample > 0) restoreCaptureTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                var lines = new List<VoucherDraftLine>(size * 2);
                foreach (var application in crossed.FinancialEffects!.CreditApplications)
                {
                    var group = Guid.NewGuid();
                    lines.Add(new(payable, fixture.SupplierPartyId, null, "Reabrir obligación del cruce", 0, .001m,
                        LineId: Guid.NewGuid(), ExistingPosition: new("Payable", application.PositionId, application.EffectId), FinancialGroupId: group));
                    lines.Add(new(advanceAccount, fixture.SupplierPartyId, null, "Restituir el mismo crédito", .001m, 0,
                        LineId: Guid.NewGuid(), ExistingPosition: new("SupplierCredit", application.CreditId), FinancialGroupId: group));
                }
                var draft = await unapplications.SaveVoucherDraftAsync(user, new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                    at.AddDays(3), "MANUAL_VOUCHER", "Restitución de crédito medida por lote", "RESTITUIR-VOLUMEN", lines, null, null,
                    MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
                using (var activity = StageActivity("credit-restoration-acceptance"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                {
                    var timer = Stopwatch.StartNew();
                    var accepted = await unapplications.SendVoucherDraftAsync(user, draft.DocumentId, new(draft.RowVersion));
                    timer.Stop();
                    Assert.Empty(accepted.FinancialEffects!.Positions);
                    Assert.Empty(accepted.FinancialEffects.Payments);
                    Assert.Equal(size, accepted.FinancialEffects.Unapplications!.Count);
                    creditRestoreAcceptanceCommands ??= count.Count;
                    Assert.Equal(creditRestoreAcceptanceCommands.Value, count.Count);
                    if (sample > 0) restoreAcceptTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                using (var activity = StageActivity("credit-restoration-posting"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                using (var writes = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    commandFilter: command => command.CommandText.Contains("DECLARE @Unapplications TABLE", StringComparison.Ordinal)))
                {
                    var timer = Stopwatch.StartNew();
                    await processor.ProcessAsync(draft.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
                    timer.Stop();
                    creditRestorePostingCommands ??= count.Count;
                    Assert.Equal(creditRestorePostingCommands.Value, count.Count);
                    Assert.Equal(1, writes.Count);
                    if (sample > 0) restorePostTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                var restoredEntry = await original.GetEntryAsync(user, draft.DocumentId);
                Assert.NotNull(restoredEntry);
                Assert.Equal(size * .001m, restoredEntry.DebitTotal);
            }
            Budget("restitución-crédito/captura", restoreCaptureTimes, 1);
            Budget("restitución-crédito/aceptación", restoreAcceptTimes, creditRestoreAcceptanceCommands!.Value);
            Budget("restitución-crédito/posting", restorePostTimes, creditRestorePostingCommands!.Value);
            var returnAcceptTimes = new List<double>();
            var returnPostTimes = new List<double>();
            for (var sample = 0; sample <= 20; sample++)
            {
                var returnLines = paidPositions.SelectMany(paid => {
                    var group = Guid.NewGuid();
                    return new VoucherDraftLine[] {
                        new(payable, fixture.SupplierPartyId, null, "Reabrir por devolución real", 0, .001m,
                            LineId: Guid.NewGuid(), ExistingPosition: new("Payable", paid.PositionId, paid.TransactionId), FinancialGroupId: group),
                        new(returnCash, fixture.SupplierPartyId, null, "Dinero realmente devuelto", .001m, 0,
                            LineId: Guid.NewGuid(), FinancialGroupId: group) };
                }).ToArray();
                var draft = await unapplications.SaveVoucherDraftAsync(user, new(Guid.NewGuid(), AccountingManualDocumentTypes.ManualVoucher,
                    at.AddDays(1), "MANUAL_VOUCHER", "Devolución real medida por lote", "DEVOLUCION-VOLUMEN", returnLines,
                    null, null, MainPartyId: fixture.SupplierPartyId, PayloadVersion: 4, VoucherTypeCode: "Accounting"));
                using (var activity = StageActivity("payment-return-acceptance"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                {
                    var timer = Stopwatch.StartNew();
                    var accepted = await unapplications.SendVoucherDraftAsync(user, draft.DocumentId, new(draft.RowVersion));
                    timer.Stop();
                    Assert.Equal(size, accepted.FinancialEffects!.DirectMovements.Count);
                    Assert.Empty(accepted.FinancialEffects.Positions);
                    returnAcceptanceCommands ??= count.Count;
                    Assert.Equal(returnAcceptanceCommands.Value, count.Count);
                    if (sample > 0) returnAcceptTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
                using (var activity = StageActivity("payment-return-posting"))
                using (var count = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId))
                using (var writes = new InventoryBalanceProcessingTests.CommandCounter(database, activity.TraceId,
                    commandFilter: command => command.CommandText.Contains("DECLARE @Unapplications TABLE", StringComparison.Ordinal)))
                {
                    var timer = Stopwatch.StartNew();
                    await processor.ProcessAsync(draft.DocumentId, AccountingManualDocumentTypes.ManualVoucher, fixture.BusinessId, default);
                    timer.Stop();
                    returnPostingCommands ??= count.Count;
                    Assert.Equal(returnPostingCommands.Value, count.Count);
                    Assert.Equal(1, writes.Count);
                    if (sample > 0) returnPostTimes.Add(timer.Elapsed.TotalMilliseconds);
                }
            }
            Budget("devolución-real/aceptación", returnAcceptTimes, returnAcceptanceCommands!.Value);
            Budget("devolución-real/posting", returnPostTimes, returnPostingCommands!.Value);
            void Budget(string stage, List<double> samples, int commands)
            {
                var ordered = samples.Order().ToArray();
                var p95 = ordered[(int)Math.Ceiling(ordered.Length * .95) - 1];
                output.WriteLine("Pago nativo, {0} obligaciones, {1}: p95 {2:F1} ms, {3} SQL, 20 muestras.", size, stage, p95, commands);
                if (p95 >= 2000 && stage == "desaplicación/posting")
                    foreach (var command in unapplySqlTimes.GroupBy(item => item.Command))
                        output.WriteLine("Desaplicación/{0}: count={1}; avg={2:F1} ms; max={3:F1} ms",
                            command.Key, command.Count(), command.Average(item => item.Milliseconds), command.Max(item => item.Milliseconds));
                Assert.True(p95 < 2000, $"{stage}, {size} obligaciones: p95 {p95:F1} ms supera 2 segundos.");
            }
        }
        Assert.Equal(42, publisher.Count);
        Assert.Equal(128, unapplicationPublisher.Count); // 42 unapplications, 2 crossings, 42 restorations and 42 actual money returns.
    }

    private async Task<List<Guid>> SeedExistingPositionsAsync(SqlConnection connection,
        Guid payableAccount, Guid expenseAccount, string runPrefix)
    {
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var positions = new List<Guid>();
        await using (var seed = new SqlCommand("""
            SET XACT_ABORT ON; BEGIN TRANSACTION;
            DECLARE @At datetimeoffset(7)='2026-08-02T12:00:00+00:00';
            DECLARE @Hash binary(32)=HASHBYTES('SHA2_256',CONVERT(varbinary(max),'{}'));
            DECLARE @Site uniqueidentifier=(SELECT PartySiteId FROM dbo.PartySites
              WHERE PartyId=@PartyId AND IsPrimary=1 AND IsActive=1);
            DECLARE @Period uniqueidentifier=(SELECT TOP(1) PeriodId FROM dbo.AccountingPeriods
              WHERE TenantId=@TenantId AND Status=N'Open' AND CONVERT(date,@At) BETWEEN StartsOn AND EndsOn
              ORDER BY StartsOn DESC,PeriodId);
            DECLARE @Seed TABLE(Number int PRIMARY KEY,SourceId uniqueidentifier,JobId uniqueidentifier,
              EntryId uniqueidentifier,PositionId uniqueidentifier);
            INSERT @Seed SELECT TOP(10000) ROW_NUMBER() OVER(ORDER BY a.object_id,b.object_id),NEWID(),NEWID(),NEWID(),NEWID()
              FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT dbo.AccountingSourceDocuments(SourceDocumentId,SourceDocumentType,TenantId,BusinessId,PayloadJson,
              PayloadHash,OccurredAt,AcceptedAt,AccountingEntryRequired)
            SELECT SourceId,N'FinancialVolumeFixture',@TenantId,@BusinessId,N'{}',@Hash,@At,@At,1 FROM @Seed;
            INSERT dbo.AccountingPostingJobs(AccountingPostingJobId,TenantId,BusinessId,SourceDocumentId,SourceDocumentType,
              SourcePayloadHash,OccurredAt,AccountingEntryRequired,Status,AttemptCount,CreatedAt,CompletedAt)
            SELECT JobId,@TenantId,@BusinessId,SourceId,N'FinancialVolumeFixture',@Hash,@At,1,N'Posted',1,@At,@At FROM @Seed;
            INSERT dbo.AccountingEntries(EntryId,TenantId,BusinessId,PeriodId,SourceDocumentId,SourceDocumentType,
              EntryNumber,OccurredAt,PostedAt,Description,DebitTotal,CreditTotal,SourcePayloadHash,RuleVersion)
            SELECT EntryId,@TenantId,@BusinessId,@Period,SourceId,N'FinancialVolumeFixture',CONCAT(@RunPrefix,Number),
              @At,@At,N'Apertura sintética de rendimiento',1000,1000,@Hash,1 FROM @Seed;
            INSERT dbo.AccountingEntryLines(EntryId,LineNumber,AccountId,PartyId,Description,Debit,Credit)
            SELECT EntryId,1,@PayableAccount,@PartyId,N'Obligación sintética',0,1000 FROM @Seed
            UNION ALL SELECT EntryId,2,@ExpenseAccount,@PartyId,N'Contrapartida sintética',1000,0 FROM @Seed;
            INSERT dbo.Payables(PayableId,BusinessId,SupplierId,PartySiteId,SourceDocumentId,SourceDocumentType,
              DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt,OpeningEntryId,OpeningEntryLineNumber)
            SELECT PositionId,@BusinessId,@SupplierId,@Site,SourceId,N'FinancialVolumeFixture',CONCAT(@RunPrefix,Number),
              N'COP',1000,1000,DATEADD(day,30,@At),N'Open',@At,EntryId,1 FROM @Seed;
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT NEWID(),PositionId,N'Opening',1000,SourceId,@At,@At FROM @Seed;
            COMMIT;
            SELECT PositionId FROM @Seed WHERE Number<=250 ORDER BY Number;
            """, connection) { CommandTimeout = 180 })
        {
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@SupplierId", fixture.SupplierId);
            seed.Parameters.AddWithValue("@PartyId", fixture.SupplierPartyId);
            seed.Parameters.AddWithValue("@PayableAccount", payableAccount);
            seed.Parameters.AddWithValue("@ExpenseAccount", expenseAccount);
            seed.Parameters.AddWithValue("@RunPrefix", runPrefix);
            await using var reader = await seed.ExecuteReaderAsync();
            while (await reader.ReadAsync()) positions.Add(reader.GetGuid(0));
        }
        return positions;
    }

    private static Activity StageActivity(string name) => new Activity(name).SetIdFormat(ActivityIdFormat.W3C)
        .SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded).Start();

    private async Task<T> ClosurePlanOnTimeoutAsync<T>(Func<Task<T>> action, string stage)
    {
        try { return await action(); }
        catch (SqlException error) when (error.Number == -2)
        {
            await WriteClosureDiagnosticsAsync(stage);
            throw;
        }
    }

    private async Task WriteClosureDiagnosticsAsync(string stage)
    {
        // Diagnose only after a failed measurement. Successful gates have no
        // observer, extra SQL, retries or altered timeout/budget.
        try
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            Assert.StartsWith("AuralyServerSlice_", connection.Database);
            await using var plans = new SqlCommand("""
                DECLARE @Candidates TABLE(Handle varbinary(64) PRIMARY KEY,Label nvarchar(32));
                INSERT @Candidates SELECT TOP(4) cached.plan_handle,
                  CASE WHEN text.objectid=OBJECT_ID(N'worksessions.ManualTreasuryMovements') THEN N'TreasuryFunction' ELSE N'ClosureBatch' END
                FROM sys.dm_exec_cached_plans cached CROSS APPLY sys.dm_exec_sql_text(cached.plan_handle) text
                WHERE EXISTS(SELECT 1 FROM sys.dm_exec_plan_attributes(cached.plan_handle) attribute
                    WHERE attribute.attribute=N'dbid' AND CONVERT(int,attribute.value)=DB_ID())
                  AND cached.cacheobjtype=N'Compiled Plan'
                  AND text.text NOT LIKE N'%sys.dm_exec_cached_plans%'
                  AND (text.text LIKE N'%DECLARE @ManualDirect TABLE%' OR text.text LIKE N'%#ClosureContext%'
                    OR text.objectid=OBJECT_ID(N'worksessions.ManualTreasuryMovements'))
                ORDER BY CASE WHEN text.text LIKE N'%CREATE TABLE #Corrections%' THEN 0 ELSE 1 END,
                  cached.usecounts DESC;
                SELECT candidate.Label,CONVERT(nvarchar(max),planData.query_plan)
                FROM @Candidates candidate OUTER APPLY sys.dm_exec_query_plan(candidate.Handle) planData;
                """, connection) { CommandTimeout = 10 };
            await using var reader = await plans.ExecuteReaderAsync();
            var index = 0;
            while (await reader.ReadAsync())
            {
                if (reader.IsDBNull(1)) continue;
                var path = Path.Combine(Path.GetTempPath(), $"auraly-closure-{stage}-failure-{index++}.sqlplan");
                await File.WriteAllTextAsync(path, reader.GetString(1));
                output.WriteLine("Failure diagnostic ({0}): {1}", reader.GetString(0), path);
            }
        }
        catch (Exception diagnosticError)
        {
            output.WriteLine("Failure diagnostic unavailable: {0}", diagnosticError.Message);
        }
    }

    private static string Label(SqlCommand command)
    {
        if (command.CommandType == System.Data.CommandType.StoredProcedure) return command.CommandText;
        if (command.CommandText.Contains("DECLARE @RequestedLines TABLE", StringComparison.Ordinal)) return "ValidateDimensions";
        if (command.CommandText.Contains("DECLARE @LockedPosition", StringComparison.Ordinal)) return "LockDraft";
        if (command.CommandText.Contains("SELECT d.DocumentType,d.OccurredAt", StringComparison.Ordinal)) return "ReadDraft";
        if (command.CommandText.Contains("UPDATE accounting.VoucherDrafts SET SentAt", StringComparison.Ordinal)) return "FreezeDraft";
        return command.CommandText.Trim().Split('\n')[0].Trim();
    }

    private sealed class RecordingPublisher(string expectedType = AccountingManualDocumentTypes.ManualVoucher) : IAccountingProcessingSignalPublisher
    {
        public int Count { get; private set; }
        public Task PublishAsync(AccountingProcessingSignal signal, CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedType, signal.DocumentType);
            Count++;
            return Task.CompletedTask;
        }
    }
}
