using System.Net;
using Microsoft.Data.SqlClient;

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
        }
        finally
        {
            await RemoveFromPendingFiscalListingAsync(warmup.DocumentId, single.DocumentId);
            await RemoveFromPendingFiscalListingAsync(source.DocumentId, source.DocumentId);
        }
    }

    private sealed class CommandCounter : IObserver<System.Diagnostics.DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly string database;
        private readonly System.Collections.Concurrent.ConcurrentBag<IDisposable> subscriptions = [];
        private readonly IDisposable allListeners;
        private int count;
        public int Count => Volatile.Read(ref count);
        public CommandCounter(string database)
        {
            this.database = database;
            allListeners = System.Diagnostics.DiagnosticListener.AllListeners.Subscribe(this);
        }
        public void OnNext(System.Diagnostics.DiagnosticListener listener)
        {
            if (listener.Name == "SqlClientDiagnosticListener")
                subscriptions.Add(listener.Subscribe(this, name => name.EndsWith(".WriteCommandBefore", StringComparison.Ordinal)));
        }
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value?.GetType().GetProperty("Command")?.GetValue(value.Value) is SqlCommand command &&
                command.Connection?.Database == database) Interlocked.Increment(ref count);
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
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
