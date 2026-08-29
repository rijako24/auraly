using Auraly.Contracts.Catalog;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosCatalogStore
{
    public async Task ApplyReferenceOptionsAsync(
        string catalogCode,
        IReadOnlyCollection<ReferenceOption> options,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = connection.CreateCommand())
        {
            delete.Transaction = (SqliteTransaction)transaction;
            delete.CommandText = "DELETE FROM PosReferenceOptions WHERE CatalogCode=$catalog;";
            delete.Parameters.AddWithValue("$catalog", catalogCode);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var option in options)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = (SqliteTransaction)transaction;
            insert.CommandText = """
                INSERT INTO PosReferenceOptions(
                    CatalogCode,OptionId,Code,Label,Description,SortOrder)
                VALUES($catalog,$id,$code,$label,$description,$sortOrder);
                """;
            insert.Parameters.AddWithValue("$catalog", catalogCode);
            insert.Parameters.AddWithValue("$id", option.Id.ToString("D"));
            insert.Parameters.AddWithValue("$code", option.Code);
            insert.Parameters.AddWithValue("$label", option.Label);
            insert.Parameters.AddWithValue(
                "$description", (object?)option.Description ?? DBNull.Value);
            insert.Parameters.AddWithValue("$sortOrder", option.SortOrder);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReferenceOption>> ReferenceOptionsAsync(
        string catalogCode,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT OptionId,Code,Label,Description,SortOrder
            FROM PosReferenceOptions
            WHERE CatalogCode=$catalog
            ORDER BY SortOrder,Label,Code;
            """;
        command.Parameters.AddWithValue("$catalog", catalogCode);
        var result = new List<ReferenceOption>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new ReferenceOption(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4)));
        }
        return result;
    }
}
