using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Receivables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlReceivablePaymentDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => ReceivablesDocumentTypes.Payment;

    public async Task HandleAsync(ConfirmedDocument document, CancellationToken token)
    {
        var payment=CustomerPaymentContractSerializer.Deserialize(document.Payload);
        if(payment.PaymentId!=document.DocumentId.Value||payment.BusinessId!=document.BusinessId.Value||payment.TenantId!=document.TenantId.Value||
           payment.Allocations.Count==0||payment.TotalAmount!=payment.Allocations.Sum(x=>x.Amount))
            throw new InvalidOperationException("The customer receipt envelope or allocations are inconsistent.");
        var session=sessions.Current;
        await LockPaymentAsync(session,payment,token);
        foreach(var allocation in payment.Allocations.OrderBy(x=>x.LineNumber)) await ApplyAsync(session,payment,allocation,token);
        await CompleteAsync(session,payment,token);
        if(payment.WorkSessionId is Guid workSessionId) await InsertWorkSessionMovementAsync(session,payment,workSessionId,token);
        await SqlAccountingPostingJobWriter.InsertAsync(session,document,payment.PaidAt,ids,timeProvider,token);
        await using var outbox=new SqlCommand("INSERT dbo.ServerOutboxMessages(MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt) VALUES(@Id,@DocumentId,N'ReceivablePayment',N'receivables.customer-payment.processed',@Payload,@Now)",session.Connection,session.Transaction);
        outbox.Parameters.AddWithValue("@Id",ids.NewId());outbox.Parameters.AddWithValue("@DocumentId",payment.PaymentId);outbox.Parameters.AddWithValue("@Payload",document.Payload);outbox.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());await outbox.ExecuteNonQueryAsync(token);
    }

    private static async Task LockPaymentAsync(SqlDocumentProcessingSessionAccessor.Session session,CustomerPaymentDocumentPayload payment,CancellationToken token)
    {
        await using var command=new SqlCommand("SELECT CustomerId,CurrencyCode,TotalAmount,Status FROM dbo.CustomerPayments WITH(UPDLOCK,HOLDLOCK) WHERE PaymentId=@Id AND BusinessId=@BusinessId",session.Connection,session.Transaction);
        command.Parameters.AddWithValue("@Id",payment.PaymentId);command.Parameters.AddWithValue("@BusinessId",payment.BusinessId);
        await using var reader=await command.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token)||reader.GetGuid(0)!=payment.CustomerId||reader.GetString(1)!=payment.CurrencyCode||reader.GetDecimal(2)!=payment.TotalAmount||reader.GetString(3)!="Accepted")
            throw new InvalidOperationException("The accepted customer receipt no longer matches its immutable payload.");
    }

    private async Task ApplyAsync(SqlDocumentProcessingSessionAccessor.Session session,CustomerPaymentDocumentPayload payment,CustomerPaymentAllocationSnapshot allocation,CancellationToken token)
    {
        decimal balance;string status;
        await using(var read=new SqlCommand("""
            SELECT r.OutstandingAmount,r.Status FROM dbo.Receivables r WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.CustomerPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
              ON a.ReceivableId=r.ReceivableId AND a.PaymentId=@PaymentId AND a.LineNumber=@Line AND a.Amount=@Amount AND a.AppliedAt IS NULL
            WHERE r.ReceivableId=@ReceivableId AND r.BusinessId=@BusinessId AND r.CustomerId=@CustomerId AND r.CurrencyCode=@Currency;
            """,session.Connection,session.Transaction))
        {
            read.Parameters.AddWithValue("@PaymentId",payment.PaymentId);read.Parameters.AddWithValue("@Line",allocation.LineNumber);Money(read,"@Amount",allocation.Amount);read.Parameters.AddWithValue("@ReceivableId",allocation.ReceivableId);read.Parameters.AddWithValue("@BusinessId",payment.BusinessId);read.Parameters.AddWithValue("@CustomerId",payment.CustomerId);read.Parameters.AddWithValue("@Currency",payment.CurrencyCode);
            await using var reader=await read.ExecuteReaderAsync(token);if(!await reader.ReadAsync(token))throw new InvalidOperationException("The receipt allocation no longer matches its reserved receivable.");balance=reader.GetDecimal(0);status=reader.GetString(1);
        }
        if(status is "Paid" or "Cancelled"||allocation.Amount>balance)throw new InvalidOperationException("The reserved receipt cannot be applied to the current balance.");
        var after=decimal.Round(balance-allocation.Amount,4);var next=after==0?"Paid":"PartiallyPaid";var now=timeProvider.GetUtcNow();
        await using var command=new SqlCommand("""
            UPDATE dbo.Receivables SET OutstandingAmount=@After,Status=@Status WHERE ReceivableId=@ReceivableId;
            UPDATE dbo.CustomerPaymentApplications SET AppliedAt=@Now WHERE PaymentId=@PaymentId AND LineNumber=@Line AND AppliedAt IS NULL;
            INSERT dbo.ReceivableTransactions(ReceivableTransactionId,ReceivableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@ReceivableId,N'Payment',@Amount,@PaymentId,@PaidAt,@Now);
            """,session.Connection,session.Transaction);
        command.Parameters.AddWithValue("@After",after);command.Parameters.AddWithValue("@Status",next);command.Parameters.AddWithValue("@ReceivableId",allocation.ReceivableId);command.Parameters.AddWithValue("@PaymentId",payment.PaymentId);command.Parameters.AddWithValue("@Line",allocation.LineNumber);command.Parameters.AddWithValue("@Now",now);command.Parameters.AddWithValue("@TransactionId",ids.NewId());Money(command,"@Amount",allocation.Amount);command.Parameters.AddWithValue("@PaidAt",payment.PaidAt);
        if(await command.ExecuteNonQueryAsync(token)!=3)throw new DBConcurrencyException("The receipt was not applied atomically.");
    }

    private static async Task CompleteAsync(SqlDocumentProcessingSessionAccessor.Session session,CustomerPaymentDocumentPayload payment,CancellationToken token)
    {
        await using var command=new SqlCommand("UPDATE dbo.CustomerPayments SET Status=N'Processed',ProcessedAt=SYSDATETIMEOFFSET() WHERE PaymentId=@Id AND Status=N'Accepted' AND NOT EXISTS(SELECT 1 FROM dbo.CustomerPaymentApplications WHERE PaymentId=@Id AND AppliedAt IS NULL)",session.Connection,session.Transaction);command.Parameters.AddWithValue("@Id",payment.PaymentId);if(await command.ExecuteNonQueryAsync(token)!=1)throw new DBConcurrencyException("The customer receipt could not be completed.");
    }

    private async Task InsertWorkSessionMovementAsync(SqlDocumentProcessingSessionAccessor.Session session,CustomerPaymentDocumentPayload payment,Guid workSessionId,CancellationToken token)
    {
        await using var command=new SqlCommand("""
            INSERT dbo.WorkSessionMovements(WorkSessionMovementId,WorkSessionId,DocumentId,PaymentNumber,BusinessDate,MovementType,PaymentMethodCode,Amount,Reference,SourceKey,OccurredAt,RecordedByUserId)
            VALUES(@Id,@SessionId,NULL,NULL,@Date,N'ReceivablePayment',@Method,@Amount,@Reference,@SourceKey,@OccurredAt,@UserId);
            """,session.Connection,session.Transaction);
        command.Parameters.AddWithValue("@Id",ids.NewId());command.Parameters.AddWithValue("@SessionId",workSessionId);command.Parameters.Add(new SqlParameter("@Date",SqlDbType.Date){Value=payment.PaidAt.Date});command.Parameters.AddWithValue("@Method",payment.PaymentMethod);Money(command,"@Amount",payment.TotalAmount);command.Parameters.AddWithValue("@Reference",(object?)payment.Reference??DBNull.Value);command.Parameters.AddWithValue("@SourceKey",$"receivable-payment:{payment.PaymentId:D}");command.Parameters.AddWithValue("@OccurredAt",payment.PaidAt);command.Parameters.AddWithValue("@UserId",payment.ConfirmedByUserId);await command.ExecuteNonQueryAsync(token);
    }
    private static void Money(SqlCommand c,string name,decimal value){var p=c.Parameters.Add(name,SqlDbType.Decimal);p.Precision=19;p.Scale=4;p.Value=value;}
}
