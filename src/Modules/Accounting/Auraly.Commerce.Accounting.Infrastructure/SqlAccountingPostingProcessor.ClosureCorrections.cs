using System.Data;
using System.Text.Json;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Commerce.Accounting.Infrastructure;

public sealed partial class SqlAccountingPostingProcessor
{
    private async Task ApplyClosureCorrectionFinancialEffectsAsync(
        SqlConnection connection, SqlTransaction transaction,
        WorkSessionClosureReconciliationPayload payload, CancellationToken token)
    {
        var changes = (payload.PaymentCorrections ?? [])
            .Where(item => item.Amount != item.OriginalAmount)
            .Select(item => new
            {
                item.MovementType, item.SourceId, item.SubledgerId, item.CustomerId,
                Delta = item.Amount - item.OriginalAmount,
                AdjustmentId = ids.NewId(), NewReceivableId = ids.NewId(),
                NewOpeningId = ids.NewId(), NewCreditId = ids.NewId(),
                OutboxId = ids.NewId()
            }).ToArray();
        if (changes.Length == 0) return;

        await using var command = new SqlCommand("""
            CREATE TABLE #Changes(MovementType nvarchar(32),SourceId uniqueidentifier,
              SubledgerId uniqueidentifier,CustomerId uniqueidentifier,Delta decimal(19,4),
              AdjustmentId uniqueidentifier,NewReceivableId uniqueidentifier,
              NewOpeningId uniqueidentifier,NewCreditId uniqueidentifier,OutboxId uniqueidentifier);
            INSERT #Changes SELECT MovementType,SourceId,SubledgerId,CustomerId,Delta,
              AdjustmentId,NewReceivableId,NewOpeningId,NewCreditId,OutboxId
            FROM OPENJSON(@Rows) WITH(MovementType nvarchar(32),SourceId uniqueidentifier,
              SubledgerId uniqueidentifier,CustomerId uniqueidentifier,Delta decimal(19,4),
              AdjustmentId uniqueidentifier,NewReceivableId uniqueidentifier,
              NewOpeningId uniqueidentifier,NewCreditId uniqueidentifier,OutboxId uniqueidentifier);
            IF @@ROWCOUNT<>@Count THROW 51607,'The closure correction payload is incomplete.',1;
            IF EXISTS(SELECT 1 FROM #Changes WHERE Delta=0 OR MovementType NOT IN
              (N'Sale',N'ReceivablePayment',N'PayablePayment',N'Refund',N'CashIn',N'CashOut'))
              THROW 51607,'The closure correction type is invalid.',1;

            CREATE TABLE #Receivable(ReceivableId uniqueidentifier PRIMARY KEY,
              Adjustment decimal(19,4),AdjustmentId uniqueidentifier);
            INSERT #Receivable SELECT SubledgerId,-SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),AdjustmentId)))
            FROM #Changes WHERE MovementType IN(N'Sale',N'ReceivablePayment')
              AND SubledgerId IS NOT NULL GROUP BY SubledgerId;
            DELETE FROM #Receivable WHERE Adjustment=0;
            UPDATE balance SET OutstandingAmount=balance.OutstandingAmount+input.Adjustment,
              Status=CASE WHEN balance.OutstandingAmount+input.Adjustment=0 THEN N'Paid'
                WHEN balance.OutstandingAmount+input.Adjustment>=balance.OriginalAmount THEN N'Open'
                ELSE N'PartiallyPaid' END
            FROM dbo.Receivables balance WITH(UPDLOCK,HOLDLOCK)
            JOIN #Receivable input ON input.ReceivableId=balance.ReceivableId
            WHERE balance.BusinessId=@BusinessId AND balance.Status<>N'Cancelled'
              AND balance.OutstandingAmount+input.Adjustment>=0;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #Receivable)
              THROW 51607,'The receivable changed after closure acceptance.',1;
            INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,
              TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT AdjustmentId,ReceivableId,N'Adjustment',Adjustment,@ReconciliationId,@At,@Now
            FROM #Receivable;

            CREATE TABLE #NewReceivable(SourceId uniqueidentifier PRIMARY KEY,
              CustomerId uniqueidentifier,Adjustment decimal(19,4),
              ReceivableId uniqueidentifier,OpeningId uniqueidentifier);
            INSERT #NewReceivable SELECT SourceId,
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),CustomerId))),-SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),NewReceivableId))),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),NewOpeningId)))
            FROM #Changes WHERE MovementType=N'Sale' AND SubledgerId IS NULL
            GROUP BY SourceId;
            DELETE FROM #NewReceivable WHERE Adjustment=0;
            IF EXISTS(SELECT 1 FROM #NewReceivable WHERE Adjustment<=0 OR CustomerId IS NULL)
              THROW 51607,'The sale cannot open an unidentified customer balance.',1;
            INSERT dbo.Receivables(ReceivableId,BusinessId,CustomerId,PartySiteId,
              SourceDocumentId,SourceDocumentType,DocumentNumber,CurrencyCode,
              OriginalAmount,OutstandingAmount,DueDate,Status,CreatedAt)
            SELECT input.ReceivableId,@BusinessId,input.CustomerId,sale.CustomerPartySiteId,
              sale.DocumentId,sale.DocumentType,sale.DocumentNumber,N'COP',
              input.Adjustment,input.Adjustment,@At,N'Open',@Now
            FROM #NewReceivable input JOIN dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK)
              ON sale.DocumentId=input.SourceId AND sale.BusinessId=@BusinessId
                AND sale.CustomerId=input.CustomerId AND sale.CustomerPartySiteId IS NOT NULL
            WHERE NOT EXISTS(SELECT 1 FROM dbo.Receivables existing WITH(UPDLOCK,HOLDLOCK)
              WHERE existing.SourceDocumentId=sale.DocumentId AND existing.SourceDocumentType=sale.DocumentType);
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #NewReceivable)
              THROW 51607,'The sale balance changed after closure acceptance.',1;
            INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,
              TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT OpeningId,ReceivableId,N'Opening',Adjustment,@ReconciliationId,@At,@Now
            FROM #NewReceivable;

            CREATE TABLE #Payable(PayableId uniqueidentifier PRIMARY KEY,
              Adjustment decimal(19,4),AdjustmentId uniqueidentifier);
            INSERT #Payable SELECT SubledgerId,SUM(Delta),
              CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),AdjustmentId)))
            FROM #Changes WHERE MovementType=N'PayablePayment' GROUP BY SubledgerId;
            DELETE FROM #Payable WHERE Adjustment=0;
            UPDATE balance SET OutstandingAmount=balance.OutstandingAmount+input.Adjustment,
              FunctionalOutstandingAmount=CASE WHEN balance.FunctionalOutstandingAmount IS NULL THEN NULL
                ELSE balance.FunctionalOutstandingAmount+input.Adjustment*balance.ExchangeRate END,
              Status=CASE WHEN balance.OutstandingAmount+input.Adjustment=0 THEN N'Paid'
                WHEN balance.OutstandingAmount+input.Adjustment>=balance.OriginalAmount THEN N'Open'
                ELSE N'PartiallyPaid' END
            FROM dbo.Payables balance WITH(UPDLOCK,HOLDLOCK)
            JOIN #Payable input ON input.PayableId=balance.PayableId
            WHERE balance.BusinessId=@BusinessId AND balance.Status<>N'Cancelled'
              AND balance.OutstandingAmount+input.Adjustment>=0;
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #Payable)
              THROW 51607,'The payable changed after closure acceptance.',1;
            INSERT dbo.PayableTransactions(PayableTransactionId,PayableId,
              TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            SELECT AdjustmentId,PayableId,N'Adjustment',Adjustment,@ReconciliationId,@At,@Now
            FROM #Payable;

            INSERT dbo.CustomerCredits(CustomerCreditId,BusinessId,CustomerId,SourceReturnId,
              OriginalAmount,AvailableAmount,Status,CreatedAt)
            SELECT input.NewCreditId,@BusinessId,input.CustomerId,returned.ReturnId,
              input.Delta,input.Delta,N'Open',@Now
            FROM #Changes input JOIN dbo.SalesReturns returned WITH(UPDLOCK,HOLDLOCK)
              ON input.MovementType=N'Refund' AND returned.ReturnId=input.SourceId
                AND returned.BusinessId=@BusinessId AND returned.CustomerId=input.CustomerId
                AND returned.TotalAmount>=input.Delta
            WHERE input.Delta>0 AND NOT EXISTS(SELECT 1 FROM dbo.CustomerCredits credit WITH(UPDLOCK,HOLDLOCK)
              WHERE credit.SourceReturnId=returned.ReturnId);
            IF @@ROWCOUNT<>(SELECT COUNT(*) FROM #Changes WHERE MovementType=N'Refund')
              THROW 51607,'The return credit changed after closure acceptance.',1;

            DECLARE @BaseCursor bigint;
            SELECT @BaseCursor=COALESCE(MAX(AvailableThroughCursor),0)
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'Customers';
            WITH Customers AS
            (SELECT CustomerId,CONVERT(uniqueidentifier,MIN(CONVERT(nvarchar(36),OutboxId))) OutboxId,
              ROW_NUMBER() OVER(ORDER BY CustomerId) Sequence
             FROM #Changes WHERE CustomerId IS NOT NULL AND MovementType IN
               (N'Sale',N'ReceivablePayment',N'Refund')
             GROUP BY CustomerId)
            INSERT dbo.PosSynchronizationOutboxMessages(NotificationId,BusinessId,
              Stream,AvailableThroughCursor,OccurredAt,EntityType,EntityId,ChangeKind)
            SELECT OutboxId,@BusinessId,N'Customers',@BaseCursor+Sequence,@Now,
              N'Customer',CustomerId,N'Upsert' FROM Customers;
            """, connection, transaction);
        command.Parameters.Add("@Rows", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(changes);
        command.Parameters.AddWithValue("@Count", changes.Length);
        command.Parameters.AddWithValue("@BusinessId", payload.BusinessId);
        command.Parameters.AddWithValue("@ReconciliationId", payload.ReconciliationId);
        command.Parameters.AddWithValue("@At", payload.ReconciledAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(token);
    }
}
