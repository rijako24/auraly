using System.Net;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

[Collection(ServerSliceCollection.Name)]
public sealed class InventoryBalanceProcessingTests(ServerSliceFixture fixture)
{
    [Fact]
    public async Task Sales_update_the_authoritative_balance_in_sequence_and_a_duplicate_has_no_effect()
    {
        var quantityBefore = await ReadBalanceQuantityAsync() ?? 0m;
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
                Assert.Equal(0m, movement.RecognizedUnitCost);
                Assert.Equal(0m, movement.ValueChange);
            },
            movement =>
            {
                Assert.Equal(quantityBefore - first.Lines[0].Quantity, movement.QuantityBefore);
                Assert.Equal(
                    quantityBefore - first.Lines[0].Quantity - second.Lines[0].Quantity,
                    movement.QuantityAfter);
                Assert.True(movement.ProcessingSequence > movements[0].ProcessingSequence);
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
