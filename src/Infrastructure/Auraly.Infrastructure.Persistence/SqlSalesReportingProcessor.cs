using System.Data;
using Auraly.Application.Sales;
using Auraly.Contracts.Returns;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlSalesReportingProcessor(
    SqlServerConnectionFactory connections,
    SqlSalesReportingProjectionWriter projectionWriter)
{
    public async Task ProcessAsync(
        Guid documentId,
        string documentType,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        if (!SalesReportingProcessingPolicy.Supports(documentType)) return;

        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var source = await LockSourceAsync(
                connection, transaction, documentId, documentType, businessId,
                cancellationToken);
            if (source is null)
                throw new InvalidOperationException(
                    "The completed document has no immutable reporting source.");
            if (source.AlreadyProjected)
            {
                await transaction.CommitAsync(cancellationToken);
                return;
            }

            var session = new SalesReportingSqlSession(connection, transaction);
            if (documentType is "SalesInvoice" or "SalesReceipt")
                await projectionWriter.ProjectSaleAsync(
                    session, PosSaleContractSerializer.Deserialize(source.Payload),
                    cancellationToken);
            else
                await projectionWriter.ProjectReturnAsync(
                    session, SalesReturnContractSerializer.Deserialize(source.Payload),
                    cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<ReportingSource?> LockSourceAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid documentId, string documentType, Guid businessId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.PayloadJson,
                   CONVERT(bit,CASE WHEN
                     (@DocumentType IN(N'SalesInvoice',N'SalesReceipt') AND EXISTS
                       (SELECT 1 FROM dbo.SalesReportDocuments WITH(UPDLOCK,HOLDLOCK)
                        WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId))
                     OR
                     (@DocumentType=N'SalesReturn' AND EXISTS
                       (SELECT 1 FROM dbo.SalesReportLineFacts WITH(UPDLOCK,HOLDLOCK)
                        WHERE SourceDocumentId=@DocumentId
                          AND SourceDocumentType=N'SalesReturn'))
                     THEN 1 ELSE 0 END)
            FROM dbo.DocumentProcessingPayloads p
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=p.DocumentId
             AND j.DocumentType=p.DocumentType
             AND j.BusinessId=p.BusinessId
            WHERE p.DocumentId=@DocumentId
              AND p.DocumentType=@DocumentType
              AND p.BusinessId=@BusinessId
              AND j.Status=N'Completed';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ReportingSource(reader.GetString(0), reader.GetBoolean(1))
            : null;
    }

    private sealed record ReportingSource(string Payload, bool AlreadyProjected);
}
