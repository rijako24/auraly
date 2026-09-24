using System.Data;
using Auraly.Application.Expenses;
using Auraly.Contracts.Expenses;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlExpenseStore
{
    public async Task<ExpenseCancellationAcceptance> CancelAsync(ExpenseUserIdentity user,
        Guid expenseId, CancelExpenseRequest request, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            await using var command = new SqlCommand("""
                DECLARE @LockedPayableId uniqueidentifier;
                SELECT @LockedPayableId=PayableId FROM dbo.Payables WITH(UPDLOCK,HOLDLOCK)
                WHERE SourceDocumentId=@ExpenseId AND SourceDocumentType=N'Expense'
                  AND BusinessId=@BusinessId;
                SELECT e.Status,e.CancellationId,e.CancellationReason,e.PurchaseEvidenceType,
                  source.PayloadJson,p.PayableId,p.OutstandingAmount,
                  fiscal.FiscalNumber,fiscal.UniqueCode,fiscal.IssuedAt,fiscal.FiscalStatus,
                  snapshot.SnapshotJson,job.AccountingPostingJobId,
                  CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.SalesReturnCharges returned WITH(UPDLOCK,HOLDLOCK)
                    WHERE returned.AppliedChargeId=e.ExpenseId) THEN 1 ELSE 0 END AS bit),
                  e.CancellationReasonOptionId
                FROM dbo.Expenses e WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.Businesses b ON b.BusinessId=e.BusinessId AND b.TenantId=@TenantId
                JOIN dbo.AccountingSourceDocuments source ON source.SourceDocumentId=e.ExpenseId
                  AND source.SourceDocumentType=N'Expense' AND source.BusinessId=e.BusinessId
                LEFT JOIN dbo.Payables p WITH(UPDLOCK,HOLDLOCK) ON p.SourceDocumentId=e.ExpenseId
                  AND p.SourceDocumentType=N'Expense' AND p.BusinessId=e.BusinessId
                LEFT JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=e.ExpenseId
                  AND fiscal.BusinessId=e.BusinessId AND fiscal.FiscalDocumentType=N'SupportDocument'
                LEFT JOIN fiscal.PurchaseSupportFiscalSnapshots snapshot ON snapshot.DocumentId=fiscal.DocumentId
                LEFT JOIN dbo.AccountingPostingJobs job ON job.SourceDocumentId=e.CancellationId
                  AND job.SourceDocumentType=N'ExpenseCancellation' AND job.BusinessId=e.BusinessId
                WHERE e.ExpenseId=@ExpenseId AND e.BusinessId=@BusinessId;
                """, connection, transaction);
            command.Parameters.AddWithValue("@TenantId", user.TenantId);
            command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            command.Parameters.AddWithValue("@ExpenseId", expenseId);
            string status, evidence; ExpenseDocumentPayload original;
            Guid? existingId, payableId, existingJobId, existingReasonOptionId;
            string? existingReason, supportNumber, supportCuds, fiscalStatus, supportJson;
            DateOnly? originalIssuedOn; decimal outstanding;
            bool returnedWithSale;
            await using (var reader = await command.ExecuteReaderAsync(ct))
            {
                if (!await reader.ReadAsync(ct))
                    throw new ExpenseValidationException("El gasto no existe en esta empresa.");
                status = reader.GetString(0);
                existingId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
                existingReason = reader.IsDBNull(2) ? null : reader.GetString(2);
                evidence = reader.GetString(3);
                original = ExpenseContractSerializer.Deserialize(reader.GetString(4));
                payableId = reader.IsDBNull(5) ? null : reader.GetGuid(5);
                outstanding = reader.IsDBNull(6) ? 0 : reader.GetDecimal(6);
                supportNumber = reader.IsDBNull(7) ? null : reader.GetString(7);
                supportCuds = reader.IsDBNull(8) ? null : reader.GetString(8);
                originalIssuedOn = reader.IsDBNull(9) ? null :
                    Auraly.Fiscal.Core.DianFiscalDateTime.DateInColombia(reader.GetDateTimeOffset(9));
                fiscalStatus = reader.IsDBNull(10) ? null : reader.GetString(10);
                supportJson = reader.IsDBNull(11) ? null : reader.GetString(11);
                existingJobId = reader.IsDBNull(12) ? null : reader.GetGuid(12);
                returnedWithSale = reader.GetBoolean(13);
                existingReasonOptionId = reader.IsDBNull(14) ? null : reader.GetGuid(14);
            }
            if (existingId is not null)
            {
                if (existingId != request.CancellationId ||
                    (request.ReasonOptionId is { } reasonOptionId
                        ? existingReasonOptionId != reasonOptionId
                        : existingReasonOptionId is not null || existingReason != request.Reason))
                    throw new ExpenseConflictException("El gasto ya tiene otra anulación.");
                if (existingJobId is null) throw new InvalidOperationException("La anulación no tiene trabajo contable.");
                await transaction.CommitAsync(ct);
                return new(expenseId, existingId.Value, existingJobId.Value,
                    evidence == PurchaseEvidenceTypes.BuyerElectronicSupportDocument, true);
            }
            if (status != "Processed")
                throw new ExpenseValidationException("Solo se puede anular un gasto procesado.");
            if (returnedWithSale)
                throw new ExpenseConflictException("El cargo de esta factura ya fue devuelto; su gasto no se puede anular otra vez.");
            var reason = request.Reason;
            if (request.ReasonOptionId is { } optionId)
            {
                await using var option = new SqlCommand("""
                    SELECT Label FROM [reference].[Options] WITH(HOLDLOCK)
                    WHERE OptionId=@OptionId AND CatalogCode=N'expense-cancellation-reason' AND IsActive=1;
                    """, connection, transaction);
                option.Parameters.AddWithValue("@OptionId", optionId);
                reason = (string?)await option.ExecuteScalarAsync(ct);
                if (reason is null)
                    throw new ExpenseValidationException("Selecciona un motivo de anulación activo.");
            }
            if (original.BusinessId != user.BusinessId || original.TenantId != user.TenantId || original.ExpenseId != expenseId)
                throw new InvalidOperationException("El origen contable del gasto no coincide con el tenant.");
            if (original.Withholding.NetAmount != outstanding && payableId is null && original.Withholding.NetAmount > 0)
                throw new InvalidOperationException("Falta la cuenta por pagar original del gasto.");
            if (outstanding < 0 || outstanding > original.Withholding.NetAmount)
                throw new InvalidOperationException("El saldo de la cuenta por pagar no concilia con el gasto.");
            await using (var pending = new SqlCommand("""
                SELECT (SELECT COUNT_BIG(*) FROM dbo.SupplierPaymentApplications a WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.SupplierPayments payment WITH(UPDLOCK,HOLDLOCK) ON payment.PaymentId=a.PaymentId
                WHERE a.PayableId=@PayableId AND a.AppliedAt IS NULL AND payment.Status=N'Accepted'),
                (SELECT COALESCE(SUM(CASE WHEN TransactionType=N'Payment' THEN Amount ELSE 0 END),0)
                  FROM dbo.PayableTransactions WITH(UPDLOCK,HOLDLOCK) WHERE PayableId=@PayableId),
                (SELECT COUNT_BIG(*) FROM dbo.PayableTransactions WITH(UPDLOCK,HOLDLOCK)
                  WHERE PayableId=@PayableId AND TransactionType NOT IN(N'Opening',N'Payment'));
                """, connection, transaction))
            {
                pending.Parameters.AddWithValue("@PayableId", (object?)payableId ?? DBNull.Value);
                await using var reader = await pending.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);
                if (reader.GetInt64(0) > 0)
                    throw new ExpenseConflictException("Hay un pago pendiente de procesar para este gasto.");
                if (reader.GetInt64(2) > 0 || reader.GetDecimal(1) != original.Withholding.NetAmount - outstanding)
                    throw new ExpenseConflictException(
                        "La cuenta por pagar tiene ajustes distintos de pagos; primero se debe conciliar antes de anular el gasto.");
            }
            var hasSupport = evidence == PurchaseEvidenceTypes.BuyerElectronicSupportDocument;
            PurchaseSupportFiscalSnapshot? originalSupport = null;
            if (hasSupport)
            {
                if (fiscalStatus != FiscalDocumentStatusCodes.DianAccepted ||
                    string.IsNullOrWhiteSpace(supportCuds) || string.IsNullOrWhiteSpace(supportNumber) ||
                    originalIssuedOn is null || supportJson is null)
                    throw new ExpenseValidationException("El documento soporte debe estar aceptado por la DIAN antes de anular el gasto.");
                originalSupport = PurchaseSupportFiscalSnapshotSerializer.Deserialize(supportJson);
            }
            var now = timeProvider.GetUtcNow();
            SqlGoodsReceiptStore.SupportFiscalAllocation? allocation = null;
            if (hasSupport)
            {
                if (!await SqlDianDocumentQuota.TryReserveAsync(connection, transaction, user.BusinessId,
                        request.CancellationId, FiscalDocumentTypeCodes.SupportDocument, now, ct))
                    throw new ExpenseValidationException("No hay cupo DIAN para la nota de ajuste del documento soporte.");
                try
                {
                    allocation = await SqlGoodsReceiptStore.AllocateSupportFiscalAsync(connection, transaction,
                        user.BusinessId, original.SupplierId, now, now, ct);
                }
                catch (Auraly.Application.Purchasing.PurchasingValidationException error)
                { throw new ExpenseValidationException(error.Message); }
            }
            var jobId = ids.NewId();
            var payload = new ExpenseCancellationPayload(user.TenantId, user.BusinessId,
                request.CancellationId, user.UserId, now, reason!, original, payableId,
                outstanding, original.Withholding.NetAmount - outstanding);
            await using (var update = new SqlCommand("""
                UPDATE dbo.Expenses SET Status=N'CancellationPending',CancellationId=@CancellationId,
                  CancellationReason=@Reason,CancellationReasonOptionId=@ReasonOptionId,CancellationAcceptedAt=@Now
                WHERE ExpenseId=@ExpenseId AND BusinessId=@BusinessId AND Status=N'Processed'
                  AND CancellationId IS NULL;
                """, connection, transaction))
            {
                update.Parameters.AddWithValue("@CancellationId", request.CancellationId);
                update.Parameters.AddWithValue("@Reason", reason!);
                update.Parameters.AddWithValue("@ReasonOptionId", (object?)request.ReasonOptionId ?? DBNull.Value);
                update.Parameters.AddWithValue("@Now", now);
                update.Parameters.AddWithValue("@ExpenseId", expenseId);
                update.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                if (await update.ExecuteNonQueryAsync(ct) != 1)
                    throw new ExpenseConflictException("El gasto cambió antes de anularlo.");
            }
            await SqlAccountingPostingJobWriter.InsertSourceAsync(connection, transaction,
                user.TenantId, user.BusinessId, request.CancellationId, ExpenseDocumentTypes.Cancellation,
                ExpenseCancellationSerializer.Serialize(payload), now, ids, timeProvider, ct,
                AccountingJobRequirement.PreserveCommercialEffects, jobId);
            if (allocation is not null && originalSupport is not null)
                await InsertExpenseAdjustmentAsync(connection, transaction, payload, originalSupport,
                    allocation, supportNumber!, supportCuds!, originalIssuedOn!.Value, now, ct);
            await transaction.CommitAsync(ct);
            return new(expenseId, request.CancellationId, jobId, hasSupport, false);
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new ExpenseConflictException("El identificador o consecutivo de la anulación ya está en uso.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private static async Task InsertExpenseAdjustmentAsync(SqlConnection connection, SqlTransaction transaction,
        ExpenseCancellationPayload payload, PurchaseSupportFiscalSnapshot original,
        SqlGoodsReceiptStore.SupportFiscalAllocation allocation, string originalNumber,
        string originalCuds, DateOnly originalIssuedOn, DateTimeOffset now, CancellationToken ct)
    {
        var snapshot = new PurchaseSupportFiscalSnapshot(null, allocation.IssuerConfigurationId,
            allocation.FiscalNumber, allocation.Environment, allocation.QrValidationUrl,
            original.Seller, allocation.Authorization, original.Lines,
            original.SellerOriginCode, OriginalSupportNumber: originalNumber,
            OriginalSupportCuds: originalCuds, OriginalSupportIssuedOn: originalIssuedOn,
            SellerPostalZone: original.SellerPostalZone, ExpenseCancellation: payload);
        await using var command = new SqlCommand("""
            INSERT dbo.FiscalDocuments(DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
              AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            VALUES(@Id,@BusinessId,N'ExpenseCancellation',N'SupportDocumentAdjustment',
              @Number,@FiscalNumber,N'CUDS',NULL,@Now,@Status,@Now,@Now);
            INSERT fiscal.PurchaseSupportFiscalSnapshots(DocumentId,SnapshotJson,Environment,CreatedAt)
            VALUES(@Id,@Snapshot,@Environment,@Now);
            INSERT dbo.FiscalDocumentProcesses(DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,
              AttemptCount,NextAttemptAt,CreatedAt,UpdatedAt)
            VALUES(@Id,@BusinessId,@IssuerId,@Status,0,@Now,@Now,@Now);
            """, connection, transaction);
        command.Parameters.AddWithValue("@Id", payload.CancellationId);
        command.Parameters.AddWithValue("@BusinessId", payload.BusinessId);
        var auralyNumber = payload.Original.DocumentNumber;
        command.Parameters.AddWithValue("@Number",
            (auralyNumber.Length > 60 ? auralyNumber[..60] : auralyNumber) + "-ANU");
        command.Parameters.AddWithValue("@FiscalNumber", allocation.FiscalNumber);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        command.Parameters.AddWithValue("@Snapshot", PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("@Environment", snapshot.Environment);
        command.Parameters.AddWithValue("@IssuerId", snapshot.FiscalIssuerConfigurationId);
        await command.ExecuteNonQueryAsync(ct);
    }
}
