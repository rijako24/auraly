using System.Data;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlPosSaleDocumentHandler
{
    private async Task InsertReceivableAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PosSaleUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Credit is null) return;

        decimal? creditLimit;
        bool enabled;
        decimal outstanding;
        await using (var read = new SqlCommand("""
            SELECT cp.CreditLimit,CAST(COALESCE(cp.IsCreditEnabled,0) AS bit),
                   COALESCE(SUM(CASE WHEN r.Status IN (N'Open',N'PartiallyPaid')
                                     THEN r.OutstandingAmount ELSE 0 END),0)
            FROM dbo.Customers c WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.CustomerCreditProfiles cp WITH(UPDLOCK,HOLDLOCK)
              ON cp.CustomerId=c.CustomerId AND cp.BusinessId=c.BusinessId
            LEFT JOIN dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
              ON r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId AND c.IsActive=1
            GROUP BY cp.CreditLimit,cp.IsCreditEnabled;
            """, session.Connection, session.Transaction))
        {
            read.Parameters.AddWithValue("@CustomerId", request.Credit.CustomerId);
            read.Parameters.AddWithValue("@BusinessId", request.BusinessId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException("The debtor is not an active customer of this business.");
            creditLimit = reader.IsDBNull(0) ? null : reader.GetDecimal(0);
            enabled = reader.GetBoolean(1);
            outstanding = reader.GetDecimal(2);
        }
        if (!enabled)
            throw new InvalidOperationException("Credit sales are not enabled for this customer.");
        if (creditLimit is not null && outstanding + request.Credit.Amount > creditLimit)
            throw new InvalidOperationException("The customer credit limit would be exceeded.");

        var receivableId = _idGenerator.NewId();
        var transactionId = _idGenerator.NewId();
        var now = _timeProvider.GetUtcNow();
        await using var command = new SqlCommand("""
            INSERT dbo.Receivables
              (ReceivableId,BusinessId,CustomerId,SourceDocumentId,SourceDocumentType,
               DocumentNumber,CurrencyCode,OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            VALUES
              (@ReceivableId,@BusinessId,@CustomerId,@DocumentId,N'SalesInvoice',
               @DocumentNumber,'COP',@Amount,@Amount,@DueDate,N'Open',@Now);
            INSERT dbo.ReceivableTransactions
              (ReceivableTransactionId,ReceivableId,TransactionType,Amount,
               SourceDocumentId,OccurredAt,CreatedAt)
            VALUES
              (@TransactionId,@ReceivableId,N'Opening',@Amount,@DocumentId,@IssuedAt,@Now);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@ReceivableId", receivableId);
        command.Parameters.AddWithValue("@TransactionId", transactionId);
        command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
        command.Parameters.AddWithValue("@CustomerId", request.Credit.CustomerId);
        command.Parameters.AddWithValue("@DocumentId", request.DocumentId);
        command.Parameters.AddWithValue("@DocumentNumber", request.DocumentNumber.FullNumber);
        var amount = command.Parameters.Add("@Amount", SqlDbType.Decimal);
        amount.Precision = 19; amount.Scale = 4; amount.Value = request.Credit.Amount;
        command.Parameters.AddWithValue("@DueDate", request.Credit.DueDate);
        command.Parameters.AddWithValue("@IssuedAt", request.CommercialSnapshot.IssuedAt);
        command.Parameters.AddWithValue("@Now", now);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 2)
            throw new DBConcurrencyException("The receivable was not opened atomically with the sale.");
    }
}
