using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlCatalogStore
{
    public async Task<IReadOnlyList<ProductRotationDetail>> ProductRotationAsync(
        Guid tenantId, Guid businessId, Guid productId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("dbo.CatalogProductRotationGet", connection)
        {
            CommandType = System.Data.CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@TenantId", tenantId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ProductId", productId);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            var items = new List<ProductRotationDetail>();
            while (await reader.ReadAsync(ct))
                items.Add(new(reader.GetGuid(0),reader.GetString(1),reader.GetString(2),reader.GetDecimal(3),
                    reader.GetDecimal(4),reader.GetDecimal(5),reader.GetDecimal(6),reader.GetDecimal(7),
                    reader.GetDecimal(8),reader.GetDecimal(9),reader.GetDecimal(10),reader.GetDecimal(11),
                    reader.IsDBNull(12)?null:reader.GetDecimal(12),DateOnly.FromDateTime(reader.GetDateTime(13)),
                    reader.GetFieldValue<DateTimeOffset>(14)));
            return items;
        }
        catch (SqlException exception) when (exception.Number == 51010)
        { throw new Auraly.Application.Catalog.CatalogValidationException(exception.Message); }
    }
}
