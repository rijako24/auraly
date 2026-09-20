using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

internal static class SqlDianDocumentQuota
{
    public static async Task<bool> TryReserveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid sourceDocumentId,
        string documentKind,
        DateTimeOffset now,
        CancellationToken cancellationToken) => await TryReserveManyAsync(connection, transaction,
            businessId, [sourceDocumentId], documentKind, now, cancellationToken);

    public static async Task<bool> TryReserveManyAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, IReadOnlyList<Guid> sourceDocumentIds, string documentKind, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (sourceDocumentIds.Count is < 1 or > 100 || sourceDocumentIds.Any(id => id == Guid.Empty) ||
            sourceDocumentIds.Distinct().Count() != sourceDocumentIds.Count)
            throw new ArgumentException("Quota reservation requires a bounded set of distinct documents.", nameof(sourceDocumentIds));
        await using var command = new SqlCommand("dbo.TenantDianDocumentQuotaReserve", connection, transaction)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", sourceDocumentIds[0]);
        if (sourceDocumentIds.Count > 1)
            command.Parameters.Add("@DocumentIdsJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(sourceDocumentIds);
        command.Parameters.AddWithValue("@DocumentKind", documentKind);
        command.Parameters.AddWithValue("@Now", now);
        var reserved = command.Parameters.Add("@Reserved", SqlDbType.Bit);
        reserved.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return reserved.Value is true;
    }
}
