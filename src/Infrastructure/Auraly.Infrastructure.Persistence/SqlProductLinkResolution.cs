using System.Data;
using System.Text.Json;
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
        => (await ResolveInventoryBatchAsync(session, businessId, [productId], cancellationToken))[productId];

    public static async Task<IReadOnlyDictionary<Guid, InventoryProductResolution>> ResolveInventoryBatchAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        Guid businessId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken)
    {
        if (productIds.Count == 0) return new Dictionary<Guid, InventoryProductResolution>();
        var requested = productIds.Distinct().ToArray();
        const string sql = """
            DECLARE @Products TABLE(ProductId uniqueidentifier PRIMARY KEY);
            INSERT @Products SELECT ProductId FROM OPENJSON(@ProductsJson) WITH(ProductId uniqueidentifier '$');
            SELECT p.ProductId,COALESCE(l.ParentProductId,p.ProductId),
                   CASE WHEN l.ProductLinkId IS NULL THEN CAST(1 AS DECIMAL(19,6)) ELSE l.InventoryFactor END
            FROM @Products input
            JOIN dbo.Products p WITH(UPDLOCK,HOLDLOCK) ON p.ProductId=input.ProductId
            LEFT JOIN dbo.ProductLinks l WITH(UPDLOCK,HOLDLOCK)
              ON l.TenantId=p.TenantId AND l.ChildProductId=p.ProductId
             AND l.SharesInventory=1 AND l.IsActive=1
            WHERE p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
            ORDER BY p.ProductId OPTION(RECOMPILE);
            """;
        await using var command = new SqlCommand(sql, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.Add("@ProductsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(requested);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<Guid, InventoryProductResolution>(requested.Length);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(reader.GetGuid(0), new(reader.GetGuid(1), reader.GetDecimal(2)));
        if (result.Count != requested.Length)
            throw new InvalidOperationException("The inventory product is outside the business.");
        return result;
    }
}
