using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Auraly.Application.DocumentProcessing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Sdk;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed class InventoryBalanceProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Sales_update_the_authoritative_balance_in_sequence_and_a_duplicate_has_no_effect()
    {
        var quantityBefore = await ReadBalanceQuantityAsync() ?? 0m;
        var averageCostBefore = await ReadBalanceAverageUnitCostAsync() ?? 0m;
        if (averageCostBefore == 0m && quantityBefore <= 0m)
            averageCostBefore = await ReadProductCostBasisAsync();
        var first = fixture.CreateValidRequest(8_891);
        var second = fixture.CreateValidRequest(8_892);
        using var client = fixture.CreateClient();

        await UploadAsync(client, first);
        await UploadAsync(client, second);

        var movements = await ReadMovementsAsync(first.DocumentId, second.DocumentId);
        Assert.Collection(
            movements,
            movement =>
            {
                Assert.Equal(quantityBefore, movement.QuantityBefore);
                Assert.Equal(quantityBefore - first.Lines[0].Quantity, movement.QuantityAfter);
                Assert.Equal(averageCostBefore, movement.RecognizedUnitCost);
                Assert.InRange(decimal.Abs(movement.ValueChange + first.Lines[0].Quantity * averageCostBefore), 0m, 0.0001m);
            },
            movement =>
            {
                Assert.Equal(quantityBefore - first.Lines[0].Quantity, movement.QuantityBefore);
                Assert.Equal(
                    quantityBefore - first.Lines[0].Quantity - second.Lines[0].Quantity,
                    movement.QuantityAfter);
                Assert.True(movement.ProcessingSequence > movements[0].ProcessingSequence);
                Assert.Equal(averageCostBefore, movement.RecognizedUnitCost);
                Assert.InRange(decimal.Abs(movement.ValueChange + second.Lines[0].Quantity * averageCostBefore), 0m, 0.0001m);
            });

        var quantityAfter = await ReadBalanceQuantityAsync();
        Assert.Equal(
            quantityBefore - first.Lines[0].Quantity - second.Lines[0].Quantity,
            quantityAfter);
        await UploadAsync(client, first);
        Assert.Equal(quantityAfter, await ReadBalanceQuantityAsync());
        Assert.Equal(2, (await ReadMovementsAsync(first.DocumentId, second.DocumentId)).Count);
        await AssertValuationEvidenceAsync(first.DocumentId, second.DocumentId);
        await RemoveFromPendingFiscalListingAsync(first.DocumentId, second.DocumentId);
    }

    [Fact]
    public async Task A_product_without_inventory_is_sold_without_creating_kardex_or_changing_balance()
    {
        var quantityBefore = await ReadBalanceQuantityAsync();
        var sale = fixture.CreateValidRequest(8_893);
        await SetManageStockAsync(false);
        try
        {
            using var client = fixture.CreateClient();
            await UploadAsync(client, sale);
            Assert.Equal(quantityBefore, await ReadBalanceQuantityAsync());
            Assert.Equal(0, await fixture.CountAsync("InventoryMovements", sale.DocumentId));
            Assert.Equal(1, await fixture.CountAsync("SalesDocumentLines", sale.DocumentId));
        }
        finally
        {
            await SetManageStockAsync(true);
            await RemoveFromPendingFiscalListingAsync(sale.DocumentId, sale.DocumentId);
        }
    }

    [Fact]
    public async Task Twenty_sale_lines_reuse_one_inventory_batch_and_do_not_multiply_SQL_trips()
    {
        using var client = fixture.CreateClient();
        var warmup = fixture.CreateValidRequest(8894);
        var single = fixture.CreateValidRequest(8895);
        var source = fixture.CreateValidRequest(8896);
        try
        {
            await UploadAsync(client, warmup);
            int singleCommands;
            using (var counter = new CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
            {
                await UploadAsync(client, single);
                singleCommands = counter.Count;
            }
            var before = await ReadBalanceQuantityAsync() ?? 0;
            var original = source.Lines[0];
            var many = source with { Lines = Enumerable.Range(1, 20).Select(number => original with {
                LineNumber = number, Quantity = original.Quantity / 20,
                DiscountAmount = original.DiscountAmount / 20, TaxAmount = original.TaxAmount / 20,
                UntaxedAmount = original.UntaxedAmount / 20, LineTotal = original.LineTotal / 20 }).ToArray() };
            int manyCommands;
            using (var counter = new CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog))
            {
                await UploadAsync(client, many);
                manyCommands = counter.Count;
            }
            Assert.True(singleCommands > 0, "SqlClient no publicó evidencia de comandos.");
            Assert.True(manyCommands <= singleCommands,
                $"Una línea: {singleCommands} comandos; veinte líneas: {manyCommands} comandos.");
            var movements = await ReadMovementsAsync(many.DocumentId, many.DocumentId);
            Assert.Equal(20, movements.Count);
            Assert.Equal(before, movements[0].QuantityBefore);
            Assert.Equal(before - original.Quantity, movements[^1].QuantityAfter);
            Assert.Equal(before - original.Quantity, await ReadBalanceQuantityAsync());
            await UploadAsync(client, many);
            Assert.Equal(20, (await ReadMovementsAsync(many.DocumentId, many.DocumentId)).Count);
            await AssertValuationEvidenceAsync(many.DocumentId, many.DocumentId);
        }
        finally
        {
            await RemoveFromPendingFiscalListingAsync(warmup.DocumentId, single.DocumentId);
            await RemoveFromPendingFiscalListingAsync(source.DocumentId, source.DocumentId);
        }
    }

    [Fact]
    public async Task Valuation_evidence_is_complete_and_reconciled_while_legacy_rows_remain_compatible()
    {
        var sale = fixture.CreateValidRequest(8_897);
        using var client = fixture.CreateClient();
        await UploadAsync(client, sale);
        try
        {
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            Assert.StartsWith("AuralyServerSlice_", connection.Database);
            foreach (var assignment in new[] { "CostPoolScope=NULL", "PoolQuantityAfter=PoolQuantityAfter+1", "PoolInventoryValueAfter=PoolInventoryValueAfter+1", "ValuationMode=N'Unknown'" })
            {
                await using var invalid = new SqlCommand(
                    $"UPDATE dbo.InventoryMovements SET {assignment} WHERE DocumentId=@Id;", connection);
                invalid.Parameters.AddWithValue("@Id", sale.DocumentId);
                var error = await Assert.ThrowsAsync<SqlException>(async () => await invalid.ExecuteNonQueryAsync());
                Assert.Equal(547, error.Number);
            }

            // The additive schema must still accept the wholly absent evidence
            // produced by an older writer. Roll back the simulated legacy row.
            await using (var transaction = (SqlTransaction)await connection.BeginTransactionAsync())
            {
                await using var legacy = new SqlCommand("""
                    UPDATE dbo.InventoryMovements SET ValuationMode=NULL,CostPoolScope=NULL,
                      PoolQuantityBefore=NULL,PoolQuantityAfter=NULL,
                      PoolInventoryValueBefore=NULL,PoolInventoryValueAfter=NULL,CostPoolEpochId=NULL
                    WHERE DocumentId=@Id;
                    """, connection, transaction);
                legacy.Parameters.AddWithValue("@Id", sale.DocumentId);
                Assert.Equal(1, await legacy.ExecuteNonQueryAsync());
                await transaction.RollbackAsync();
            }
            await AssertValuationEvidenceAsync(sale.DocumentId, sale.DocumentId);
        }
        finally
        {
            await RemoveFromPendingFiscalListingAsync(sale.DocumentId, sale.DocumentId);
        }
    }

    [Fact]
    public async Task A_legacy_writer_gap_starts_a_new_cost_context_without_repairing_the_completed_replay()
    {
        var errors = new ErrorCaptureLoggerProvider();
        fixture.Services.GetRequiredService<ILoggerFactory>().AddProvider(errors);
        var first = fixture.CreateValidRequest(8_899);
        var legacy = fixture.CreateValidRequest(8_900);
        var next = fixture.CreateValidRequest(8_901);
        using var client = fixture.CreateClient();
        try
        {
            await UploadAsync(client, first);
            var firstEpoch = await ReadCostPoolEpochAsync(first.DocumentId);
            Assert.NotNull(firstEpoch);
            using (var oldWriter = new CommandCounter(new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog,
                commandFilter: command => command.CommandText.Contains("INSERT dbo.InventoryMovements(", StringComparison.Ordinal)
                    && command.Parameters["@DocumentId"].Value is Guid id && id == legacy.DocumentId,
                beforeExecute: command =>
                {
                    Assert.Equal(0, command.Parameters["@EpochCount"].Value);
                    command.Parameters["@HeadsJson"].Value = "[]";
                    var movements = JsonNode.Parse((string)command.Parameters["@Movements"].Value)!.AsArray();
                    foreach (var movement in movements) movement!["CostPoolEpochId"] = null;
                    command.Parameters["@Movements"].Value = movements.ToJsonString();
                }))
            {
                await UploadAsync(client, legacy);
                Assert.Equal(1, oldWriter.Count);
            }
            Assert.Null(await ReadCostPoolEpochAsync(legacy.DocumentId));
            var snapshot = await ReadBalanceSnapshotAsync();
            await UploadAsync(client, legacy);
            Assert.Equal(snapshot, await ReadBalanceSnapshotAsync());
            Assert.Null(await ReadCostPoolEpochAsync(legacy.DocumentId));
            try
            {
                await UploadAsync(client, next);
            }
            catch (XunitException failure)
            {
                throw new XunitException($"{failure.Message}\nRecent server errors:\n{errors.Recent}");
            }
            var nextEpoch = await ReadCostPoolEpochAsync(next.DocumentId);
            Assert.NotNull(nextEpoch);
            Assert.NotEqual(firstEpoch, nextEpoch);
            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var baseline = new SqlCommand("""
                SELECT epoch.InitialQuantity,movement.PoolQuantityBefore
                FROM dbo.InventoryMovements movement JOIN inventory.CostPoolEpochs epoch ON epoch.EpochId=movement.CostPoolEpochId
                WHERE movement.DocumentId=@Id;
                """, connection);
            baseline.Parameters.AddWithValue("@Id", next.DocumentId);
            await using var reader = await baseline.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(reader.GetDecimal(1), reader.GetDecimal(0));
        }
        finally
        {
            await RemoveFromPendingFiscalListingAsync(first.DocumentId, legacy.DocumentId);
            await RemoveFromPendingFiscalListingAsync(next.DocumentId, next.DocumentId);
        }
    }

    private sealed class ErrorCaptureLoggerProvider : ILoggerProvider
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> errors = new();
        public string Recent => string.Join("\n", errors.Reverse().Take(5).Reverse());
        public ILogger CreateLogger(string categoryName) => new ErrorCaptureLogger(this, categoryName);
        public void Dispose() { }

        private sealed class ErrorCaptureLogger(ErrorCaptureLoggerProvider owner, string categoryName) : ILogger
        {
            public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    owner.errors.Enqueue($"{categoryName}: {formatter(state, exception)} {exception}");
            }
        }

        private sealed class EmptyScope : IDisposable
        {
            public static readonly EmptyScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public async Task Invalid_valuation_evidence_rolls_back_the_balance_and_retries_the_same_movement_once()
    {
        var sale = fixture.CreateValidRequest(8_898);
        // Simulate a business/product without the new derived context reference.
        // The failed attempt must roll back a newly inserted epoch as well.
        await using (var connection = new SqlConnection(fixture.ConnectionString))
        {
            await connection.OpenAsync();
            Assert.StartsWith("AuralyServerSlice_", connection.Database);
            await using var cut = new SqlCommand("""
                DELETE head FROM inventory.CostPoolHeads head JOIN dbo.Businesses business ON business.BusinessId=head.BusinessId
                WHERE head.BusinessId=@BusinessId AND head.ProductId=@ProductId AND business.TenantId=@TenantId;
                """, connection);
            cut.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            cut.Parameters.AddWithValue("@ProductId", fixture.ProductId);
            cut.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            await cut.ExecuteNonQueryAsync();
        }
        var quantityBefore = await ReadBalanceQuantityAsync();
        var costBefore = await ReadBalanceAverageUnitCostAsync();
        var snapshotBefore = await ReadBalanceSnapshotAsync();
        fixture.PauseDocumentProcessing();
        try
        {
            using var client = fixture.CreateClient();
            await UploadAsync(client, sale);
            var signal = Assert.Single(fixture.DrainDocumentSignals());
            Assert.Equal(sale.DocumentId, signal.DocumentId);
            using var scope = fixture.CreateScope();
            var worker = scope.ServiceProvider.GetRequiredService<DocumentProcessingWorker>();
            var database = new SqlConnectionStringBuilder(fixture.ConnectionString).InitialCatalog;
            Assert.StartsWith("AuralyServerSlice_", database);
            using (var fault = new CommandCounter(database,
                commandFilter: command => command.CommandText.Contains("INSERT dbo.InventoryMovements(", StringComparison.Ordinal)
                    && command.Parameters["@DocumentId"].Value is Guid id && id == sale.DocumentId,
                beforeExecute: command =>
                {
                    Assert.Equal(1, command.Parameters["@EpochCount"].Value);
                    var movements = JsonNode.Parse((string)command.Parameters["@Movements"].Value)!.AsArray();
                    var first = movements[0]!;
                    first["PoolInventoryValueAfter"] = first["PoolInventoryValueAfter"]!.GetValue<decimal>() + 1m;
                    command.Parameters["@Movements"].Value = movements.ToJsonString();
                }))
            {
                // The check fails inside the ledger command, after balance updates.
                var error = await Assert.ThrowsAsync<SqlException>(() => worker.ProcessOneAsync(signal));
                Assert.Contains(error.Errors.Cast<SqlError>(), item => item.Number == 547);
                Assert.Equal(1, fault.Count);
            }
            Assert.Equal(quantityBefore, await ReadBalanceQuantityAsync());
            Assert.Equal(costBefore, await ReadBalanceAverageUnitCostAsync());
            Assert.Equal(snapshotBefore, await ReadBalanceSnapshotAsync());
            Assert.Equal(0, await fixture.CountAsync("InventoryMovements", sale.DocumentId));
            // Acceptance persists the header before the operational transaction.
            // A failed attempt keeps that header pending, without its line effects.
            Assert.Equal(1, await fixture.CountAsync("SalesDocuments", sale.DocumentId));
            Assert.Equal(0, await fixture.CountAsync("SalesDocumentLines", sale.DocumentId));

            await using var connection = new SqlConnection(fixture.ConnectionString);
            await connection.OpenAsync();
            await using var job = new SqlCommand("""
                SELECT job.Status,job.AvailableAt,sale.ProcessingStatus,sale.ProcessedAt
                FROM dbo.DocumentProcessingJobs job
                JOIN dbo.SalesDocuments sale ON sale.DocumentId=job.DocumentId
                WHERE job.DocumentId=@Id;
                """, connection);
            job.Parameters.AddWithValue("@Id", sale.DocumentId);
            DateTimeOffset availableAt;
            await using (var reader = await job.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal("RetryScheduled", reader.GetString(0));
                availableAt = reader.GetDateTimeOffset(1);
                Assert.Equal("Received", reader.GetString(2));
                Assert.True(reader.IsDBNull(3));
            }
            await ServerSliceFixture.WaitUntilAvailableAsync(availableAt);
            Assert.Equal(DocumentProcessingResult.Processed, await worker.ProcessOneAsync(signal));
            Assert.Equal(DocumentProcessingResult.AlreadyProcessed, await worker.ProcessOneAsync(signal));
            Assert.Equal(quantityBefore - sale.Lines[0].Quantity, await ReadBalanceQuantityAsync());
            Assert.Equal(1, await fixture.CountAsync("InventoryMovements", sale.DocumentId));
            Assert.Equal(1, await fixture.CountAsync("SalesDocuments", sale.DocumentId));
            await AssertValuationEvidenceAsync(sale.DocumentId, sale.DocumentId);
        }
        finally
        {
            fixture.ResumeDocumentProcessing();
            fixture.DrainDocumentSignals();
            await RemoveFromPendingFiscalListingAsync(sale.DocumentId, sale.DocumentId);
        }
    }

    internal sealed class CommandCounter : IObserver<System.Diagnostics.DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string database;
        private readonly System.Diagnostics.ActivityTraceId? traceId;
        private readonly Func<SqlCommand, bool>? commandFilter;
        private readonly Action<SqlCommand>? beforeExecute;
        private readonly Action<SqlCommand, double>? afterExecute;
        private readonly System.Collections.Concurrent.ConcurrentDictionary<SqlCommand, long> starts = new();
        private readonly System.Collections.Concurrent.ConcurrentBag<IDisposable> subscriptions = [];
        private readonly IDisposable allListeners;
        private int count;
        public int Count => Volatile.Read(ref count);
        public string? LastCommandText { get; private set; }
        public CommandCounter(string database, System.Diagnostics.ActivityTraceId? traceId = null,
            Func<SqlCommand, bool>? commandFilter = null, Action<SqlCommand>? beforeExecute = null,
            Action<SqlCommand, double>? afterExecute = null)
        {
            this.database = database;
            this.traceId = traceId;
            this.commandFilter = commandFilter;
            this.beforeExecute = beforeExecute;
            this.afterExecute = afterExecute;
            allListeners = System.Diagnostics.DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(System.Diagnostics.DiagnosticListener listener)
        {
            if (listener.Name == "SqlClientDiagnosticListener")
                subscriptions.Add(listener.Subscribe(this, name => name.EndsWith(".WriteCommandBefore", StringComparison.Ordinal) ||
                    afterExecute is not null && (name.EndsWith(".WriteCommandAfter", StringComparison.Ordinal) || name.EndsWith(".WriteCommandError", StringComparison.Ordinal))));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (traceId.HasValue && System.Diagnostics.Activity.Current?.TraceId != traceId.Value) return;
            if (!value.Key.EndsWith(".WriteCommandBefore", StringComparison.Ordinal))
            {
                if (value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is SqlCommand completed &&
                    starts.TryRemove(completed, out var start))
                    afterExecute?.Invoke(completed, System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                return;
            }
            if (value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is SqlCommand command &&
                command.Connection?.Database == database && (commandFilter is null || commandFilter(command)))
            {
                beforeExecute?.Invoke(command);
                if (afterExecute is not null) starts[command] = System.Diagnostics.Stopwatch.GetTimestamp();
                LastCommandText = command.CommandText;
                Interlocked.Increment(ref count);
            }
        }
        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;
        public void Dispose()
        {
            allListeners.Dispose();
            foreach (var subscription in subscriptions) subscription.Dispose();
        }
    }

    private async Task SetManageStockAsync(bool value)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE dbo.Products SET ManageStock=@Value WHERE ProductId=@ProductId;";
        command.Parameters.AddWithValue("@Value", value);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    private async Task UploadAsync(
        HttpClient client,
        Auraly.Contracts.Sales.PosSaleUploadRequest request)
    {
        using var upload = fixture.CreateUploadMessage(request);
        using var response = await client.SendAsync(upload);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<decimal?> ReadBalanceQuantityAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT QuantityOnHand
            FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (decimal)value;
    }

    private async Task<decimal?> ReadBalanceAverageUnitCostAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT AverageUnitCost
            FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId;
            """;
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (decimal)value;
    }

    private async Task<decimal> ReadProductCostBasisAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT CostBasisAmount FROM dbo.ProductPrices
            WHERE BusinessId=@BusinessId AND ProductId=@ProductId AND IsActive=1;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        return (decimal)(await command.ExecuteScalarAsync()
            ?? throw new InvalidOperationException("The product has no initial cost."));
    }

    private async Task<IReadOnlyList<MovementEvidence>> ReadMovementsAsync(Guid first, Guid second)
    {
        var result = new List<MovementEvidence>();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.ProcessingSequence, m.QuantityBefore, m.QuantityAfter,
                   m.RecognizedUnitCost, m.ValueChange
            FROM dbo.InventoryMovements m
            WHERE m.DocumentId IN (@First,@Second)
            ORDER BY m.ProcessingSequence, m.LineNumber;
            """;
        command.Parameters.AddWithValue("@First", first);
        command.Parameters.AddWithValue("@Second", second);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(new MovementEvidence(
                reader.GetInt64(0), reader.GetDecimal(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetDecimal(4)));
        }

        return result;
    }

    private async Task<string> ReadBalanceSnapshotAsync()
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT (SELECT QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt,
              (SELECT head.BusinessId,head.IsDamageWarehouse,head.EpochId,head.LastProcessingSequence
               FROM inventory.CostPoolHeads head JOIN dbo.Businesses business ON business.BusinessId=head.BusinessId
               WHERE business.TenantId=@TenantId AND head.ProductId=@ProductId
               ORDER BY head.BusinessId,head.IsDamageWarehouse FOR JSON PATH) Contexts,
              (SELECT EpochId,ScopeKind,IsDamageWarehouse,MemberBusinessIdsJson,InitialQuantity,
                 SourceDocumentId,SourceDocumentType,CreatedAt FROM inventory.CostPoolEpochs
               WHERE TenantId=@TenantId AND ProductId=@ProductId ORDER BY EpochId FOR JSON PATH) ContextHistory
            FROM dbo.InventoryBalances
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId AND ProductId=@ProductId
            FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        command.Parameters.AddWithValue("@ProductId", fixture.ProductId);
        command.Parameters.AddWithValue("@TenantId", fixture.TenantId);
        var snapshot = Assert.IsType<string>(await command.ExecuteScalarAsync());
        using var evidence = JsonDocument.Parse(snapshot);
        Assert.Equal(JsonValueKind.Object, evidence.RootElement.ValueKind);
        return snapshot;
    }

    private async Task<Guid?> ReadCostPoolEpochAsync(Guid documentId)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT CostPoolEpochId FROM dbo.InventoryMovements WHERE DocumentId=@Id;", connection);
        command.Parameters.AddWithValue("@Id", documentId);
        return await command.ExecuteScalarAsync() is Guid id ? id : null;
    }

    private async Task AssertValuationEvidenceAsync(Guid first, Guid second)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            SELECT ValuationMode,CostPoolScope,PoolQuantityBefore,PoolQuantityAfter,
              PoolInventoryValueBefore,PoolInventoryValueAfter,QuantityChange,ValueChange
            FROM dbo.InventoryMovements
            WHERE BusinessId=@BusinessId AND DocumentId IN(@First,@Second)
            ORDER BY ProcessingSequence,LineNumber;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        command.Parameters.AddWithValue("@First", first);
        command.Parameters.AddWithValue("@Second", second);
        await using var reader = await command.ExecuteReaderAsync();
        (decimal Quantity, decimal Value)? previous = null;
        var count = 0;
        while (await reader.ReadAsync())
        {
            Assert.Equal("AverageCost", reader.GetString(0));
            Assert.Contains(reader.GetString(1), new[] { "Business", "SharedTenant" });
            Assert.Equal(reader.GetDecimal(2) + reader.GetDecimal(6), reader.GetDecimal(3));
            Assert.Equal(reader.GetDecimal(5) - reader.GetDecimal(4), reader.GetDecimal(7));
            if (previous is { } before)
            {
                Assert.Equal(before.Quantity, reader.GetDecimal(2));
                Assert.Equal(before.Value, reader.GetDecimal(4));
            }
            previous = (reader.GetDecimal(3), reader.GetDecimal(5));
            count++;
        }
        Assert.True(count > 0);
    }

    private async Task RemoveFromPendingFiscalListingAsync(Guid first, Guid second)
    {
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE dbo.FiscalDocumentProcesses
            SET Status=N'DianAccepted', UpdatedAt=SYSDATETIMEOFFSET()
            WHERE DocumentId IN (@First,@Second);
            """;
        command.Parameters.AddWithValue("@First", first);
        command.Parameters.AddWithValue("@Second", second);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record MovementEvidence(
        long ProcessingSequence,
        decimal QuantityBefore,
        decimal QuantityAfter,
        decimal RecognizedUnitCost,
        decimal ValueChange);
}
