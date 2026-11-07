using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.ServerSlice.IntegrationTests;

public sealed partial class OnlineSalesCheckoutTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Generic_upgrade_backfills_only_a_new_column_and_preserves_captured_history_on_replay(bool introduceColumn)
    {
        var userId = await CreateUserAsync("generic-upgrade");
        using var client = fixture.CreateUserClient(userId, CommercePermissionCodes.SalesCreate);
        var draft = await CaptureAsync(client, await OpenAsync(client));
        var sale = await CompleteAsync(client, draft.DraftId,
            new(draft.Version, [new OnlineSalesPayment("Cash", draft.PayableAmount, null)]), Guid.NewGuid().ToString("N"));
        var next = await CaptureAsync(client, await OpenAsync(client));
        var orderId = Guid.NewGuid();
        await using var connection = new SqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            await using (var setup = new SqlCommand("""
                UPDATE dbo.Products SET IsGenericProduct=1,ManageStock=0 WHERE ProductId=@Product;
                INSERT dbo.Orders(OrderId,BusinessId) VALUES(@Order,@Business);
                INSERT dbo.OrderItems(OrderId,BusinessId,ProductId,ProductNameSnapshot,Quantity,UnitPrice,DocumentUnitCost,LineTotal)
                VALUES(@Order,@Business,@Product,N'Producto histórico',1,100,50,100);
                """, connection, transaction))
            {
                setup.Parameters.AddWithValue("@Product", fixture.ProductId);
                setup.Parameters.AddWithValue("@Order", orderId);
                setup.Parameters.AddWithValue("@Business", fixture.BusinessId);
                await setup.ExecuteNonQueryAsync();
            }
            if (introduceColumn)
            {
                await using var remove = new SqlCommand("""
                    ALTER TABLE dbo.OrderItems DROP CONSTRAINT DF_OrderItems_IsGenericProductSnapshot;
                    ALTER TABLE dbo.OrderItems DROP COLUMN IsGenericProductSnapshot;
                    ALTER TABLE dbo.SalesDraftLines DROP CONSTRAINT DF_SalesDraftLines_IsGenericProductSnapshot;
                    ALTER TABLE dbo.SalesDraftLines DROP COLUMN IsGenericProductSnapshot;
                    ALTER TABLE dbo.SalesDocumentLines DROP CONSTRAINT DF_SalesDocumentLines_IsGenericProductSnapshot;
                    ALTER TABLE dbo.SalesDocumentLines DROP COLUMN IsGenericProductSnapshot;
                    """, connection, transaction);
                await remove.ExecuteNonQueryAsync();
            }
            await fixture.ApplyMigrationAsync("20260916_AddGenericProductMode.sql", connection, transaction);
            await AssertFlagsAsync(introduceColumn);
            await using (var changeToday = new SqlCommand("UPDATE dbo.Products SET IsGenericProduct=0 WHERE ProductId=@Id;", connection, transaction))
            {
                changeToday.Parameters.AddWithValue("@Id", fixture.ProductId);
                await changeToday.ExecuteNonQueryAsync();
            }
            await fixture.ApplyMigrationAsync("20260916_AddGenericProductMode.sql", connection, transaction);
            await AssertFlagsAsync(introduceColumn);
        }
        finally { await transaction.RollbackAsync(); }

        async Task AssertFlagsAsync(bool expected)
        {
            await using var read = new SqlCommand("""
                SELECT IsGenericProductSnapshot FROM dbo.OrderItems WHERE OrderId=@Order
                UNION ALL SELECT IsGenericProductSnapshot FROM dbo.SalesDraftLines WHERE SalesDraftId=@Draft
                UNION ALL SELECT IsGenericProductSnapshot FROM dbo.SalesDocumentLines WHERE DocumentId=@Document;
                """, connection, transaction);
            read.Parameters.AddWithValue("@Order", orderId);
            read.Parameters.AddWithValue("@Draft", next.DraftId);
            read.Parameters.AddWithValue("@Document", sale.Receipt.DocumentId);
            await using var reader = await read.ExecuteReaderAsync();
            var count = 0;
            while (await reader.ReadAsync()) { Assert.Equal(expected, reader.GetBoolean(0)); count++; }
            Assert.Equal(3, count);
        }
    }
}
