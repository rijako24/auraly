using System.Data;
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
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("dbo.TenantDianDocumentQuotaReserve", connection, transaction)
        { CommandType = CommandType.StoredProcedure };
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", sourceDocumentId);
        command.Parameters.AddWithValue("@DocumentKind", documentKind);
        command.Parameters.AddWithValue("@Now", now);
        var reserved = command.Parameters.Add("@Reserved", SqlDbType.Bit);
        reserved.Direction = ParameterDirection.Output;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return reserved.Value is true;
    }
}
