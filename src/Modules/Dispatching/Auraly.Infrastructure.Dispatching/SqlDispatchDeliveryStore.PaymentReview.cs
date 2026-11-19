using System.Data;
using Auraly.Contracts.Dispatching;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Dispatching;

public sealed partial class SqlDispatchDeliveryStore
{
    private const string PaymentColumns = "PaymentId,ApplicationType,MethodCode,Amount,Reference,EvidenceUrl,PaymentMethodId,PaymentMethodVersion,FormName,CardFranchiseCode,ApprovalNumber,VerifiedBy,VerifiedAt,OriginalMethod,OriginalFormName,CorrectionReason,CorrectedBy,CorrectedAt";

    public Task<DispatchPaymentReviewResult> VerifyPaymentAsync(DispatchActorIdentity actor, Guid dispatchId,
        Guid paymentId, VerifyDispatchPaymentRequest request, CancellationToken ct) =>
        ReviewPaymentAsync(actor, dispatchId, paymentId, request.IsVerified, null, request.ExpectedCorrectedAt, ct);

    public Task<DispatchPaymentReviewResult> CorrectPaymentAsync(DispatchActorIdentity actor, Guid dispatchId,
        Guid paymentId, CorrectDispatchPaymentRequest request, CancellationToken ct) =>
        ReviewPaymentAsync(actor, dispatchId, paymentId, null, request, request.ExpectedCorrectedAt, ct);

    private async Task<DispatchPaymentReviewResult> ReviewPaymentAsync(DispatchActorIdentity actor, Guid dispatchId,
        Guid paymentId, bool? verified, CorrectDispatchPaymentRequest? correction, DateTimeOffset? expectedCorrectedAt, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SET XACT_ABORT ON;
            BEGIN TRAN;
            BEGIN TRY
              IF NOT EXISTS(SELECT 1 FROM dbo.Dispatches WITH(UPDLOCK,HOLDLOCK)
                WHERE DispatchId=@Id AND BusinessId=@BusinessId AND TenantId=@TenantId)
                THROW 51000,N'El despacho no pertenece a esta sede.',1;
              IF NOT EXISTS(SELECT 1 FROM dispatch.Closures settlement WITH(UPDLOCK,HOLDLOCK)
                INNER JOIN dbo.Dispatches dispatch ON dispatch.DispatchId=settlement.DispatchId AND dispatch.BusinessId=settlement.BusinessId
                WHERE settlement.DispatchId=@Id AND settlement.BusinessId=@BusinessId AND dispatch.TenantId=@TenantId
                  AND settlement.Status=N'PendingReview')
                THROW 51000,N'El despacho no está pendiente de revisión.',1;
              IF NOT EXISTS(SELECT 1 FROM dbo.DispatchDeliveryPayments WITH(UPDLOCK,HOLDLOCK)
                WHERE DispatchDeliveryPaymentId=@PaymentId AND DispatchId=@Id AND BusinessId=@BusinessId
                  AND ApplicationType IN(N'InvoicePayment',N'CreditAdvance'))
                THROW 51000,N'El pago no pertenece a este despacho.',1;
              IF @FormId IS NOT NULL
              BEGIN
                IF NOT EXISTS(SELECT 1 FROM reference.PaymentMethodDetails WITH(HOLDLOCK)
                  WHERE TenantId=@TenantId AND PaymentMethodId=@FormId AND Version=@Version
                    AND MethodCode=@Method AND IsActive=1 AND IsReady=1 AND UseForReceipts=1)
                  THROW 51000,N'La forma de pago cambió o no está habilitada para recaudos. Vuelve a seleccionarla.',1;
                IF @Method IN(N'DebitCard',N'CreditCard') AND NOT EXISTS(
                  SELECT 1 FROM reference.Options WHERE CatalogCode=N'card-franchise' AND Code=@Franchise AND IsActive=1)
                  THROW 51000,N'La franquicia no está habilitada.',1;
                IF EXISTS(SELECT @FormId,@Version,CASE WHEN @Method=N'Transfer' THEN @Reference END,
                    CASE WHEN @Method IN(N'DebitCard',N'CreditCard') THEN @Franchise END,
                    CASE WHEN @Method IN(N'DebitCard',N'CreditCard') THEN @Approval END,@Reason
                  EXCEPT SELECT ReviewedPaymentMethodId,ReviewedPaymentMethodVersion,ReviewedReference,
                    ReviewedCardFranchiseCode,ReviewedApprovalNumber,CorrectionReason
                  FROM dbo.DispatchDeliveryPayments WHERE DispatchDeliveryPaymentId=@PaymentId)
                BEGIN
                  IF EXISTS(SELECT CorrectedAt FROM dbo.DispatchDeliveryPayments WHERE DispatchDeliveryPaymentId=@PaymentId
                    EXCEPT SELECT @ExpectedCorrectedAt)
                    THROW 51000,N'El pago fue corregido por otro usuario. Actualiza el despacho antes de continuar.',1;
                  UPDATE dbo.DispatchDeliveryPayments SET ReviewedPaymentMethodId=@FormId,ReviewedPaymentMethodVersion=@Version,
                  ReviewedReference=CASE WHEN @Method=N'Transfer' THEN @Reference END,
                  ReviewedCardFranchiseCode=CASE WHEN @Method IN(N'DebitCard',N'CreditCard') THEN @Franchise END,
                  ReviewedApprovalNumber=CASE WHEN @Method IN(N'DebitCard',N'CreditCard') THEN @Approval END,
                  CorrectionReason=@Reason,CorrectedBy=@UserId,CorrectedAt=SYSUTCDATETIME(),VerifiedBy=NULL,VerifiedAt=NULL
                  WHERE DispatchDeliveryPaymentId=@PaymentId;
                END;
              END
              ELSE
              BEGIN
                IF EXISTS(SELECT 1 FROM dispatch.EffectivePayments(@Id) WHERE PaymentId=@PaymentId AND MethodCode=N'Cash')
                  THROW 51000,N'El efectivo se valida por el valor recibido, sin chequear cada factura.',1;
                IF EXISTS(SELECT CorrectedAt FROM dbo.DispatchDeliveryPayments WHERE DispatchDeliveryPaymentId=@PaymentId
                  EXCEPT SELECT @ExpectedCorrectedAt)
                  THROW 51000,N'La forma de pago cambió. Revisa el nuevo soporte antes de chequearlo.',1;
                UPDATE dbo.DispatchDeliveryPayments SET
                  VerifiedBy=CASE WHEN @Verified=1 THEN COALESCE(VerifiedBy,@UserId) END,
                  VerifiedAt=CASE WHEN @Verified=1 THEN COALESCE(VerifiedAt,SYSUTCDATETIME()) END
                WHERE DispatchDeliveryPaymentId=@PaymentId;
              END;
              DECLARE @Gross decimal(19,4),@Transfer decimal(19,4),@Cards decimal(19,4);
              SELECT @Gross=GrossCash,@Transfer=TransferTotal,@Cards=CardTotal FROM dispatch.PaymentTotals(@Id);
              DECLARE @Expenses decimal(19,4)=(SELECT COALESCE(SUM(ApprovedAmount),0) FROM dbo.DispatchExpenses
                WHERE DispatchId=@Id AND ApprovalStatus=N'Approved');
              IF @Expenses>@Gross THROW 51000,N'Los gastos aprobados superan el efectivo recaudado. Revisa los gastos antes de corregir la forma.',1;
              UPDATE dispatch.Closures SET ExpectedCash=@Gross-@Expenses,DepositTotal=@Transfer WHERE DispatchId=@Id;
            """ + $"\nSELECT DispatchSourceDocumentId,{PaymentColumns} FROM dispatch.EffectivePayments(@Id) WHERE PaymentId=@PaymentId;\n" + """
              SELECT @Gross,ExpectedCash,CashDifference,@Transfer,@Cards FROM dispatch.Closures WHERE DispatchId=@Id;
              COMMIT;
            END TRY
            BEGIN CATCH
              IF @@TRANCOUNT>0 ROLLBACK;
              THROW;
            END CATCH
            """, connection);
        Scope(command, actor, dispatchId);
        command.Parameters.AddWithValue("@PaymentId", paymentId);
        command.Parameters.Add("@Verified", SqlDbType.Bit).Value = (object?)verified ?? DBNull.Value;
        command.Parameters.Add("@FormId", SqlDbType.UniqueIdentifier).Value = (object?)correction?.PaymentMethodId ?? DBNull.Value;
        command.Parameters.Add("@Version", SqlDbType.Int).Value = (object?)correction?.PaymentMethodVersion ?? DBNull.Value;
        command.Parameters.Add("@Method", SqlDbType.NVarChar, 24).Value = (object?)correction?.MethodCode ?? DBNull.Value;
        command.Parameters.Add("@Reference", SqlDbType.NVarChar, 120).Value = (object?)correction?.Reference ?? DBNull.Value;
        command.Parameters.Add("@Franchise", SqlDbType.NVarChar, 40).Value = (object?)correction?.CardFranchiseCode ?? DBNull.Value;
        command.Parameters.Add("@Approval", SqlDbType.NVarChar, 80).Value = (object?)correction?.ApprovalNumber ?? DBNull.Value;
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = (object?)correction?.Reason ?? DBNull.Value;
        command.Parameters.Add("@ExpectedCorrectedAt", SqlDbType.DateTimeOffset).Value = (object?)expectedCorrectedAt ?? DBNull.Value;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            var source = reader.GetGuid(0); var payment = ReadPayment(reader, 1);
            await reader.NextResultAsync(ct); await reader.ReadAsync(ct);
            var result = new DispatchPaymentReviewResult(dispatchId, source, payment, reader.GetDecimal(0), reader.GetDecimal(1),
                reader.GetDecimal(2), reader.GetDecimal(3), reader.GetDecimal(4));
            // Consume COMMIT before exposing the authoritative result.
            while (await reader.NextResultAsync(ct)) { }
            return result;
        }
        catch (SqlException ex) when (ex.Number == 51000) { throw new DispatchConflictException(ex.Message); }
    }

    private static DispatchDeliveryPaymentDetail ReadPayment(SqlDataReader reader, int offset) => new(
        reader.GetGuid(offset), reader.GetString(offset+1), NullableString(reader,offset+2), reader.GetDecimal(offset+3),
        NullableString(reader,offset+4), NullableString(reader,offset+5), NullableGuid(reader,offset+6),
        reader.IsDBNull(offset+7) ? null : reader.GetInt32(offset+7), NullableString(reader,offset+8),
        NullableString(reader,offset+9), NullableString(reader,offset+10), NullableGuid(reader,offset+11),
        reader.IsDBNull(offset+12) ? null : reader.GetDateTimeOffset(offset+12), NullableString(reader,offset+13),
        NullableString(reader,offset+14), NullableString(reader,offset+15), NullableGuid(reader,offset+16),
        reader.IsDBNull(offset+17) ? null : reader.GetDateTimeOffset(offset+17));
}
