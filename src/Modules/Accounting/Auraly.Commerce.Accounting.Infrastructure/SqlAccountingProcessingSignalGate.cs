using Auraly.Commerce.Accounting.Application;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed class SqlAccountingProcessingSignalGate(
    AccountingSqlConnectionFactory connections) : IAccountingProcessingSignalGate
{
    public async Task<IReadOnlyList<AccountingPendingWork>> ListPendingWorkAsync(
        Guid businessId,
        Guid documentId,
        string documentType,
        CancellationToken cancellationToken = default)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("""
            SELECT job.SourceDocumentId,job.SourceDocumentType
            FROM dbo.AccountingPostingJobs job
            WHERE job.BusinessId=@BusinessId AND job.Status<>N'Posted'
              AND
              (
                (job.SourceDocumentId=@DocumentId AND job.SourceDocumentType=@DocumentType)
                OR
                (
                  @DocumentType=N'GoodsReceipt'
                  AND job.SourceDocumentType=N'GoodsReceiptCostDocument'
                  AND EXISTS
                  (
                    SELECT 1 FROM purchasing.GoodsReceiptCostDocuments cost
                    INNER JOIN dbo.GoodsReceipts receipt
                      ON receipt.GoodsReceiptId=cost.GoodsReceiptId
                     AND receipt.BusinessId=@BusinessId
                    WHERE cost.GoodsReceiptId=@DocumentId
                      AND cost.CostDocumentId=job.SourceDocumentId
                  )
                )
              )
            ORDER BY CASE WHEN job.SourceDocumentId=@DocumentId THEN 0 ELSE 1 END,
                     job.SourceDocumentId;
            """, connection);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        var result = new List<AccountingPendingWork>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new AccountingPendingWork(reader.GetGuid(0), reader.GetString(1)));
        return result;
    }
}
