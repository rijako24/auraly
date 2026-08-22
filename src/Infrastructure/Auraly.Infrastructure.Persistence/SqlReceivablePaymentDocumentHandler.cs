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
        await SqlAccountingPostingJobWriter.InsertAsync(session,document,payment.PaidAt,ids,timeProvider,token);
        await using var outbox=new SqlCommand("INSERT dbo.ServerOutboxMessages(MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt) VALUES(@Id,@DocumentId,N'ReceivablePayment',N'receivables.customer-payment.processed',@Payload,@Now)",session.Connection,session.Transaction);
        outbox.Parameters.AddWithValue("@Id",ids.NewId());outbox.Parameters.AddWithValue("@DocumentId",payment.PaymentId);outbox.Parameters.AddWithValue("@Payload",document.Payload);outbox.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());await outbox.ExecuteNonQueryAsync(token);
    }

}
