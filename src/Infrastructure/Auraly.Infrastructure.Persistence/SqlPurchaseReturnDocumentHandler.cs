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
        await ApplyFinancialEffectsAsync(session,value,cancellationToken);
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

    private async Task ApplyFinancialEffectsAsync(
        SqlDocumentProcessingSessionAccessor.Session session,
        PurchaseReturnDocumentPayload value,CancellationToken cancellationToken)
    {
        Guid? payableId=null;decimal outstanding=0;
        await using(var load=new SqlCommand("""
            SELECT PayableId,OutstandingAmount
            FROM dbo.Payables WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND SourceDocumentId=@OriginalId
              AND SourceDocumentType=N'GoodsReceipt';
            """,session.Connection,session.Transaction))
        {
            load.Parameters.AddWithValue("@BusinessId",value.BusinessId);
            load.Parameters.AddWithValue("@OriginalId",value.OriginalGoodsReceiptId);
            await using var reader=await load.ExecuteReaderAsync(cancellationToken);
            if(await reader.ReadAsync(cancellationToken))
            {payableId=reader.GetGuid(0);outstanding=reader.GetDecimal(1);}
        }
        var payableCredit=Math.Min(value.TotalAmount,outstanding);
        var supplierCredit=value.TotalAmount-payableCredit;
        var now=timeProvider.GetUtcNow();
        if(payableId is not null && payableCredit>0)
        {
            var remaining=outstanding-payableCredit;
            await using var command=new SqlCommand("""
                UPDATE dbo.Payables
                SET OutstandingAmount=@Remaining,
                    Status=CASE WHEN @Remaining=0 THEN N'Paid' ELSE N'PartiallyPaid' END
                WHERE PayableId=@PayableId;
                INSERT dbo.PayableTransactions
                  (PayableTransactionId,PayableId,TransactionType,Amount,
                   SourceDocumentId,OccurredAt,CreatedAt)
                VALUES(@TransactionId,@PayableId,N'Credit',@Amount,@ReturnId,@OccurredAt,@Now);
                """,session.Connection,session.Transaction);
            command.Parameters.AddWithValue("@Remaining",remaining);
            command.Parameters.AddWithValue("@PayableId",payableId.Value);
            command.Parameters.AddWithValue("@TransactionId",ids.NewId());
            AddMoney(command,"@Amount",payableCredit);
            command.Parameters.AddWithValue("@ReturnId",value.ReturnId);
            command.Parameters.AddWithValue("@OccurredAt",value.ReturnedAt);
            command.Parameters.AddWithValue("@Now",now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        if(supplierCredit>0)
        {
            await using var command=new SqlCommand("""
                INSERT dbo.SupplierCredits
                  (SupplierCreditId,BusinessId,SupplierId,SourcePurchaseReturnId,
                   OriginalAmount,AvailableAmount,Status,CreatedAt)
                VALUES(@Id,@BusinessId,@SupplierId,@ReturnId,@Amount,@Amount,N'Open',@Now);
                """,session.Connection,session.Transaction);
            command.Parameters.AddWithValue("@Id",ids.NewId());
            command.Parameters.AddWithValue("@BusinessId",value.BusinessId);
            command.Parameters.AddWithValue("@SupplierId",value.SupplierId);
            command.Parameters.AddWithValue("@ReturnId",value.ReturnId);
            AddMoney(command,"@Amount",supplierCredit);
            command.Parameters.AddWithValue("@Now",now);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await using var effect=new SqlCommand("""
            INSERT dbo.PurchaseReturnFinancialEffects
              (PurchaseReturnId,PayableId,PayableCreditAmount,SupplierCreditAmount,CreatedAt)
            VALUES(@ReturnId,@PayableId,@PayableCredit,@SupplierCredit,@Now);
            """,session.Connection,session.Transaction);
        effect.Parameters.AddWithValue("@ReturnId",value.ReturnId);
        effect.Parameters.AddWithValue("@PayableId",(object?)payableId??DBNull.Value);
        AddMoney(effect,"@PayableCredit",payableCredit);
        AddMoney(effect,"@SupplierCredit",supplierCredit);
        effect.Parameters.AddWithValue("@Now",now);
        await effect.ExecuteNonQueryAsync(cancellationToken);
    }

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
    private static void AddMoney(SqlCommand command,string name,decimal value)=>
        AddDecimal(command,name,value,19,4);
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale)
    {
        var parameter=command.Parameters.Add(name,SqlDbType.Decimal);
        parameter.Precision=precision;parameter.Scale=scale;parameter.Value=value;
    }
}