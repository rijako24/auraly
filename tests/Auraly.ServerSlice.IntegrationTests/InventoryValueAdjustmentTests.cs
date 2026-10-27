using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Domain.Inventory;
using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
[Trait("EngineCertification", "Operational")]
public sealed partial class InventoryValueAdjustmentTests(ServerSliceFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(false, 300)]
    [InlineData(true, 300)]
    [InlineData(false, -300)]
    public async Task The_existing_writer_revalues_the_pool_without_moving_stock_or_creating_an_obligation(
        bool shared, decimal adjustment)
    {
        await using var scope = await OpenLedgerAsync(1, shared);
        var before = await ReadFinancialCountsAsync(scope);
        var posting = Posting(scope, 0, adjustment);
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        using (var commands = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database))
        {
            var result = Assert.Single(await writer.PostBatchAsync(scope.Session, [posting], default));
            Assert.Equal(2, commands.Count);
            Assert.Equal(3, result.QuantityAfter);
            Assert.Equal(adjustment, result.ValueChange);
            Assert.Equal(0, result.RecognizedUnitCost);
        }

        await using var state = new SqlCommand("""
            SELECT QuantityOnHand,AverageUnitCost,InventoryValue FROM dbo.InventoryBalances
            WHERE ProductId=@ProductId ORDER BY QuantityOnHand;
            SELECT QuantityChange,PoolQuantityBefore,PoolQuantityAfter,PoolInventoryValueBefore,
              PoolInventoryValueAfter,ValueChange,ValuationMode,CostPoolScope
            FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId;
            """, scope.Connection, scope.Transaction);
        state.Parameters.AddWithValue("@ProductId", scope.Products[0]);
        state.Parameters.AddWithValue("@DocumentId", scope.DocumentId);
        await using (var reader = await state.ExecuteReaderAsync())
        {
            var average = adjustment > 0 ? 130m : 70m;
            Assert.True(await reader.ReadAsync());
            Assert.Equal(3, reader.GetDecimal(0));
            Assert.Equal(average, reader.GetDecimal(1));
            Assert.Equal(3 * average, reader.GetDecimal(2));
            Assert.True(await reader.ReadAsync());
            Assert.Equal(7, reader.GetDecimal(0));
            Assert.Equal(average, reader.GetDecimal(1));
            Assert.Equal(7 * average, reader.GetDecimal(2));
            Assert.False(await reader.ReadAsync());
            Assert.True(await reader.NextResultAsync());
            Assert.True(await reader.ReadAsync());
            Assert.Equal(0, reader.GetDecimal(0));
            Assert.Equal(10, reader.GetDecimal(1));
            Assert.Equal(10, reader.GetDecimal(2));
            Assert.Equal(1000, reader.GetDecimal(3));
            Assert.Equal(1000 + adjustment, reader.GetDecimal(4));
            Assert.Equal(adjustment, reader.GetDecimal(5));
            Assert.Equal("ValueAdjustment", reader.GetString(6));
            Assert.Equal(shared ? "SharedTenant" : "Business", reader.GetString(7));
            Assert.False(await reader.ReadAsync());
        }
        Assert.Equal(before, await ReadFinancialCountsAsync(scope));

        // A zero physical movement is valid only with complete value-only facts.
        foreach (var invalid in new[] { "ValuationMode=NULL", "ValuationMode=N'AverageCost'", "QuantityChange=1", "PoolInventoryValueAfter=PoolInventoryValueAfter+1" })
        {
            await using var malformed = new SqlCommand(
                $"UPDATE dbo.InventoryMovements SET {invalid} WHERE DocumentId=@Id", scope.Connection, scope.Transaction);
            malformed.Parameters.AddWithValue("@Id", scope.DocumentId);
            var error = await Assert.ThrowsAsync<SqlException>(() => malformed.ExecuteNonQueryAsync());
            Assert.Equal(547, error.Number);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(20)]
    [InlineData(500)]
    [Trait("Performance", "true")]
    public async Task Distinct_products_keep_two_ledger_commands_and_the_existing_processing_budget(int count)
    {
        await using var scope = await OpenLedgerAsync(count, false);
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        var postings = Enumerable.Range(0, count).Select(index => Posting(scope, index, 300)).ToArray();
        var samples = new List<double>(20);
        for (var sample = 0; sample < 20; sample++)
        {
            // Each measured call starts from the same locked pool. Savepoint
            // setup/rollback is outside the measured production ledger path.
            await scope.Transaction.SaveAsync("ValueSample");
            var watch = Stopwatch.StartNew();
            using (var commands = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database))
            {
                var results = await writer.PostBatchAsync(scope.Session, postings, default);
                watch.Stop();
                Assert.Equal(2, commands.Count);
                Assert.Equal(count, results.Count);
                Assert.All(results, result => { Assert.Equal(3, result.QuantityAfter); Assert.Equal(300, result.ValueChange); });
                samples.Add(watch.Elapsed.TotalMilliseconds);
            }
            await scope.Transaction.RollbackAsync("ValueSample");
        }
        var p95 = samples.Order().ElementAt(18);
        output.WriteLine($"Value-only ledger: {count} distinct products; 2 SQL commands; 20 samples; p95 {p95:F1} ms; max {samples.Max():F1} ms.");
        Assert.True(p95 < 2000, $"Ledger p95 was {p95:F1} ms.");
    }

    [Fact]
    public async Task Returns_the_actual_rounded_book_delta_instead_of_the_requested_amount()
    {
        await using var scope = await OpenLedgerAsync(1, false);
        await using (var seed = new SqlCommand("""
            UPDATE dbo.InventoryBalances SET QuantityOnHand=0.5,AverageUnitCost=1000,InventoryValue=500
            WHERE ProductId=@ProductId;
            """, scope.Connection, scope.Transaction))
        {
            seed.Parameters.AddWithValue("@ProductId", scope.Products[0]);
            Assert.Equal(2, await seed.ExecuteNonQueryAsync());
        }
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        var result = Assert.Single(await writer.PostBatchAsync(scope.Session, [Posting(scope, 0, 0.0001m)], default));
        Assert.Equal(0.5m, result.QuantityAfter);
        Assert.Equal(1000.0001m, result.AverageUnitCostAfter);
        Assert.Equal(0.0002m, result.ValueChange);
        await using var verify = new SqlCommand("""
            SELECT (SELECT SUM(InventoryValue)-1000 FROM dbo.InventoryBalances WHERE ProductId=@ProductId),
              ValueChange,PoolInventoryValueAfter-PoolInventoryValueBefore
            FROM dbo.InventoryMovements WHERE DocumentId=@DocumentId;
            """, scope.Connection, scope.Transaction);
        verify.Parameters.AddWithValue("@ProductId", scope.Products[0]);
        verify.Parameters.AddWithValue("@DocumentId", scope.DocumentId);
        await using var reader = await verify.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal(result.ValueChange, reader.GetDecimal(0));
        Assert.Equal(result.ValueChange, reader.GetDecimal(1));
        Assert.Equal(result.ValueChange, reader.GetDecimal(2));
    }

    [Fact]
    public async Task A_failure_after_balance_updates_leaves_no_value_adjustment_when_the_owner_rolls_back()
    {
        await using var scope = await OpenLedgerAsync(2, false);
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        using (var fault = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database,
            commandFilter: command => command.CommandText.Contains("INSERT dbo.InventoryMovements(", StringComparison.Ordinal),
            beforeExecute: command =>
            {
                var rows = System.Text.Json.Nodes.JsonNode.Parse((string)command.Parameters["@Movements"].Value)!.AsArray();
                rows[^1]!["PoolInventoryValueAfter"] = 999999m;
                command.Parameters["@Movements"].Value = rows.ToJsonString();
            }))
        {
            var error = await Assert.ThrowsAsync<SqlException>(() => writer.PostBatchAsync(scope.Session,
                [Posting(scope, 0, 300), Posting(scope, 1, 300)], default));
            Assert.Equal(547, error.Number);
            Assert.Equal(1, fault.Count);
        }
        await scope.RollbackAsync();
        await using var verify = new SqlCommand("""
            SELECT (SELECT COUNT(*) FROM dbo.InventoryMovements WHERE DocumentId=@Id)
              +(SELECT COUNT(*) FROM inventory.CostPoolEpochs WHERE SourceDocumentId=@Id)
              +(SELECT COUNT(*) FROM dbo.InventoryBalances WHERE ProductId IN(SELECT value FROM OPENJSON(@Products)));
            """, scope.Connection);
        verify.Parameters.AddWithValue("@Id", scope.DocumentId);
        verify.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(scope.Products));
        Assert.Equal(0, await verify.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Invalid_pool_or_physical_arguments_fail_before_any_persist_command()
    {
        await using var scope = await OpenLedgerAsync(1, false);
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        foreach (var invalid in new[] {
            Posting(scope, 0, 300) with { QuantityChange = 0.00000001m },
            Posting(scope, 0, 300) with { SpecifiedUnitCost = 100 },
            Posting(scope, 0, -1000.0001m) })
        {
            using var commands = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database);
            var error = await Record.ExceptionAsync(() => writer.PostBatchAsync(scope.Session, [invalid], default));
            if (invalid.ValueAdjustmentAmount < -1000m) Assert.IsType<InvalidOperationException>(error);
            else Assert.IsType<ArgumentException>(error);
            Assert.Equal(1, commands.Count);
        }
        await using var empty = new SqlCommand("""
            UPDATE dbo.InventoryBalances SET QuantityOnHand=0,InventoryValue=0
            WHERE ProductId=@ProductId;
            """, scope.Connection, scope.Transaction);
        empty.Parameters.AddWithValue("@ProductId", scope.Products[0]);
        Assert.Equal(2, await empty.ExecuteNonQueryAsync());
        using var emptyCommands = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.PostBatchAsync(scope.Session, [Posting(scope, 0, 300)], default));
        Assert.Equal(1, emptyCommands.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cannot_capitalize_into_a_non_stock_product_or_the_damage_warehouse(bool damage)
    {
        await using var scope = await OpenLedgerAsync(1, false);
        var posting = Posting(scope, 0, 300);
        await using var prepare = new SqlCommand(damage ? """
            DECLARE @Warehouse uniqueidentifier=(SELECT WarehouseId FROM dbo.Warehouses
              WHERE BusinessId=@BusinessId AND IsSystem=1 AND Code=N'AVE');
            IF @Warehouse IS NULL
            BEGIN
              SET @Warehouse=NEWID();
              INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,IsSystem,CreatedAt)
                VALUES(@Warehouse,@BusinessId,N'AVE',N'Damage',0,1,SYSUTCDATETIME());
            END;
            SELECT @Warehouse;
            """ : """
            UPDATE dbo.Products SET ManageStock=0 WHERE ProductId=@ProductId;
            SELECT @WarehouseId;
            """, scope.Connection, scope.Transaction);
        prepare.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
        prepare.Parameters.AddWithValue("@ProductId", scope.Products[0]);
        prepare.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
        var warehouse = Assert.IsType<Guid>(await prepare.ExecuteScalarAsync());
        if (damage) posting = posting with { WarehouseId = warehouse, MovementType = "DamageWarehouseIn" };
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        using var commands = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.PostBatchAsync(scope.Session, [posting], default));
        Assert.Contains(damage ? "Damaged inventory" : "stock-managed", error.Message);
        Assert.Equal(1, commands.Count);
    }

    [Theory]
    [InlineData("Business")]
    [InlineData("Document")]
    [InlineData("DocumentType")]
    public async Task A_batch_cannot_escape_the_active_engine_session(string mismatch)
    {
        await using var scope = await OpenLedgerAsync(1, false);
        var posting = Posting(scope, 0, 300);
        posting = mismatch switch
        {
            "Business" => posting with { BusinessId = Guid.NewGuid() },
            "Document" => posting with { DocumentId = Guid.NewGuid() },
            _ => posting with { DocumentType = "SalesInvoice" }
        };
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        using var commands = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => writer.PostBatchAsync(scope.Session, [posting], default));
        Assert.Contains("active document-processing session", error.Message);
        Assert.Equal(0, commands.Count);
    }

    [Fact]
    public async Task The_pool_load_requires_the_same_tenant_as_the_active_engine_session()
    {
        await using var scope = await OpenLedgerAsync(1, false);
        var session = scope.Session with { Context = scope.Session.Context with { TenantId = new TenantId(Guid.NewGuid()) } };
        using var services = fixture.CreateScope();
        var writer = services.ServiceProvider.GetRequiredService<SqlInventoryLedgerWriter>();
        using var commands = new InventoryBalanceProcessingTests.CommandCounter(scope.Connection.Database);
        var error = await Assert.ThrowsAsync<SqlException>(() => writer.PostBatchAsync(session, [Posting(scope, 0, 300)], default));
        Assert.Equal(51600, error.Number);
        Assert.Equal(1, commands.Count);
    }

    private InventoryLedgerPosting Posting(LedgerScope scope, int index, decimal adjustment) => new(
        fixture.BusinessId, fixture.WarehouseId, scope.Products[index], scope.DocumentId, "GoodsReceipt",
        index + 1, "ReceiptCostAdjustment", 0, null, InventoryValuationMode.ValueAdjustment,
        DateTimeOffset.UtcNow, ValueAdjustmentAmount: adjustment);

    private async Task<LedgerScope> OpenLedgerAsync(int count, bool shared)
    {
        var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        Assert.StartsWith("AuralyServerSlice_", connection.Database);
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var products = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
            var documentId = Guid.NewGuid();
            var jobId = Guid.NewGuid();
            var otherBusiness = shared ? Guid.NewGuid() : fixture.BusinessId;
            await using var seed = new SqlCommand("""
                UPDATE dbo.Businesses SET SharesProductPrices=@Shared WHERE BusinessId=@BusinessId;
                IF @Shared=1
                  INSERT dbo.Businesses(BusinessId,TenantId,Name,Description,Address,Phone,Email,Website,SharesProductPrices,IsActive,CreatedAt)
                  VALUES(@OtherBusinessId,@TenantId,N'Value-only pool',N'',N'',N'',N'',N'',1,1,SYSUTCDATETIME());
                INSERT dbo.Warehouses(WarehouseId,BusinessId,Code,Name,AllowNegativeStockSales,CreatedAt)
                  VALUES(@OtherWarehouseId,@OtherBusinessId,N'VALUE-TEST',N'Value-only test',0,SYSUTCDATETIME());
                INSERT dbo.TaxProfiles(TaxProfileId,TenantId,Code,Name,Rate,IsActive,CreatedAt)
                  VALUES(@TaxProfileId,@TenantId,N'VALUE-TEST',N'Value-only tax profile',0,1,SYSDATETIMEOFFSET());
                INSERT dbo.Products(ProductId,TenantId,ProductCode,BaseUnitCode,TaxProfileId,Source,Sku,Name,Currency,ManageStock,IsActive,CreatedAt)
                  SELECT input.ProductId,@TenantId,CONVERT(nvarchar(36),input.ProductId),N'EA',
                    @TaxProfileId,0,CONVERT(nvarchar(36),input.ProductId),N'Value-only stock',N'COP',1,1,SYSUTCDATETIME()
                  FROM OPENJSON(@Products) WITH(ProductId uniqueidentifier '$') input;
                INSERT dbo.InventoryBalances(BusinessId,WarehouseId,ProductId,QuantityOnHand,AverageUnitCost,InventoryValue,LastProcessingSequence,UpdatedAt)
                  SELECT warehouse.BusinessId,warehouse.WarehouseId,input.ProductId,warehouse.Quantity,100,warehouse.Quantity*100,0,SYSDATETIMEOFFSET()
                  FROM OPENJSON(@Products) WITH(ProductId uniqueidentifier '$') input
                  CROSS JOIN(VALUES(@BusinessId,@WarehouseId,3),(@OtherBusinessId,@OtherWarehouseId,7)) warehouse(BusinessId,WarehouseId,Quantity);
                DECLARE @Sequence bigint=(SELECT COALESCE(MAX(ProcessingSequence),0)+1 FROM dbo.DocumentProcessingJobs WHERE BusinessId=@BusinessId);
                INSERT dbo.DocumentProcessingJobs(JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
                  VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'GoodsReceipt',N'Pending',SYSDATETIMEOFFSET(),SYSDATETIMEOFFSET());
                SELECT @Sequence;
                """, connection, transaction);
            seed.Parameters.AddWithValue("@Shared", shared);
            seed.Parameters.AddWithValue("@TenantId", fixture.TenantId);
            seed.Parameters.AddWithValue("@BusinessId", fixture.BusinessId);
            seed.Parameters.AddWithValue("@WarehouseId", fixture.WarehouseId);
            seed.Parameters.AddWithValue("@OtherBusinessId", otherBusiness);
            seed.Parameters.AddWithValue("@OtherWarehouseId", Guid.NewGuid());
            seed.Parameters.AddWithValue("@TaxProfileId", Guid.NewGuid());
            seed.Parameters.AddWithValue("@DocumentId", documentId);
            seed.Parameters.AddWithValue("@JobId", jobId);
            seed.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(products));
            var sequence = Assert.IsType<long>(await seed.ExecuteScalarAsync());
            var context = new DocumentProcessingContext(new(fixture.TenantId), new(fixture.BusinessId), new(documentId), "GoodsReceipt");
            return new(connection, transaction, new(connection, transaction, context, jobId, sequence), products, documentId);
        }
        catch
        {
            await transaction.RollbackAsync();
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task<string> ReadFinancialCountsAsync(LedgerScope scope)
    {
        await using var command = new SqlCommand("""
            SELECT (SELECT
              (SELECT COUNT(*) FROM dbo.Payables) Payables,
              (SELECT COUNT(*) FROM dbo.PayableTransactions) Transactions,
              (SELECT COUNT(*) FROM dbo.AccountingSourceDocuments) Sources,
              (SELECT COUNT(*) FROM dbo.AccountingEntries) Entries,
              (SELECT COUNT(*) FROM dbo.SupplierPayments) Payments,
              (SELECT COUNT(*) FROM dbo.FiscalDocumentProcesses) Fiscal
              FOR JSON PATH,WITHOUT_ARRAY_WRAPPER);
            """, scope.Connection, scope.Transaction);
        return Assert.IsType<string>(await command.ExecuteScalarAsync());
    }

    // These are writer-boundary tests in a rolled-back disposable database.
    // They do not represent a public cost-revision endpoint or bypass any
    // production document handler, lease or accounting posting flow.
    private sealed class LedgerScope(SqlConnection connection, SqlTransaction transaction,
        SqlDocumentProcessingSessionAccessor.Session session, Guid[] products, Guid documentId) : IAsyncDisposable
    {
        public SqlConnection Connection { get; } = connection;
        public SqlTransaction Transaction { get; } = transaction;
        public SqlDocumentProcessingSessionAccessor.Session Session { get; } = session;
        public Guid[] Products { get; } = products;
        public Guid DocumentId { get; } = documentId;
        private bool rolledBack;

        public async Task RollbackAsync()
        {
            if (rolledBack) return;
            await Transaction.RollbackAsync();
            rolledBack = true;
        }

        public async ValueTask DisposeAsync()
        {
            try { await RollbackAsync(); }
            finally { await Transaction.DisposeAsync(); await Connection.DisposeAsync(); }
        }
    }
}
