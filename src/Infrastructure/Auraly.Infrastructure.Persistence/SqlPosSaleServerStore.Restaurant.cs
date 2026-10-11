using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleServerStore
{
    private static async Task ValidateRestaurantProductModeAsync(
        SqlConnection connection, SqlTransaction transaction,
        PosSaleUploadRequest request, CancellationToken ct)
    {
        if (request.Lines.Count == 0) return;
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(*)
            FROM OPENJSON(@Lines) WITH
              (LineNumber int, ProductId uniqueidentifier) input
            JOIN dbo.Products product
              ON product.ProductId=input.ProductId
             AND product.TenantId=@TenantId
            LEFT JOIN sales.RestaurantCheckoutLines restaurant
              ON restaurant.DocumentId=@DocumentId
             AND restaurant.BusinessId=@BusinessId
             AND restaurant.TenantId=@TenantId
             AND restaurant.LineNumber=input.LineNumber
            LEFT JOIN sales.RestaurantAccountAdditions addition
              ON addition.AccountId=restaurant.AccountId
             AND addition.AdditionId=restaurant.AdditionId
             AND addition.TenantId=restaurant.TenantId
            -- A local POS may upload a sale captured before this product was
            -- marked as an ingredient. The catalog change must not block sync.
            WHERE (product.IsIngredient=1 AND restaurant.DocumentId IS NULL
                   AND @SourceMode=N'Online')
               OR (restaurant.DocumentId IS NOT NULL AND
                   (addition.AdditionId IS NULL OR
                    addition.ManageStockAtSend IS NULL OR
                    addition.ManageStockAtSend<>product.ManageStock));
            """, connection, transaction);
        command.Parameters.Add("@Lines", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(request.Lines.Select(line => new {
                line.LineNumber, line.ProductId
            }));
        command.Parameters.AddWithValue("@TenantId", request.TenantId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.Add("@SourceMode", SqlDbType.NVarChar, 16).Value =
            request.SourceMode;
        if ((long)(await command.ExecuteScalarAsync(ct) ?? 0L) > 0)
            throw new PosSaleInvalidException(
                "Hay ingredientes no vendibles o productos enviados cuya modalidad de inventario cambió.");
    }
}
