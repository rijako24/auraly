using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.WorkSessions;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Contracts.WorkSessions;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlWorkSessionStore
{
    public async Task<SalePaymentCorrectionContext> GetSalePaymentCorrectionAsync(
        WorkSessionIdentity identity, Guid documentId, CancellationToken token)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(token);
        var scope = await ReadSaleCorrectionScopeAsync(connection, transaction, identity, documentId, token);
        if (!scope.Pending) throw new WorkSessionValidationException("El cierre ya fue conciliado; su evidencia aceptada se conserva.");
        await using var command = new SqlCommand("""
            SELECT CONCAT(N'Sale:',CONVERT(nvarchar(36),sale.DocumentId),N':',payment.PaymentNumber),
              COALESCE(mapping.ClosureMethodCode,payment.MethodCode),N'Sale',payment.PaymentNumber,
              payment.Amount+payment.RoundingAdjustment,payment.Reference,payment.CardFranchiseCode,
              payment.ApprovalNumber,payment.MethodCode,CONVERT(decimal(19,4),NULL),direct.SourceDocumentId,direct.PostingStatus,
              direct.TenderMethodCode,direct.Amount,direct.PaymentMethodName
            FROM dbo.SalesDocuments sale
            JOIN dbo.SalesPayments payment ON payment.DocumentId=sale.DocumentId
            LEFT JOIN worksessions.CashClosurePaymentMethodMappings mapping ON mapping.PaymentMethodCode=payment.MethodCode
            LEFT JOIN accounting.SalePaymentCorrections direct ON direct.TenantId=@Tenant AND direct.BusinessId=@Business
              AND direct.DocumentId=sale.DocumentId
              AND direct.VerificationKey=CONCAT(N'Sale:',CONVERT(nvarchar(36),sale.DocumentId),N':',payment.PaymentNumber)
            WHERE sale.DocumentId=@Document AND sale.BusinessId=@Business
            UNION ALL
            SELECT CONCAT(N'CreditSale:',CONVERT(nvarchar(36),sale.DocumentId)),N'Credit',N'CreditSale',0,
              sale.CreditAmount,NULL,NULL,NULL,N'Credit',available.AvailableAmount,direct.SourceDocumentId,direct.PostingStatus,
              direct.TenderMethodCode,direct.Amount,direct.PaymentMethodName
            FROM dbo.SalesDocuments sale
            LEFT JOIN dbo.Receivables debt ON debt.SourceDocumentId=sale.DocumentId
              AND debt.SourceDocumentType=sale.DocumentType AND debt.BusinessId=sale.BusinessId AND debt.SourceLineId IS NULL
            OUTER APPLY accounting.ObligationAvailability(@Business,N'Receivable',debt.ReceivableId,debt.OutstandingAmount,NULL) available
            LEFT JOIN accounting.SalePaymentCorrections direct ON direct.TenantId=@Tenant AND direct.BusinessId=@Business
              AND direct.DocumentId=sale.DocumentId
              AND direct.VerificationKey=CONCAT(N'CreditSale:',CONVERT(nvarchar(36),sale.DocumentId))
            WHERE sale.DocumentId=@Document AND sale.BusinessId=@Business AND sale.CreditAmount>0;
            SELECT total.PaymentMethodCode FROM dbo.WorkSessionClosurePaymentTotals total
            JOIN reference.Options optionValue ON optionValue.CatalogCode=N'cash-closure-method'
              AND optionValue.Code=total.PaymentMethodCode AND optionValue.IsActive=1
            WHERE total.WorkSessionClosureId=@Closure;
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", identity.TenantId);
        command.Parameters.AddWithValue("@Business", scope.BusinessId);
        command.Parameters.AddWithValue("@Document", documentId);
        command.Parameters.AddWithValue("@Closure", scope.ClosureId);
        var items = new List<WorkSessionPaymentVerificationItem>();
        var methods = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
                items.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), documentId,
                    scope.Number, reader.GetInt32(3), reader.GetDecimal(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7), scope.IssuedAt, "SalesReceipt", scope.Customer, null,
                    TenderMethodCode: reader.GetString(8), CollectibleAmount: reader.IsDBNull(9) ? null : reader.GetDecimal(9),
                    AppliedCorrectionId: reader.IsDBNull(10) ? null : reader.GetGuid(10),
                    CorrectionAccountingStatus: reader.IsDBNull(11) ? null : reader.GetString(11),
                    CorrectedTenderMethodCode: reader.IsDBNull(12) ? null : reader.GetString(12),
                    CorrectedAmount: reader.IsDBNull(13) ? null : reader.GetDecimal(13),
                    CorrectedPaymentMethodName: reader.IsDBNull(14) ? null : reader.GetString(14)));
            await reader.NextResultAsync(token);
            while (await reader.ReadAsync(token)) methods.Add(reader.GetString(0));
        }
        await transaction.CommitAsync(token);
        return new(documentId, scope.Number, scope.ClosureId, items, methods);
    }

    public async Task<SalePaymentCorrectionAcceptance> CorrectSalePaymentAsync(WorkSessionIdentity identity,
        Guid documentId, CorrectSalePaymentRequest request, CancellationToken token)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { documentId, identity.UserId, request }, Json))));
        await using var connection = connections.Create();
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, token);
        try
        {
            var scope = await ReadSaleCorrectionScopeAsync(connection, transaction, identity, documentId, token);
            await using (var replay = new SqlCommand("""
                DECLARE @Payload nvarchar(max),@Status nvarchar(32);
                SELECT @Payload=source.PayloadJson,@Status=job.Status FROM dbo.AccountingSourceDocuments source
                JOIN dbo.AccountingPostingJobs job ON job.AccountingPostingJobId=source.AccountingPostingJobId
                  AND job.SourceDocumentId=source.SourceDocumentId AND job.SourceDocumentType=source.SourceDocumentType
                  AND job.TenantId=source.TenantId AND job.BusinessId=source.BusinessId
                WHERE source.SourceDocumentId=@Id AND source.SourceDocumentType=N'WorkSessionClosureReconciliation'
                  AND source.CorrectsSaleDocumentId=@Document AND source.TenantId=@Tenant AND source.BusinessId=@Business;
                IF @Payload IS NULL AND EXISTS(SELECT 1 FROM dbo.AccountingSourceDocuments
                  WHERE TenantId=@Tenant AND BusinessId=@Business AND CorrectsSaleDocumentId=@Document
                    AND PaymentCorrectionVerificationKey=@Key)
                  THROW 51871,N'Este recaudo ya tiene una corrección aceptada. Se conserva su resultado y evidencia.',1;
                -- Do not wait for a receivable lock while retaining a read of
                -- another correction's job. Its processor must finish first.
                IF @Payload IS NULL AND EXISTS(SELECT 1 FROM accounting.SalePaymentCorrections
                  WHERE TenantId=@Tenant AND BusinessId=@Business AND WorkSessionClosureId=@Closure
                    AND PostingStatus NOT IN(N'Posted',N'CommercialEffectsApplied'))
                  THROW 51871,N'Hay una corrección de este cierre pendiente de aplicar. Revisa su estado antes de continuar.',1;
                SELECT @Payload,@Status WHERE @Payload IS NOT NULL;
                """, connection, transaction))
            {
                replay.Parameters.AddWithValue("@Id", request.CorrectionId);
                replay.Parameters.AddWithValue("@Document", documentId);
                replay.Parameters.AddWithValue("@Tenant", identity.TenantId);
                replay.Parameters.AddWithValue("@Business", scope.BusinessId);
                replay.Parameters.AddWithValue("@Closure", scope.ClosureId);
                replay.Parameters.AddWithValue("@Key", request.Correction.VerificationKey.Trim());
                await using var reader = await replay.ExecuteReaderAsync(token);
                if (await reader.ReadAsync(token))
                {
                    var accepted = JsonSerializer.Deserialize<WorkSessionClosureReconciliationPayload>(reader.GetString(0), Json)!;
                    if (accepted.DirectRequestHash != hash || accepted.ReconciledByUserId != identity.UserId)
                        throw new WorkSessionConflictException("La corrección ya fue recibida con otros datos.");
                    var status = reader.GetString(1);
                    await reader.CloseAsync();
                    await transaction.CommitAsync(token);
                    return new(request.CorrectionId, documentId, scope.BusinessId, status, true);
                }
            }
            if (!scope.Pending) throw new WorkSessionConflictException("El cierre ya fue conciliado; su evidencia aceptada se conserva.");
            var verification = await ValidatePaymentVerificationsAsync(connection, transaction, scope.ClosureId,
                scope.BusinessId, [new(request.Correction.VerificationKey, "Verified")],
                [request.Correction], true, token, lockPaymentTypes: true);
            var correction = verification.Corrections.Single();
            if (correction.SourceId != documentId || correction.MovementType is not ("Sale" or "CreditSale")
                || correction.SourceDocumentType != "SalesReceipt")
                throw new WorkSessionValidationException("La corrección debe pertenecer al comprobante seleccionado.");
            var delta = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            if (correction.MovementType != "CreditSale") delta[correction.OriginalPaymentMethodCode] = -correction.OriginalAmount;
            delta[correction.PaymentMethodCode] = delta.GetValueOrDefault(correction.PaymentMethodCode) + correction.Amount;
            var lines = new List<WorkSessionClosureReconciliationAccountingLine>();
            var allocations = new List<WorkSessionMethodChange>();
            await using (var groups = new SqlCommand("""
                WITH UsedAllocations AS
                (
                  SELECT allocation.PaymentMethodCode,SUM(allocation.Amount) Amount
                  FROM accounting.SalePaymentCorrections source
                  CROSS APPLY OPENJSON(source.ClosureAllocationsJson) WITH(
                    PaymentMethodCode nvarchar(32) '$.paymentMethodCode',Amount decimal(19,4) '$.amount') allocation
                  WHERE source.TenantId=@Tenant AND source.BusinessId=@Business AND source.WorkSessionClosureId=@Closure
                    AND source.PostingStatus IN(N'Posted',N'CommercialEffectsApplied')
                  GROUP BY allocation.PaymentMethodCode
                )
                SELECT total.PaymentMethodCode,mapping.Category,total.Difference,
                  COALESCE(used.Amount,0)
                FROM dbo.WorkSessionClosurePaymentTotals total
                JOIN reference.Options optionValue ON optionValue.CatalogCode=N'cash-closure-method'
                  AND optionValue.Code=total.PaymentMethodCode AND optionValue.IsActive=1
                CROSS APPLY accounting.PaymentPostingCategory(N'ClosurePaymentMethod',total.PaymentMethodCode) mapping
                LEFT JOIN UsedAllocations used ON used.PaymentMethodCode=total.PaymentMethodCode
                WHERE total.WorkSessionClosureId=@Closure;
                """, connection, transaction))
            {
                groups.Parameters.AddWithValue("@Tenant", identity.TenantId);
                groups.Parameters.AddWithValue("@Business", scope.BusinessId);
                groups.Parameters.AddWithValue("@Closure", scope.ClosureId);
                await using var reader = await groups.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token))
                {
                    var method = reader.GetString(0);
                    var change = delta.GetValueOrDefault(method);
                    var remaining = (reader.IsDBNull(2) ? 0 : reader.GetDecimal(2)) - reader.GetDecimal(3);
                    var allocation = Math.Sign(change) == Math.Sign(remaining)
                        ? Math.Sign(change) * Math.Min(Math.Abs(change), Math.Abs(remaining)) : 0;
                    var expected = Math.Max(-change, 0);
                    var verified = Math.Max(change, 0);
                    lines.Add(new(method, reader.GetString(1), expected, expected + allocation, verified, change, null));
                    if (allocation != 0) allocations.Add(new(method, allocation));
                }
            }
            if (delta.Keys.Any(method => lines.All(line => line.PaymentMethodCode != method))
                || lines.Select(line => line.PaymentMethodCode).Distinct(StringComparer.OrdinalIgnoreCase).Count() != lines.Count)
                throw new WorkSessionValidationException("El medio seleccionado no está configurado en este cierre.");
            var now = timeProvider.GetUtcNow();
            var payload = new WorkSessionClosureReconciliationPayload(request.CorrectionId, scope.ClosureId,
                identity.TenantId, scope.BusinessId, identity.UserId, now, lines, [], [correction],
                DirectSaleDocumentId: documentId, DirectRequestHash: hash, ClosureAllocations: allocations);
            var count = await SqlAccountingPostingJobWriter.InsertSourcesAsync(connection, transaction,
                [new(identity.TenantId, scope.BusinessId, request.CorrectionId,
                    WorkSessionAccountingDocumentTypes.ClosureReconciliation, JsonSerializer.Serialize(payload, Json), now, ids.NewId())],
                now, AccountingJobRequirement.PreserveCommercialEffects, token);
            if (count != 1) throw new WorkSessionConflictException("No se pudo aceptar la corrección financiera.");
            await SqlSalesReportingJobWriter.InsertPaymentCorrectionAsync(connection, transaction,
                scope.BusinessId, request.CorrectionId, WorkSessionAccountingDocumentTypes.ClosureReconciliation,
                ids.NewId(), now, token);
            await transaction.CommitAsync(token);
            return new(request.CorrectionId, documentId, scope.BusinessId, "Pending");
        }
        catch (SqlException error) when (error.Number is 2601 or 2627 or 51732 or 51871)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new WorkSessionConflictException(error.Number == 51871 ? error.Message : "El recaudo ya tiene una corrección aceptada.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private sealed record SaleCorrectionScope(Guid ClosureId, string Number, string? Customer,
        DateTimeOffset IssuedAt, bool Pending, Guid BusinessId);

    private static async Task<SaleCorrectionScope> ReadSaleCorrectionScopeAsync(SqlConnection connection,
        SqlTransaction transaction, WorkSessionIdentity identity, Guid documentId, CancellationToken token)
    {
        await using var command = new SqlCommand("""
            DECLARE @Closure uniqueidentifier=(SELECT closure.WorkSessionClosureId
              FROM dbo.SalesDocuments sale
              JOIN dbo.WorkSessionClosures closure ON closure.WorkSessionId=sale.WorkSessionId
              JOIN dbo.WorkSessions session ON session.WorkSessionId=sale.WorkSessionId
                AND session.TenantId=@Tenant AND session.BusinessId=sale.BusinessId AND session.Kind=N'Normal'
              WHERE sale.DocumentId=@Document AND sale.DocumentType=N'SalesReceipt'
                AND sale.CollectionMode=N'Session');
            -- Match closure acceptance's lock order before taking the sale lock.
            SELECT WorkSessionClosureId FROM dbo.WorkSessionClosures WITH(UPDLOCK,HOLDLOCK)
              WHERE WorkSessionClosureId=@Closure;
            SELECT closure.WorkSessionClosureId,sale.DocumentNumber,sale.CustomerNameSnapshot,sale.IssuedAt,
              CONVERT(bit,CASE WHEN closure.ReconciliationStatus=N'Pending' AND NOT EXISTS(
                SELECT 1 FROM dbo.WorkSessionClosureReconciliations accepted WHERE accepted.WorkSessionClosureId=closure.WorkSessionClosureId)
                THEN 1 ELSE 0 END),sale.BusinessId
            FROM dbo.SalesDocuments sale WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.WorkSessions session ON session.WorkSessionId=sale.WorkSessionId AND session.Kind=N'Normal'
              AND session.BusinessId=sale.BusinessId AND session.TenantId=@Tenant
            JOIN dbo.WorkSessionClosures closure WITH(UPDLOCK,HOLDLOCK) ON closure.WorkSessionId=session.WorkSessionId
            WHERE sale.DocumentId=@Document AND sale.DocumentType=N'SalesReceipt'
              AND sale.CollectionMode=N'Session';
            """, connection, transaction);
        command.Parameters.AddWithValue("@Tenant", identity.TenantId);
        command.Parameters.AddWithValue("@Document", documentId);
        await using var reader = await command.ExecuteReaderAsync(token);
        await reader.NextResultAsync(token);
        if (!await reader.ReadAsync(token)) throw new WorkSessionValidationException(
            "Selecciona un comprobante de esta sede cuya sesión de caja esté cerrada y pendiente de conciliación.");
        return new(reader.GetGuid(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetFieldValue<DateTimeOffset>(3), reader.GetBoolean(4), reader.GetGuid(5));
    }
}
