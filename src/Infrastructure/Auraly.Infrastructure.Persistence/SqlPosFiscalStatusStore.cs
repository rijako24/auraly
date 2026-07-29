using System.Data;
using Auraly.Application.Fiscal;
using Auraly.Contracts.Fiscal;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPosFiscalStatusStore(
    SqlServerConnectionFactory connections) : IPosFiscalStatusStore
{
    public async Task<PosFiscalStatusPage> PageAsync(
        PosFiscalDeviceContext device,
        string? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (@Take)
                   d.DocumentId,d.FiscalNumber,d.CufeReceived,p.Status,
                   p.LastStatusCode,p.LastStatusDescription,p.UpdatedAt,p.RowVersion
            FROM dbo.FiscalDocumentProcesses p
            INNER JOIN dbo.SalesDocuments d ON d.DocumentId=p.DocumentId
            WHERE d.BusinessId=@BusinessId
              AND d.DeviceId=@DeviceId
              AND d.RegisterId=@RegisterId
              AND p.RowVersion>@Cursor
            ORDER BY p.RowVersion;
            """;
        var decodedCursor = PosFiscalStatusService.DecodeCursor(cursor);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Take", pageSize + 1);
        command.Parameters.AddWithValue("@BusinessId", device.BusinessId);
        command.Parameters.AddWithValue("@DeviceId", device.DeviceId);
        command.Parameters.AddWithValue("@RegisterId", device.RegisterId);
        command.Parameters.Add("@Cursor", SqlDbType.Timestamp, 8).Value = decodedCursor;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<(PosFiscalStatusChange Item, byte[] Cursor)>();
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add((new PosFiscalStatusChange(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetDateTimeOffset(6)),
                (byte[])reader[7]));
        }

        var hasMore = rows.Count > pageSize;
        var page = rows.Take(pageSize).ToArray();
        var nextCursor = page.Length == 0
            ? PosFiscalStatusService.EncodeCursor(decodedCursor)
            : PosFiscalStatusService.EncodeCursor(page[^1].Cursor);
        return new PosFiscalStatusPage(
            page.Select(row => row.Item).ToArray(),
            nextCursor,
            hasMore);
    }
}
