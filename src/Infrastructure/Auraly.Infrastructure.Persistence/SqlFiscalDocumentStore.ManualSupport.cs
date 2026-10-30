using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Auraly.Application.Fiscal;
using Auraly.Commerce.Accounting.Application;
using Auraly.Commerce.Accounting.Contracts;
using Auraly.Commerce.Accounting.Infrastructure;
using Auraly.Commerce.Taxation.Contracts;
using Auraly.Commerce.Taxation.Application;
using Auraly.Commerce.Taxation.Domain;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlFiscalDocumentStore
{
    public async Task<string?> ReleaseManualSupportAsync(Guid businessId, Guid documentId,
        DateTimeOffset requestedAt, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        string? status;
        string? postingStatus;
        await using (var read = new SqlCommand("""
            SELECT process.Status,posting.Status
            FROM dbo.FiscalDocumentProcesses process WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.FiscalDocuments fiscal ON fiscal.DocumentId=process.DocumentId
              AND fiscal.BusinessId=process.BusinessId
            LEFT JOIN dbo.AccountingPostingJobs posting ON posting.SourceDocumentId=fiscal.DocumentId
              AND posting.SourceDocumentType=fiscal.SourceDocumentType
              AND posting.BusinessId=fiscal.BusinessId
            WHERE process.DocumentId=@DocumentId AND process.BusinessId=@BusinessId
              AND fiscal.SourceDocumentType=@SourceType
              AND fiscal.FiscalDocumentType IN(N'SupportDocument',N'SupportDocumentAdjustment');
            """, connection, transaction))
        {
            read.Parameters.AddWithValue("@DocumentId", documentId);
            read.Parameters.AddWithValue("@BusinessId", businessId);
            read.Parameters.AddWithValue("@SourceType", AccountingManualDocumentTypes.ManualVoucher);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                status = null;
                postingStatus = null;
            }
            else
            {
                status = reader.GetString(0);
                postingStatus = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }
        if (status is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }
        if ((status is FiscalDocumentStatusCodes.AwaitingManualSubmission or
            FiscalDocumentStatusCodes.PendingGeneration) && postingStatus != "Posted")
            throw new FiscalOperationException(
                "Espera a que el comprobante esté contabilizado antes de enviarlo a la DIAN.");
        if (status == FiscalDocumentStatusCodes.AwaitingManualSubmission)
        {
            bool reserved;
            try
            {
                reserved = await SqlDianDocumentQuota.TryReserveAsync(connection, transaction,
                    businessId, documentId, FiscalDocumentTypeCodes.SupportDocument,
                    requestedAt, cancellationToken);
            }
            catch (Auraly.Application.Sales.PosSaleInvalidException error)
            { throw new FiscalOperationException(error.Message); }
            if (!reserved)
                throw new FiscalOperationException(
                    "No hay cupos DIAN disponibles para enviar el documento soporte.");
            await using var update = new SqlCommand("""
                UPDATE dbo.FiscalDocumentProcesses
                SET Status=@Pending,NextAttemptAt=@Now,UpdatedAt=@Now
                WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND Status=@Awaiting;
                IF @@ROWCOUNT<>1 THROW 51736,N'El estado fiscal cambió antes del envío.',1;
                UPDATE dbo.FiscalDocuments
                SET FiscalStatus=@Pending,UpdatedAt=@Now
                WHERE DocumentId=@DocumentId AND BusinessId=@BusinessId AND FiscalStatus=@Awaiting;
                IF @@ROWCOUNT<>1 THROW 51736,N'El documento fiscal cambió antes del envío.',1;
                """, connection, transaction);
            update.Parameters.AddWithValue("@DocumentId", documentId);
            update.Parameters.AddWithValue("@BusinessId", businessId);
            update.Parameters.AddWithValue("@Now", requestedAt);
            update.Parameters.AddWithValue("@Pending", FiscalDocumentStatusCodes.PendingGeneration);
            update.Parameters.AddWithValue("@Awaiting", FiscalDocumentStatusCodes.AwaitingManualSubmission);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return status == FiscalDocumentStatusCodes.AwaitingManualSubmission
            ? FiscalDocumentStatusCodes.PendingGeneration : status;
    }
    private static readonly JsonSerializerOptions ManualReviewHashJson = new()
    {
        Converters = { new ManualReviewDecimalConverter() }
    };

    // SQL returns decimal(19,4), while HTTP may carry the same value without
    // trailing zeroes. Only the review signature uses this numeric normalization.
    private sealed class ManualReviewDecimalConverter : JsonConverter<decimal>
    {
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString("G29", CultureInfo.InvariantCulture));
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            throw new NotSupportedException("The review signature serializer only writes canonical values.");
    }

    public async Task<VoucherFiscalCalculation> ReviewAsync(SqlConnection connection, SqlTransaction? transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request, CancellationToken token)
    {
        if (request.VoucherTypeCode == "SupportDocumentCancellation")
            return await ReviewManualSupportCancellationAsync(connection, transaction, user, request, token);
        var capture = request.FiscalCapture
            ?? throw new AccountingValidationException("Agrega las bases fiscales del documento soporte.");
        await using var command = new SqlCommand("""
            SELECT supplier.SupplierId,supplier.PurchaseEvidencePolicy,settings.FunctionalCurrencyCode
            FROM dbo.Businesses business
            JOIN dbo.AccountingTenantSettings settings ON settings.TenantId=business.TenantId
            JOIN dbo.Parties party ON party.TenantId=business.TenantId AND party.PartyId=@PartyId AND party.IsActive=1
            JOIN dbo.Suppliers supplier ON supplier.TenantId=party.TenantId AND supplier.PartyId=party.PartyId AND supplier.IsActive=1
            WHERE business.TenantId=@TenantId AND business.BusinessId=@BusinessId;
            SELECT requested.SourceLineId,COALESCE(profile.Name,N'IVA'),COALESCE(profile.DianTaxCode,N'01'),COALESCE(profile.Rate,0)
            FROM OPENJSON(@Bases) WITH(SourceLineId uniqueidentifier,TaxProfileId uniqueidentifier) requested
            LEFT JOIN dbo.TaxProfiles profile ON profile.TaxProfileId=requested.TaxProfileId AND profile.TenantId=@TenantId
              AND profile.IsActive=1 AND profile.DianTaxCode=N'01'
            WHERE requested.TaxProfileId IS NULL OR profile.TaxProfileId IS NOT NULL;
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@PartyId", (object?)request.MainPartyId ?? DBNull.Value);
        command.Parameters.Add("@Bases", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(capture.Acquisitions);
        Guid supplierId;
        var inputs = capture.Acquisitions.ToDictionary(item => item.SourceLineId);
        var acquisitions = new List<VoucherFiscalCalculatedAcquisition>(inputs.Count);
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token))
                throw new AccountingValidationException("Configura contabilidad y selecciona un proveedor principal activo de esta empresa.");
            supplierId = reader.GetGuid(0);
            var policy = reader.IsDBNull(1) ? null : reader.GetString(1);
            if (reader.GetString(2) != "COP" || request.CurrencyCode != "COP")
                throw new AccountingValidationException("El documento soporte manual requiere moneda funcional COP.");
            if (!PurchaseEvidenceTypes.AllowedFor(policy).Contains(PurchaseEvidenceTypes.BuyerElectronicSupportDocument))
                throw new AccountingValidationException("La política del proveedor no permite generar documento soporte de esta adquisición.");
            if (await reader.ReadAsync(token))
                throw new AccountingValidationException("El tercero tiene un rol de proveedor ambiguo; concilia su configuración antes de continuar.");
            await reader.NextResultAsync(token);
            while (await reader.ReadAsync(token))
            {
                var value = inputs[reader.GetGuid(0)];
                var rate = reader.GetDecimal(3);
                acquisitions.Add(new(value, reader.GetString(1), reader.GetString(2), rate,
                    AcquisitionTaxAmounts.InputVat(value.TaxExclusiveAmount, rate)));
            }
        }
        if (acquisitions.Count != inputs.Count)
            throw new AccountingValidationException("Un perfil de IVA no está activo, no corresponde a esta empresa o no es válido para documento soporte.");
        // Keep fiscal numbering and hashes in the capture order, independent of
        // a SQL plan's ordering. Resolution and validation remain by set.
        var resolved = acquisitions.ToDictionary(item => item.Value.SourceLineId);
        acquisitions = capture.Acquisitions.Select(item => resolved[item.SourceLineId]).ToList();
        var manualAccounts = capture.WithholdingAdjustments?
            .Where(item => item.Action == "Manual" && item.AccountId.HasValue)
            .Select(item => item.AccountId!.Value).Distinct().ToArray();
        var plan = await withholding.PrepareCalculationPlanAsync(user.TenantId, user.BusinessId,
            [supplierId], token, manualAccounts);
        var concepts = plan.Rules.Where(rule => rule.Direction == WithholdingDirection.Purchase &&
                rule.Moment == WithholdingRecognitionMoment.Accrual && rule.ConceptCode is not null)
            .Select(rule => rule.ConceptCode!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (acquisitions.Any(item => !string.IsNullOrWhiteSpace(item.Value.WithholdingConceptCode) &&
            !concepts.Contains(item.Value.WithholdingConceptCode.Trim())))
            throw new AccountingValidationException("Un concepto tributario del soporte no tiene una regla de compra activa.");
        Auraly.Commerce.Taxation.Application.PurchaseWithholdingReview review;
        try
        {
            review = withholding.ReviewPurchaseDocument(plan, supplierId, acquisitions.Select(item =>
                new WithholdingPreviewRequest(user.BusinessId, WithholdingDirections.Purchase,
                    WithholdingRecognitionMoments.Accrual, supplierId,
                    string.IsNullOrWhiteSpace(item.Value.WithholdingConceptCode) ? null : item.Value.WithholdingConceptCode.Trim().ToUpperInvariant(),
                    null, item.Value.TaxExclusiveAmount, item.VatAmount, request.OccurredAt)).ToArray(),
                capture.WithholdingAdjustments, user.UserId, request.OccurredAt);
        }
        catch (WithholdingRuleException error)
        { throw new AccountingValidationException($"Revisa la configuración de retenciones: {error.Message}"); }
        catch (TaxationValidationException error) { throw new AccountingValidationException(error.Message); }
        var canonicalLines = request.Lines.Select(line => line with {
            Description = line.Description.Trim(),
            Reference = string.IsNullOrWhiteSpace(line.Reference) ? null : line.Reference.Trim() }).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {
            request.VoucherId, request.BusinessId, request.MainPartyId, request.OccurredAt,
            Description = request.Description.Trim(),
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            request.ConceptCode, request.CurrencyCode, request.VoucherTypeCode, request.WorkSessionId, Lines = canonicalLines,
            acquisitions, capture.WithholdingBindings, capture.WithholdingAdjustments,
            supplierId, review.Calculation, review.Diagnostics, review.CanConfirm }, ManualReviewHashJson))));
        return new(supplierId, acquisitions, review.Calculation, hash, review.Diagnostics, review.CanConfirm);
    }

    public async Task<PreparedManualVoucherFiscal> PrepareAsync(SqlConnection connection, SqlTransaction transaction,
        AccountingUserIdentity user, ConfirmManualAccountingVoucherRequest request,
        VoucherFiscalCalculation calculation, VoucherFiscalPreview preview, CancellationToken token)
    {
        if (transaction.Connection != connection)
            throw new InvalidOperationException("Manual support requires the journal acceptance transaction.");
        if (!preview.CanConfirm)
            throw new AccountingValidationException(string.Join(" ", preview.Diagnostics));
        if (request.FiscalCapture?.CalculationHash != preview.CalculationHash)
            throw new AccountingConflictException("El cálculo fiscal cambió. Recalcula y revisa el soporte antes de contabilizar.");
        var siteId = preview.SupplierPartySiteId
            ?? throw new AccountingValidationException("Selecciona la sede del proveedor del documento soporte.");
        var now = timeProvider.GetUtcNow();
        SqlGoodsReceiptStore.SupportFiscalAllocation support;
        try
        {
            support = await SqlGoodsReceiptStore.AllocateSupportFiscalAsync(connection, transaction,
                user.BusinessId, calculation.SupplierId, request.OccurredAt, now, token, siteId);
        }
        catch (Auraly.Application.Purchasing.PurchasingValidationException error)
        { throw new AccountingValidationException(error.Message); }
        if (calculation.Cancellation is { } cancellation)
        {
            var original = cancellation.FiscalSnapshot;
            var adjustment = new PurchaseSupportFiscalSnapshot(null, support.IssuerConfigurationId,
                support.FiscalNumber, support.Environment, support.QrValidationUrl, original.Seller,
                support.Authorization, original.Lines, original.SellerOriginCode,
                OriginalSupportNumber: original.OriginalSupportNumber,
                OriginalSupportCuds: original.OriginalSupportCuds,
                OriginalSupportIssuedOn: original.OriginalSupportIssuedOn,
                SellerPostalZone: original.SellerPostalZone,
                ManualCancellation: new(user.TenantId, user.BusinessId, request.VoucherId,
                    request.OccurredAt, original.ManualAcquisition!));
            return new(support.FiscalNumber, support.IssuerConfigurationId, support.Environment,
                PurchaseSupportFiscalSnapshotSerializer.Serialize(adjustment), preview.Withholding,
                FiscalDocumentTypeCodes.SupportDocumentAdjustment);
        }
        var due = request.Lines.Single(line => line.LineId == preview.PayableSourceLineId).DueDate
            ?? throw new AccountingValidationException("La obligación del soporte requiere vencimiento.");
        var supplierId = calculation.SupplierId;
        var payload = new ManualPurchaseSupportPayload(user.TenantId, user.BusinessId, request.VoucherId, supplierId,
            request.OccurredAt, due, "COP", preview.Acquisitions.Select(line => new ManualPurchaseSupportLineSnapshot(
                line.LineNumber, line.Value.SourceLineId, line.Description, line.Value.TaxExclusiveAmount,
                line.VatAmount, line.TaxRate)).ToArray(), preview.Withholding);
        var snapshot = new PurchaseSupportFiscalSnapshot(null, support.IssuerConfigurationId, support.FiscalNumber,
            support.Environment, support.QrValidationUrl, support.Seller, support.Authorization,
            preview.Acquisitions.Select(line => new PurchaseSupportLineMetadata(line.LineNumber,
                $"CUENTA-{line.AccountCode}", "999", "EA", line.TaxName, line.DianTaxCode)).ToArray(),
            SellerPostalZone: support.SellerPostalZone, ManualAcquisition: payload);
        return new(support.FiscalNumber, support.IssuerConfigurationId, support.Environment,
            PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot), preview.Withholding, FiscalDocumentTypeCodes.SupportDocument);
    }

    public async Task PersistAsync(SqlConnection connection, SqlTransaction transaction, AccountingUserIdentity user,
        ConfirmManualAccountingVoucherRequest request, PreparedManualVoucherFiscal prepared, CancellationToken token)
    {
        if (prepared.FiscalDocumentType == FiscalDocumentTypeCodes.SupportDocument)
            await SqlWithholdingRuleStore.InsertDocumentSnapshotsAsync(connection, transaction, user.BusinessId,
                [new(request.VoucherId, AccountingManualDocumentTypes.ManualVoucher, request.OccurredAt, prepared.Withholding)], token);
        await InsertPurchaseSupportSourcesAsync(connection, transaction, user.TenantId,
            [new(request.VoucherId, user.BusinessId, AccountingManualDocumentTypes.ManualVoucher,
                prepared.FiscalDocumentType, prepared.FiscalNumber, prepared.FiscalNumber,
                request.OccurredAt, prepared.IssuerId, prepared.Environment, prepared.SnapshotJson)],
            timeProvider.GetUtcNow(), token,
            FiscalDocumentStatusCodes.AwaitingManualSubmission);
    }
}
