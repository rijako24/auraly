using System.Data;
using System.Text.Json;
using Auraly.Application.Returns;
using Auraly.Contracts.Returns;
using Auraly.Domain.Returns;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlSalesReturnStore
{
    private static async Task<RefundSettlementContext> ValidateDispatchCardRefundAsync(SqlConnection connection,
        SqlTransaction transaction,SalesReturnUserIdentity user,ConfirmSalesReturnRequest request,decimal amount,
        CancellationToken token)
    {
        await using var command=new SqlCommand("""
            SELECT payment.MethodCode,payment.Amount-COALESCE(prior.Amount,0),payment.CardFranchiseCode,payment.ApprovalNumber
            FROM dbo.DispatchSourceDocuments source
            JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=source.DispatchId AND dispatch.TenantId=@Tenant AND dispatch.BusinessId=@Business
            JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId AND sale.BusinessId=@Business AND sale.CollectionMode=N'Dispatch'
            CROSS APPLY dispatch.EffectivePayments(source.DispatchId) payment
            JOIN dispatch.Closures closure ON closure.DispatchId=source.DispatchId
            JOIN dbo.AccountingPostingJobs posting ON posting.SourceDocumentId=closure.DispatchSettlementId
              AND posting.SourceDocumentType=N'DispatchCashDifference' AND posting.BusinessId=@Business AND posting.TenantId=@Tenant
              AND posting.Status IN(N'Posted',N'CommercialEffectsApplied')
            OUTER APPLY(SELECT SUM(returned.TotalAmount) Amount FROM dbo.SalesReturns returned WITH(UPDLOCK,HOLDLOCK)
              WHERE returned.OriginalDispatchPaymentId=payment.PaymentId AND returned.EconomicResolution=N'Refund') prior
            WHERE source.SourceDocumentId=@Original AND payment.PaymentId=@Payment
              AND payment.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND payment.ApplicationType=N'InvoicePayment';
            """,connection,transaction);
        command.Parameters.AddWithValue("@Tenant",user.TenantId);
        command.Parameters.AddWithValue("@Business",user.BusinessId);
        command.Parameters.AddWithValue("@Original",request.OriginalDocumentId);
        command.Parameters.AddWithValue("@Payment",request.OriginalDispatchPaymentId!.Value);
        await using var reader=await command.ExecuteReaderAsync(token);
        if(!await reader.ReadAsync(token) || reader.GetString(0)!=request.RefundMethodCode)
            throw new SalesReturnValidationException("Selecciona la transacción de tarjeta realmente recaudada y conciliada del despacho.");
        if(reader.GetDecimal(1)<amount)
            throw new SalesReturnConflictException("El valor supera el saldo disponible de la transacción de tarjeta recaudada.");
        return new(null,null,reader.IsDBNull(2)?null:reader.GetString(2),reader.IsDBNull(3)?null:reader.GetString(3));
    }

    private static async Task<decimal> ValidateDispatchReturnAsync(SqlConnection connection,
        SqlTransaction transaction, SalesReturnUserIdentity user, ConfirmSalesReturnRequest request,
        CancellationToken token)
    {
        // Same aggregate lock order as delivery, close and reconciliation. A
        // public return cannot claim that a collected sale was never received.
        await using var command = new SqlCommand("""
            SELECT CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN source.DocumentTotalSnapshot ELSE delivery.ReturnTotal END
            FROM dbo.Dispatches dispatch WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.DispatchSettlementOperations operation ON operation.DispatchId=dispatch.DispatchId
              AND operation.Status=N'Processing' AND operation.RequestedBy=@User
            JOIN dbo.DispatchSourceDocuments source ON source.DispatchId=dispatch.DispatchId
              AND source.SourceDocumentId=@Original
            JOIN dbo.DispatchDeliveryEvents delivery ON delivery.DispatchSourceDocumentId=source.DispatchSourceDocumentId
              AND delivery.IsCurrent=1
            JOIN dbo.SalesDocuments sale ON sale.DocumentId=source.SourceDocumentId
              AND sale.BusinessId=dispatch.BusinessId
            WHERE dispatch.DispatchId=@Dispatch AND dispatch.TenantId=@Tenant AND dispatch.BusinessId=@Business
              AND dispatch.Status=N'SettlementProcessing'
              AND ((@CreditOnly=0 AND sale.CollectionMode=N'Dispatch') OR (@CreditOnly=1 AND source.CreditAmountSnapshot>=
                CASE WHEN delivery.DeliveryStatus=N'NotDelivered' THEN source.DocumentTotalSnapshot ELSE delivery.ReturnTotal END))
              AND (delivery.DeliveryStatus=N'NotDelivered' OR EXISTS(
                SELECT 1 FROM dbo.DispatchDeliveryReturns returned WHERE returned.DispatchSourceDocumentId=source.DispatchSourceDocumentId))
              AND NOT EXISTS(
                SELECT expected.LineNumber,expected.Quantity,expected.Disposition
                FROM(
                  SELECT line.SourceLineNumber LineNumber,line.AssignedQuantity Quantity,N'Sellable' Disposition FROM dbo.DispatchLines line
                    WHERE line.DispatchSourceDocumentId=source.DispatchSourceDocumentId AND delivery.DeliveryStatus=N'NotDelivered'
                  UNION ALL
                  SELECT returned.OriginalLineNumber,returned.Quantity,returned.InventoryDisposition
                    FROM dbo.DispatchDeliveryReturns returned WHERE returned.DispatchSourceDocumentId=source.DispatchSourceDocumentId
                      AND delivery.DeliveryStatus<>N'NotDelivered'
                ) expected
                FULL JOIN OPENJSON(@Lines) WITH(OriginalLineNumber int,Quantity decimal(19,6),InventoryDisposition nvarchar(24)) requested
                  ON requested.OriginalLineNumber=expected.LineNumber
                WHERE expected.LineNumber IS NULL OR requested.OriginalLineNumber IS NULL
                  OR expected.Quantity<>requested.Quantity OR expected.Disposition<>requested.InventoryDisposition)
              AND (delivery.DeliveryStatus=N'NotDelivered' OR NOT EXISTS(SELECT 1 FROM OPENJSON(@Charges)))
              AND NOT EXISTS(SELECT 1 FROM dispatch.Closures closure JOIN dbo.AccountingPostingJobs posting
                ON posting.SourceDocumentId=closure.DispatchSettlementId AND posting.SourceDocumentType=N'DispatchCashDifference'
                WHERE closure.DispatchId=dispatch.DispatchId AND posting.Status IN(N'Posted',N'CommercialEffectsApplied'));
            """, connection, transaction);
        command.Parameters.AddWithValue("@Dispatch", request.DispatchId!.Value);
        command.Parameters.AddWithValue("@Original", request.OriginalDocumentId);
        command.Parameters.AddWithValue("@Tenant", user.TenantId);
        command.Parameters.AddWithValue("@Business", user.BusinessId);
        command.Parameters.AddWithValue("@User", user.UserId);
        command.Parameters.AddWithValue("@CreditOnly",request.EconomicResolution==ReturnEconomicResolutions.CustomerCredit);
        command.Parameters.Add("@Lines",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.Lines);
        command.Parameters.Add("@Charges",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(request.ReturnedChargeIds??[]);
        if (await command.ExecuteScalarAsync(token) is not decimal expected)
            throw new SalesReturnConflictException("La venta no conserva una devolución de entrega pendiente de conciliación en este despacho.");
        return expected;
    }

    private static async Task<IReadOnlyList<UncollectedPaymentReversal>> AllocateUncollectedReversalAsync(
        SqlConnection connection, SqlTransaction transaction, ConfirmSalesReturnRequest request,
        decimal total, CancellationToken token)
    {
        // Acceptance reserves the immutable original tender budget, including
        // returns accepted but not posted yet. The original sale lock serializes
        // concurrent returns; no financial/stock effect is written here.
        await using var command = new SqlCommand("""
            SELECT payment.PaymentNumber,payment.Amount+payment.RoundingAdjustment-COALESCE(prior.Amount,0)
            FROM dbo.SalesPayments payment WITH(UPDLOCK,HOLDLOCK)
            OUTER APPLY(
              SELECT SUM(reversal.Amount) Amount
              FROM dbo.SalesReturns returned
              JOIN dbo.DocumentProcessingPayloads payload ON payload.DocumentId=returned.ReturnId AND payload.DocumentType=N'SalesReturn'
              CROSS APPLY OPENJSON(payload.PayloadJson,'$.uncollectedPaymentReversals')
                WITH(OriginalPaymentNumber int '$.originalPaymentNumber',Amount decimal(19,4) '$.amount') reversal
              WHERE returned.OriginalDocumentId=payment.DocumentId
                AND returned.EconomicResolution=N'ReverseUncollected' AND reversal.OriginalPaymentNumber=payment.PaymentNumber
            ) prior
            WHERE payment.DocumentId=@Original ORDER BY payment.PaymentNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Original", request.OriginalDocumentId);
        var available = new Dictionary<int, decimal>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) available.Add(reader.GetInt32(0), reader.GetDecimal(1));
        try
        {
            return SalesReturnAmountCalculator.AllocateUncollectedReversal(available, total)
                .Select(item => new UncollectedPaymentReversal(item.Key, item.Value)).ToArray();
        }
        catch (ArgumentException error) { throw new SalesReturnConflictException(error.Message); }
    }
}
