using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal sealed record InventoryProductResolution(Guid ProductId, decimal Factor);

internal static class SqlProductLinkResolution
{
    public static async Task<InventoryProductResolution> ResolveInventoryAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid businessId,
        Guid productId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COALESCE(l.ParentProductId,p.ProductId),
                   CASE WHEN l.ProductLinkId IS NULL THEN CAST(1 AS DECIMAL(19,6)) ELSE l.InventoryFactor END
            FROM dbo.Products p WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.ProductLinks l WITH(UPDLOCK,HOLDLOCK)
              ON l.BusinessId=@BusinessId AND l.ChildProductId=p.ProductId
             AND l.SharesInventory=1 AND l.IsActive=1
            WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
              AND p.ProductId=@ProductId;
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ProductId", productId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The inventory product is outside the business.");
        return new(reader.GetGuid(0), reader.GetDecimal(1));
    }
}
