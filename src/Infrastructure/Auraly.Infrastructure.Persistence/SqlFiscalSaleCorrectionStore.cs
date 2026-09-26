using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Fiscal;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlFiscalSaleCorrectionStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time) : IFiscalSaleCorrectionStore
{
    public async Task<DuplicateFiscalCorrectionResult> CreateDuplicateCorrectionAsync(
        FiscalUserIdentity user,
        Guid originalDocumentId,
        Guid retainedDocumentId,
        CancellationToken cancellationToken)
    {
        if (originalDocumentId == Guid.Empty || retainedDocumentId == Guid.Empty ||
            originalDocumentId == retainedDocumentId)
            throw new FiscalOperationException(
                "Selecciona la factura duplicada y una factura distinta para conservar.");

        var issuedAt = time.GetUtcNow();
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var prior = await LoadExistingAsync(connection, transaction,
                user.BusinessId, originalDocumentId, cancellationToken);
            if (prior is not null)
            {
                if (prior.RetainedDocumentId != retainedDocumentId)
                    throw new FiscalOperationException(
                        "La factura duplicada ya tiene una corrección vinculada a otra factura.");
                await transaction.CommitAsync(cancellationToken);
                return prior;
            }

            var state = await LoadOriginalAndRetainedAsync(connection, transaction,
                user, originalDocumentId, retainedDocumentId, issuedAt, cancellationToken)
                ?? throw new FiscalOperationException(
                    "Las dos facturas no cumplen las condiciones para corregir una duplicación.");
            var original = PosSaleContractSerializer.Deserialize(state.OriginalJson);
            var retained = PosSaleContractSerializer.Deserialize(state.RetainedJson);
            ValidatePair(state, original, retained, originalDocumentId,
                retainedDocumentId, user.BusinessId);
            await StopPendingDuplicateDeliveryAsync(connection, transaction,
                user.BusinessId, originalDocumentId, issuedAt, cancellationToken);

            var number = await SqlOperationalDocumentAllocator.AllocateNumberAsync(
                connection, transaction, user.BusinessId,
                AuralyDocumentTypes.FiscalSaleCorrection, issuedAt, cancellationToken);
            var correctionId = ids.NewId();
            var snapshot = BuildSnapshot(correctionId, state, original, retained,
                number.FullNumber, issuedAt);
            var json = FiscalOnlyCreditNoteSnapshotSerializer.Serialize(snapshot);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));

            await using var insert = new SqlCommand("""
                INSERT dbo.FiscalDocuments(
                  DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
                  AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,
                  IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
                VALUES(@CorrectionId,@BusinessId,N'FiscalSaleCorrection',N'CreditNote',
                  @Number,@Number,N'CUDE',NULL,@IssuedAt,N'PendingGeneration',@IssuedAt,@IssuedAt);
                INSERT dbo.FiscalSaleCorrections(
                  CorrectionId,BusinessId,OriginalDocumentId,RetainedDocumentId,
                  CreatedByUserId,DocumentSeriesId,DocumentConsecutive,ReasonCode,
                  IssuedAt,SnapshotJson,PayloadHash,FiscalStatus,CreatedAt)
                VALUES(@CorrectionId,@BusinessId,@OriginalId,@RetainedId,
                  @UserId,@SeriesId,@Consecutive,N'DuplicateInvoice',
                  @IssuedAt,@SnapshotJson,@PayloadHash,N'PendingGeneration',@IssuedAt);
                INSERT dbo.FiscalDocumentProcesses(
                  DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,
                  AttemptCount,CreatedAt,UpdatedAt)
                VALUES(@CorrectionId,@BusinessId,@IssuerId,N'PendingGeneration',
                  0,@IssuedAt,@IssuedAt);
                """, connection, transaction);
            insert.Parameters.AddWithValue("@CorrectionId", correctionId);
            insert.Parameters.AddWithValue("@BusinessId", user.BusinessId);
            insert.Parameters.AddWithValue("@OriginalId", originalDocumentId);
            insert.Parameters.AddWithValue("@RetainedId", retainedDocumentId);
            insert.Parameters.AddWithValue("@UserId", user.UserId);
            insert.Parameters.AddWithValue("@SeriesId", number.SeriesId);
            insert.Parameters.AddWithValue("@Consecutive", number.Consecutive);
            insert.Parameters.AddWithValue("@Number", number.FullNumber);
            insert.Parameters.AddWithValue("@IssuedAt", issuedAt);
            insert.Parameters.AddWithValue("@SnapshotJson", json);
            insert.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = hash;
            insert.Parameters.AddWithValue("@IssuerId", state.IssuerId);
            if (await insert.ExecuteNonQueryAsync(cancellationToken) != 3)
                throw new DBConcurrencyException(
                    "La nota crédito fiscal no pudo guardarse de manera completa.");

            await transaction.CommitAsync(cancellationToken);
            return new(correctionId, originalDocumentId, retainedDocumentId,
                FiscalDocumentStatusCodes.PendingGeneration, true);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw new FiscalOperationException(
                "La factura duplicada ya tiene una corrección o la numeración cambió. Consulta su estado antes de repetir.");
        }
        catch (SqlException exception) when (exception.Number == 51022)
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw new FiscalOperationException(
                "El correo de la factura duplicada está en curso; espera su resultado antes de corregirla.");
        }
        catch
        {
            if (transaction.Connection is not null)
                await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<DuplicateFiscalCorrectionResult?> LoadExistingAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        Guid originalDocumentId, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT correction.CorrectionId,correction.RetainedDocumentId,
                   fiscal.FiscalStatus
            FROM dbo.FiscalSaleCorrections correction WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.FiscalDocuments fiscal
              ON fiscal.DocumentId=correction.CorrectionId
             AND fiscal.BusinessId=correction.BusinessId
            WHERE correction.BusinessId=@BusinessId
              AND correction.OriginalDocumentId=@OriginalId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@OriginalId", originalDocumentId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new DuplicateFiscalCorrectionResult(
                reader.GetGuid(0), originalDocumentId, reader.GetGuid(1),
                reader.GetString(2), false)
            : null;
    }

    private static async Task StopPendingDuplicateDeliveryAsync(
        SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid documentId, DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            IF EXISTS(
              SELECT 1
              FROM dbo.FiscalDocuments fiscal WITH(UPDLOCK,HOLDLOCK)
              JOIN dbo.TenantProvisioningOutboxMessages message WITH(UPDLOCK,HOLDLOCK)
                ON message.MessageId=fiscal.DeliveryOutboxMessageId
              WHERE fiscal.DocumentId=@DocumentId AND fiscal.BusinessId=@BusinessId
                AND fiscal.DeliveredAt IS NULL AND message.ProcessedAt IS NULL
                AND message.LeaseId IS NOT NULL AND message.LeaseExpiresAt>@Now)
                THROW 51022,N'El correo de la factura duplicada está en curso; espera su resultado antes de corregirla.',1;

            UPDATE message
            SET ProcessedAt=@Now,
                LastError=N'Cancelado: factura electrónica duplicada corregida con nota crédito.'
            FROM dbo.TenantProvisioningOutboxMessages message
            JOIN dbo.FiscalDocuments fiscal
              ON fiscal.DeliveryOutboxMessageId=message.MessageId
            WHERE fiscal.DocumentId=@DocumentId AND fiscal.BusinessId=@BusinessId
              AND fiscal.DeliveredAt IS NULL AND message.ProcessedAt IS NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<CorrectionSourceState?> LoadOriginalAndRetainedAsync(
        SqlConnection connection, SqlTransaction transaction, FiscalUserIdentity user,
        Guid originalId, Guid retainedId, DateTimeOffset issuedAt,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT original.ProcessingStatus,original.FiscalStatus,
                   original.CufeReceived,original.PayloadHash,
                   originalSnapshot.PayloadHash,originalSnapshot.SnapshotJson,
                   retained.ProcessingStatus,retained.FiscalStatus,
                   retained.CufeReceived,retained.PayloadHash,
                   retainedSnapshot.PayloadHash,retainedSnapshot.SnapshotJson,
                   link.OrderId,
                   issuer.FiscalIssuerConfigurationId,issuer.Environment,
                   fiscalAuth.QrValidationUrl,
                   CONVERT(bit,CASE WHEN EXISTS(
                     SELECT 1 FROM dbo.DocumentProcessingJobs j
                     WHERE j.DocumentId=original.DocumentId)
                     OR EXISTS(SELECT 1 FROM dbo.SalesDocumentLines line
                       WHERE line.DocumentId=original.DocumentId)
                     OR EXISTS(SELECT 1 FROM dbo.SalesPayments payment
                       WHERE payment.DocumentId=original.DocumentId)
                     OR EXISTS(SELECT 1 FROM dbo.Receivables receivable
                       WHERE receivable.SourceDocumentId=original.DocumentId)
                     OR EXISTS(SELECT 1 FROM dbo.InventoryMovements movement
                       WHERE movement.DocumentId=original.DocumentId)
                     OR EXISTS(SELECT 1 FROM dbo.OrderInvoiceLinks linked
                       WHERE linked.DocumentId=original.DocumentId)
                     OR EXISTS(SELECT 1 FROM dbo.AccountingPostingJobs posting
                       WHERE posting.SourceDocumentId=original.DocumentId)
                   THEN 1 ELSE 0 END) HasOriginalEffects,
                   (SELECT COUNT(*) FROM dbo.SalesDocumentLines line
                    WHERE line.DocumentId=retained.DocumentId) RetainedLineCount
            FROM dbo.SalesDocuments original WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business ON business.BusinessId=original.BusinessId
            JOIN dbo.AppUsers operatorUser ON operatorUser.UserId=@UserId
              AND operatorUser.TenantId=business.TenantId AND operatorUser.IsActive=1
            JOIN dbo.FiscalSnapshots originalSnapshot WITH(UPDLOCK,HOLDLOCK)
              ON originalSnapshot.DocumentId=original.DocumentId
             AND originalSnapshot.IntegrityStatus=N'FiscalVerified'
            JOIN dbo.FiscalDocuments originalFiscal WITH(UPDLOCK,HOLDLOCK)
              ON originalFiscal.DocumentId=original.DocumentId
             AND originalFiscal.BusinessId=original.BusinessId
             AND originalFiscal.FiscalStatus=N'DianAccepted'
             AND originalFiscal.UniqueCode=original.CufeReceived
            JOIN dbo.FiscalDocumentProcesses originalProcess
              ON originalProcess.DocumentId=original.DocumentId
             AND originalProcess.BusinessId=original.BusinessId
             AND originalProcess.Status=N'DianAccepted'
            JOIN dbo.SalesDocuments retained WITH(UPDLOCK,HOLDLOCK)
              ON retained.DocumentId=@RetainedId
             AND retained.BusinessId=original.BusinessId
             AND retained.DocumentType=N'SalesInvoice'
             AND retained.SourceMode=N'Online'
            JOIN dbo.FiscalSnapshots retainedSnapshot WITH(UPDLOCK,HOLDLOCK)
              ON retainedSnapshot.DocumentId=retained.DocumentId
             AND retainedSnapshot.IntegrityStatus=N'FiscalVerified'
            JOIN dbo.FiscalDocuments retainedFiscal WITH(UPDLOCK,HOLDLOCK)
              ON retainedFiscal.DocumentId=retained.DocumentId
             AND retainedFiscal.BusinessId=retained.BusinessId
             AND retainedFiscal.FiscalStatus=N'DianAccepted'
             AND retainedFiscal.UniqueCode=retained.CufeReceived
            JOIN dbo.OrderInvoiceLinks link WITH(UPDLOCK,HOLDLOCK)
              ON link.DocumentId=retained.DocumentId
             AND link.BusinessId=retained.BusinessId
            JOIN dbo.AccountingPostingJobs finance
              ON finance.SourceDocumentId=retained.DocumentId
             AND finance.SourceDocumentType=N'SalesInvoice'
             AND finance.Status IN(N'Posted',N'CommercialEffectsApplied')
            JOIN dbo.FiscalAuthorizations fiscalAuth
              ON fiscalAuth.FiscalAuthorizationId=original.FiscalAuthorizationId
             AND fiscalAuth.BusinessId=original.BusinessId
            CROSS APPLY(
              SELECT TOP(1) configuration.FiscalIssuerConfigurationId,
                     configuration.Environment
              FROM dbo.FiscalIssuerConfigurations configuration
              WHERE configuration.BusinessId=original.BusinessId
                AND configuration.IsActive=1
                AND configuration.ValidFrom<=@IssuedAt
                AND (configuration.ValidTo IS NULL OR configuration.ValidTo>@IssuedAt)
              ORDER BY configuration.Version DESC) issuer
            WHERE original.DocumentId=@OriginalId
              AND original.BusinessId=@BusinessId
              AND original.DocumentType=N'SalesInvoice'
              AND original.SourceMode=N'Online'
              AND originalProcess.TrackId IS NOT NULL
              AND EXISTS(SELECT 1 FROM dbo.FiscalTransmissionAttempts attempt
                WHERE attempt.DocumentId=original.DocumentId
                  AND attempt.Disposition=N'Accepted'
                  AND attempt.StatusCode=N'00'
                  AND attempt.MayHaveReachedDian=1)
              AND EXISTS(SELECT 1 FROM dbo.FiscalTransmissionAttempts attempt
                WHERE attempt.DocumentId=retained.DocumentId
                  AND attempt.Disposition=N'Accepted'
                  AND attempt.StatusCode=N'00'
                  AND attempt.MayHaveReachedDian=1);
            """, connection, transaction);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@OriginalId", originalId);
        command.Parameters.AddWithValue("@RetainedId", retainedId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@IssuedAt", issuedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new CorrectionSourceState(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetFieldValue<byte[]>(3), reader.GetFieldValue<byte[]>(4),
                reader.GetString(5), reader.GetString(6), reader.GetString(7),
                reader.GetString(8), reader.GetFieldValue<byte[]>(9),
                reader.GetFieldValue<byte[]>(10), reader.GetString(11),
                reader.GetGuid(12), reader.GetGuid(13), reader.GetByte(14),
                reader.GetString(15), reader.GetBoolean(16), reader.GetInt32(17))
            : null;
    }

    private static void ValidatePair(
        CorrectionSourceState state,
        PosSaleUploadRequest original,
        PosSaleUploadRequest retained,
        Guid originalId,
        Guid retainedId,
        Guid businessId)
    {
        if (state.OriginalStatus != "Blocked" ||
            state.OriginalFiscalStatus != FiscalDocumentStatusCodes.DianAccepted ||
            state.RetainedStatus != "Completed" ||
            state.RetainedFiscalStatus != FiscalDocumentStatusCodes.DianAccepted ||
            state.HasOriginalEffects ||
            original.DocumentId != originalId || retained.DocumentId != retainedId ||
            original.BusinessId != businessId || retained.BusinessId != businessId ||
            original.SourceMode != SaleSourceModes.Online ||
            retained.SourceMode != SaleSourceModes.Online ||
            original.SourceOrderId is null ||
            original.SourceOrderId != retained.SourceOrderId ||
            original.SourceOrderId != state.LinkedOrderId ||
            original.CustomerId != retained.CustomerId ||
            original.CustomerPartySiteId != retained.CustomerPartySiteId ||
            original.FiscalSnapshot is null || retained.FiscalSnapshot is null ||
            original.UblSnapshot is null || retained.UblSnapshot is null ||
            original.FiscalSnapshot.Cufe != state.OriginalCufe ||
            retained.FiscalSnapshot.Cufe != state.RetainedCufe ||
            original.FiscalSnapshot.Environment != state.IssuerEnvironment ||
            original.FiscalSnapshot.PayableRoundingAmount != 0m ||
            original.FiscalSnapshot.PayableAmount != retained.FiscalSnapshot.PayableAmount ||
            original.FiscalSnapshot.UntaxedAmount != retained.FiscalSnapshot.UntaxedAmount ||
            state.RetainedLineCount != retained.Lines.Count ||
            !PosSaleContractSerializer.Hash(original).AsSpan().SequenceEqual(state.OriginalHash) ||
            !state.OriginalHash.AsSpan().SequenceEqual(state.OriginalSnapshotHash) ||
            !PosSaleContractSerializer.Hash(retained).AsSpan().SequenceEqual(state.RetainedHash) ||
            !state.RetainedHash.AsSpan().SequenceEqual(state.RetainedSnapshotHash) ||
            JsonSerializer.Serialize(original.Lines) != JsonSerializer.Serialize(retained.Lines) ||
            JsonSerializer.Serialize(original.Charges) != JsonSerializer.Serialize(retained.Charges))
            throw new FiscalOperationException(
                "Las facturas no representan exactamente el mismo pedido o ya tienen efectos comerciales.");
    }

    private static FiscalOnlyCreditNoteSnapshot BuildSnapshot(
        Guid correctionId, CorrectionSourceState state,
        PosSaleUploadRequest original, PosSaleUploadRequest retained,
        string fiscalNumber, DateTimeOffset issuedAt)
    {
        var fiscal = original.FiscalSnapshot!;
        var ubl = original.UblSnapshot!;
        var metadata = ubl.Lines.ToDictionary(line => line.LineNumber);
        if (metadata.Count != original.Lines.Count ||
            ubl.Customer.Identification != fiscal.CustomerIdentification)
            throw new FiscalOperationException(
                "La factura duplicada no conserva los metadatos fiscales completos.");
        var lines = new List<FiscalOnlyCreditNoteLine>(original.Lines.Count +
            (original.Charges?.Count ?? 0));
        foreach (var line in original.Lines.OrderBy(line => line.LineNumber))
        {
            if (!metadata.TryGetValue(line.LineNumber, out var item) ||
                item.TaxPercent != line.TaxRate)
                throw new FiscalOperationException(
                    "Los impuestos de la factura duplicada no coinciden con su snapshot UBL.");
            lines.Add(new FiscalOnlyCreditNoteLine(
                line.LineNumber, item.ProductCode, item.ProductCodeScheme,
                line.Description, item.UnitCode, line.Quantity, line.UnitPrice,
                line.DiscountAmount, line.UntaxedAmount, line.TaxCode,
                item.TaxName, line.TaxAmount, item.TaxPercent));
        }
        foreach (var charge in original.Charges ?? [])
        {
            if (charge.InvoicedAmount <= 0) continue;
            lines.Add(new FiscalOnlyCreditNoteLine(
                lines.Count + 1, charge.Code, "999", charge.Name, "EA", 1m,
                charge.InvoicedUntaxedAmount, 0m,
                charge.InvoicedUntaxedAmount, charge.TaxCode,
                PosSaleFiscalMappings.TaxName(charge.TaxCode),
                charge.InvoicedTaxAmount, charge.TaxRate));
        }
        var untaxed = lines.Sum(line => line.UntaxedAmount);
        var tax = lines.Sum(line => line.TaxAmount);
        if (untaxed != fiscal.UntaxedAmount ||
            untaxed + tax != fiscal.PayableAmount)
            throw new FiscalOperationException(
                "El valor de la nota crédito no concilia con la factura aceptada.");
        return new FiscalOnlyCreditNoteSnapshot(
            correctionId, original.BusinessId, original.DocumentId,
            retained.DocumentId, state.IssuerId, fiscalNumber,
            ubl.CurrencyCode, state.IssuerEnvironment,
            state.QrValidationUrl, ubl.Customer,
            fiscal.CustomerIdentification, fiscal.FiscalNumber, fiscal.Cufe,
            DianFiscalDateTime.DateInColombia(fiscal.IssuedAt), issuedAt,
            untaxed, fiscal.PayableAmount,
            lines.Sum(line => line.DiscountAmount), lines);
    }

    private sealed record CorrectionSourceState(
        string OriginalStatus,
        string OriginalFiscalStatus,
        string OriginalCufe,
        byte[] OriginalHash,
        byte[] OriginalSnapshotHash,
        string OriginalJson,
        string RetainedStatus,
        string RetainedFiscalStatus,
        string RetainedCufe,
        byte[] RetainedHash,
        byte[] RetainedSnapshotHash,
        string RetainedJson,
        Guid LinkedOrderId,
        Guid IssuerId,
        byte IssuerEnvironment,
        string QrValidationUrl,
        bool HasOriginalEffects,
        int RetainedLineCount);
}
