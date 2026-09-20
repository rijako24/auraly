using System.Text.Json;
using Auraly.Contracts.Sales;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed partial class PosCatalogStore
{
    public async Task StageInvoiceChargePageAsync(Guid businessId, long cursor, InvoiceChargePage page,
        CancellationToken ct = default)
    {
        if (businessId == Guid.Empty || cursor < 0 || page.Page < 1 || page.PageSize != 100 ||
            page.Items.Count > page.PageSize || page.Items.Any(x => x.BusinessId != businessId) ||
            page.TotalCount < 0 || page.TotalPages != (int)Math.Ceiling(page.TotalCount / 100m))
            throw new InvalidDataException("El paquete de cargos no corresponde a la sede o paginación solicitada.");
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM PosInvoiceChargeStaging WHERE $page=1;
            INSERT INTO PosInvoiceChargeStaging(ChargeId,BusinessId,Version,Code,Name,SortOrder,Payload,Cursor)
            SELECT json_extract(value,'$.chargeId'),json_extract(value,'$.businessId'),
              json_extract(value,'$.version'),json_extract(value,'$.code'),json_extract(value,'$.name'),
              json_extract(value,'$.sortOrder'),value,$cursor FROM json_each($items);
            """;
        command.Parameters.AddWithValue("$page", page.Page);
        command.Parameters.AddWithValue("$cursor", cursor);
        command.Parameters.AddWithValue("$items", JsonSerializer.Serialize(page.Items, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task PromoteInvoiceChargesAsync(Guid businessId, long cursor, int expectedCount,
        CancellationToken ct = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction();
        await using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT COUNT(*) FROM PosInvoiceChargeStaging WHERE BusinessId=$business AND Cursor=$cursor;";
            check.Parameters.AddWithValue("$business", businessId.ToString("D"));
            check.Parameters.AddWithValue("$cursor", cursor);
            if (Convert.ToInt32(await check.ExecuteScalarAsync(ct)) != expectedCount)
                throw new InvalidDataException("La descarga de cargos está incompleta; se conserva la configuración anterior.");
        }
        await using var promote = connection.CreateCommand();
        promote.Transaction = transaction;
        promote.CommandText = """
            DELETE FROM PosInvoiceCharges;
            INSERT INTO PosInvoiceCharges(ChargeId,BusinessId,Version,Code,Name,SortOrder,Payload,Cursor)
            SELECT ChargeId,BusinessId,Version,Code,Name,SortOrder,Payload,Cursor
              FROM PosInvoiceChargeStaging WHERE BusinessId=$business AND Cursor=$cursor;
            DELETE FROM PosInvoiceChargeStaging;
            UPDATE PosPricingSynchronizationState SET ConfigurationCursor=$cursor WHERE StateId=1;
            """;
        promote.Parameters.AddWithValue("$business", businessId.ToString("D"));
        promote.Parameters.AddWithValue("$cursor", cursor);
        await promote.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<InvoiceChargePage> InvoiceChargesAsync(Guid businessId, int page = 1,
        CancellationToken ct = default)
    {
        if (page is < 1 or > 1000000) throw new ArgumentOutOfRangeException(nameof(page));
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM PosInvoiceCharges WHERE BusinessId=$business;
            SELECT Payload FROM PosInvoiceCharges WHERE BusinessId=$business
            ORDER BY SortOrder,Code LIMIT 100 OFFSET $offset;
            """;
        command.Parameters.AddWithValue("$business", businessId.ToString("D"));
        command.Parameters.AddWithValue("$offset", (page - 1) * 100);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        var count = reader.GetInt32(0);
        await reader.NextResultAsync(ct);
        var items = new List<InvoiceChargeDefinition>();
        while (await reader.ReadAsync(ct))
            items.Add(JsonSerializer.Deserialize<InvoiceChargeDefinition>(reader.GetString(0),
                new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new InvalidDataException("El cargo local no contiene un snapshot válido."));
        return new(items, page, 100, count, (int)Math.Ceiling(count / 100m));
    }
}
