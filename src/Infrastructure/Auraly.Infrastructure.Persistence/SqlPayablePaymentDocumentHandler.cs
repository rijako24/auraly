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
        await SqlAccountingPostingJobWriter.InsertAsync(
            session, document, payment.PaidAt, ids, timeProvider, cancellationToken);
        await InsertOutboxAsync(session, payment, document.Payload, cancellationToken);
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
