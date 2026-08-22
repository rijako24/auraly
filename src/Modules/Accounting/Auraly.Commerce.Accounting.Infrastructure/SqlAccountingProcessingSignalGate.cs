using Auraly.Commerce.Accounting.Application;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed class SqlAccountingProcessingSignalGate(
    AccountingSqlConnectionFactory connections) : IAccountingProcessingSignalGate
{
    public async Task<bool> HasPendingWorkAsync(
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT COUNT_BIG(1) FROM dbo.AccountingPostingJobs
            WHERE BusinessId=@BusinessId AND SourceDocumentId=@DocumentId
              AND SourceDocumentType=@DocumentType AND Status<>N'Posted';
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }
}
