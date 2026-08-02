using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Payables;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPayablePaymentDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => PayablesDocumentTypes.Payment;

    public async Task HandleAsync(
        ConfirmedDocument document,
        CancellationToken cancellationToken)
    {
        var payment = SupplierPaymentContractSerializer.Deserialize(document.Payload);
        if (payment.PaymentId != document.DocumentId.Value ||
            payment.BusinessId != document.BusinessId.Value ||
            payment.TenantId != document.TenantId.Value)
            throw new InvalidOperationException(
                "The supplier payment envelope does not match its payload.");
        if (payment.Allocations.Count == 0 ||
            payment.TotalAmount != payment.Allocations.Sum(item => item.Amount))
            throw new InvalidOperationException(
                "The immutable supplier payment allocations do not reconcile.");

        var session = sessions.Current;
        await LockPaymentAsync(session, payment, cancellationToken);
        foreach (var allocation in payment.Allocations.OrderBy(item => item.LineNumber))
            await ApplyAsync(session, payment, allocation, cancellationToken);
        await CompletePaymentAsync(session, payment, cancellationToken);
        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, payment.PaidAt, ids, timeProvider, cancellationToken);
        await InsertOutboxAsync(session, payment, document.Payload, cancellationToken);
    }

    private static async Task LockPaymentAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SupplierPaymentDocumentPayload payment,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT SupplierId,CurrencyCode,TotalAmount,Status
            FROM dbo.SupplierPayments WITH(UPDLOCK,HOLDLOCK)
            WHERE PaymentId=@PaymentId AND BusinessId=@BusinessId;
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
        command.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("The accepted supplier payment was not found.");
        if (reader.GetGuid(0) != payment.SupplierId ||
            !string.Equals(reader.GetString(1), payment.CurrencyCode, StringComparison.Ordinal) ||
            reader.GetDecimal(2) != payment.TotalAmount || reader.GetString(3) != "Accepted")
            throw new InvalidOperationException(
                "The accepted supplier payment no longer matches its immutable payload.");
    }

    private async Task ApplyAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SupplierPaymentDocumentPayload payment,
        SupplierPaymentAllocationSnapshot allocation,
        CancellationToken cancellationToken)
    {
        decimal outstanding;
        string status;
        await using (var read = new SqlCommand("""
            SELECT p.OutstandingAmount,p.Status
            FROM dbo.Payables p WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.SupplierPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
              ON a.PayableId=p.PayableId AND a.PaymentId=@PaymentId
             AND a.LineNumber=@LineNumber AND a.Amount=@Amount AND a.AppliedAt IS NULL
            WHERE p.PayableId=@PayableId AND p.BusinessId=@BusinessId
              AND p.SupplierId=@SupplierId AND p.CurrencyCode=@Currency;
            """, session.Connection, session.Transaction))
        {
            read.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
            read.Parameters.AddWithValue("@LineNumber", allocation.LineNumber);
            read.Parameters.AddWithValue("@Amount", allocation.Amount);
            read.Parameters.AddWithValue("@PayableId", allocation.PayableId);
            read.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
            read.Parameters.AddWithValue("@SupplierId", payment.SupplierId);
            read.Parameters.AddWithValue("@Currency", payment.CurrencyCode);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new InvalidOperationException(
                    "The payment allocation no longer matches its reserved obligation.");
            outstanding = reader.GetDecimal(0); status = reader.GetString(1);
        }
        if (status is "Paid" or "Cancelled" || allocation.Amount > outstanding)
            throw new InvalidOperationException(
                "The reserved payment cannot be applied to the current obligation balance.");
        var after = decimal.Round(outstanding - allocation.Amount, 4);
        var nextStatus = after == 0 ? "Paid" : "PartiallyPaid";
        await using var command = new SqlCommand("""
            UPDATE dbo.Payables SET OutstandingAmount=@After,Status=@Status
            WHERE PayableId=@PayableId;
            UPDATE dbo.SupplierPaymentApplications SET AppliedAt=@Now
            WHERE PaymentId=@PaymentId AND LineNumber=@LineNumber AND AppliedAt IS NULL;
            INSERT dbo.PayableTransactions
              (PayableTransactionId,PayableId,TransactionType,Amount,SourceDocumentId,OccurredAt,CreatedAt)
            VALUES(@TransactionId,@PayableId,N'Payment',@Amount,@PaymentId,@PaidAt,@Now);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@After", after);
        command.Parameters.AddWithValue("@Status", nextStatus);
        command.Parameters.AddWithValue("@PayableId", allocation.PayableId);
        command.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
        command.Parameters.AddWithValue("@LineNumber", allocation.LineNumber);
        command.Parameters.AddWithValue("@TransactionId", ids.NewId());
        command.Parameters.AddWithValue("@Amount", allocation.Amount);
        command.Parameters.AddWithValue("@PaidAt", payment.PaidAt);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 3)
            throw new DBConcurrencyException(
                "The supplier payment application was not persisted atomically.");
    }

    private async Task CompletePaymentAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SupplierPaymentDocumentPayload payment,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            UPDATE dbo.SupplierPayments SET Status=N'Processed',ProcessedAt=@Now
            WHERE PaymentId=@PaymentId AND BusinessId=@BusinessId AND Status=N'Accepted'
              AND NOT EXISTS(SELECT 1 FROM dbo.SupplierPaymentApplications
                             WHERE PaymentId=@PaymentId AND AppliedAt IS NULL);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@PaymentId", payment.PaymentId);
        command.Parameters.AddWithValue("@BusinessId", payment.BusinessId);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new DBConcurrencyException("The supplier payment could not be completed.");
    }

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        SupplierPaymentDocumentPayload payment,
        string payload,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.ServerOutboxMessages
              (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@Id,@DocumentId,N'PayablePayment',N'payables.supplier-payment.processed',@Payload,@Now);
            """, session.Connection, session.Transaction);
        command.Parameters.AddWithValue("@Id", ids.NewId());
        command.Parameters.AddWithValue("@DocumentId", payment.PaymentId);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.AddWithValue("@Now", timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
