using System.Data;
using Auraly.Application.DocumentProcessing;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.DocumentProcessing;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlPurchaseReturnDocumentHandler(
    SqlDocumentProcessingSessionAccessor sessions,
    IAuralyIdGenerator ids,
    SqlInventoryLedgerWriter inventoryWriter,
    TimeProvider timeProvider) : IConfirmedDocumentHandler
{
    public string DocumentType => PurchasingDocumentTypes.PurchaseReturn;

    public async Task HandleAsync(ConfirmedDocument document,CancellationToken cancellationToken)
    {
        var value=PurchaseReturnContractSerializer.Deserialize(document.Payload);
        if(value.ReturnId!=document.DocumentId.Value || value.BusinessId!=document.BusinessId.Value ||
           value.TenantId!=document.TenantId.Value)
            throw new InvalidOperationException(
                "The purchase return envelope does not match its payload.");
        var session=sessions.Current;
        foreach(var line in value.Lines.OrderBy(line=>line.LineNumber))
            await ApplyInventoryAsync(session,value,line,cancellationToken);
        await SqlAccountingPostingJobWriter.InsertAsync(
            session,document,value.ReturnedAt,ids,timeProvider,cancellationToken);
        await InsertOutboxAsync(session,value,document.Payload,cancellationToken);
        await MarkProcessedAsync(session,value,cancellationToken);
    }

    private Task ApplyInventoryAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PurchaseReturnDocumentPayload value,
        PurchaseReturnLineSnapshot line,
        CancellationToken cancellationToken) =>
        inventoryWriter.PostAsync(
            session,
            new InventoryLedgerPosting(
                value.BusinessId,
                value.WarehouseId,
                line.ProductId,
                value.ReturnId,
                PurchasingDocumentTypes.PurchaseReturn,
                line.LineNumber,
                "PurchaseReturn",
                -line.Quantity,
                line.RecognizedUnitCost,
                InventoryValuationModes.SpecifiedCostIssue,
                value.ReturnedAt),
            cancellationToken);

    private async Task InsertOutboxAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PurchaseReturnDocumentPayload value,string payload,
        CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            INSERT dbo.ServerOutboxMessages
              (MessageId,DocumentId,DocumentType,Type,Payload,OccurredAt)
            VALUES(@Id,@DocumentId,N'PurchaseReturn',N'purchasing.purchase-return.processed',
                   @Payload,@Now);
            """,session.Connection,session.Transaction);
        command.Parameters.AddWithValue("@Id",ids.NewId());
        command.Parameters.AddWithValue("@DocumentId",value.ReturnId);
        command.Parameters.AddWithValue("@Payload",payload);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task MarkProcessedAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PurchaseReturnDocumentPayload value,CancellationToken cancellationToken)
    {
        await using var command=new SqlCommand("""
            UPDATE dbo.PurchaseReturns SET Status=N'Processed',ProcessedAt=@Now
            WHERE PurchaseReturnId=@Id AND BusinessId=@BusinessId AND Status=N'Accepted';
            """,session.Connection,session.Transaction);
        command.Parameters.AddWithValue("@Id",value.ReturnId);
        command.Parameters.AddWithValue("@BusinessId",value.BusinessId);
        command.Parameters.AddWithValue("@Now",timeProvider.GetUtcNow());
        if(await command.ExecuteNonQueryAsync(cancellationToken)!=1)
            throw new DBConcurrencyException(
                "The purchase return could not be marked as processed.");
    }
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale)
    {
        var parameter=command.Parameters.Add(name,SqlDbType.Decimal);
        parameter.Precision=precision;parameter.Scale=scale;parameter.Value=value;
    }
}
