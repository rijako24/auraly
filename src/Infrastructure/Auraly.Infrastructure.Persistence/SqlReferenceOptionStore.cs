using Auraly.Application.Catalog;
using Auraly.Contracts.Catalog;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlReferenceOptionStore(SqlServerConnectionFactory connections)
    : IReferenceOptionStore
{
    public async Task<IReadOnlyList<ReferenceOption>> ListAsync(
        string catalogCode,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT OptionId,Code,Label,Description,SortOrder
            FROM reference.Options
            WHERE CatalogCode=@CatalogCode AND IsActive=1
            ORDER BY SortOrder,Label,Code;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@CatalogCode", catalogCode);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<ReferenceOption>();
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReferenceOption(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)));
        }

        return result;
    }
}
