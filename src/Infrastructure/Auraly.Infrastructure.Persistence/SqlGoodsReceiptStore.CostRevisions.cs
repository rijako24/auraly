using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.Contracts.Purchasing;
using Auraly.Commerce.Taxation.Contracts;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlGoodsReceiptStore
{
    public async Task<GoodsReceiptAcceptance?> FindCostRevisionReplayAsync(
        PurchasingUserIdentity user, string idempotencyKey, byte[] commandHash,
        ConfirmGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var replay = await LoadCostRevisionReplayAsync(connection, transaction, user,
            idempotencyKey, commandHash, request, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return replay;
    }

    public async Task<GoodsReceiptAcceptance> AcceptCostRevisionAsync(
        PurchasingUserIdentity user, string idempotencyKey, byte[] commandHash,
        ConfirmGoodsReceiptRequest request, GoodsReceiptCostCalculation calculation,
        WithholdingCalculationSnapshot withholding,
        IReadOnlyDictionary<Guid, WithholdingCalculationSnapshot> additionalWithholdings,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptCostRevisionAttemptAsync(user, idempotencyKey,
                    commandHash, request, calculation, withholding, additionalWithholdings,
                    cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<GoodsReceiptAcceptance> AcceptCostRevisionAttemptAsync(
        PurchasingUserIdentity user, string idempotencyKey, byte[] commandHash,
        ConfirmGoodsReceiptRequest request, GoodsReceiptCostCalculation calculation,
        WithholdingCalculationSnapshot withholding,
        IReadOnlyDictionary<Guid, WithholdingCalculationSnapshot> additionalWithholdings,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await LoadCostRevisionReplayAsync(connection, transaction, user,
                idempotencyKey, commandHash, request, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            GoodsReceiptDocumentPayload receipt;
            GoodsReceiptCostEditSnapshot? priorEdit = null;
            int priorRevision = 0;
            await using (var command = new SqlCommand("""
                SELECT payload.PayloadJson,posting.Status,
                  CONVERT(bit,CASE WHEN EXISTS (
                    SELECT 1 FROM purchasing.GoodsReceiptCostDocuments cost
                    LEFT JOIN dbo.AccountingPostingJobs costPosting
                      ON costPosting.SourceDocumentId=cost.CostDocumentId
                     AND costPosting.SourceDocumentType=N'GoodsReceiptCostDocument'
                     AND costPosting.BusinessId=receipt.BusinessId
                    WHERE cost.GoodsReceiptId=receipt.GoodsReceiptId
                      AND cost.SourceEntryId IS NULL
                      AND cost.RemovedByRevisionId IS NULL
                      AND (costPosting.Status IS NULL OR costPosting.Status<>N'Posted')
                  ) THEN 1 ELSE 0 END)
                FROM dbo.GoodsReceipts receipt WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.Businesses business ON business.BusinessId=receipt.BusinessId
                  AND business.TenantId=@TenantId
                JOIN dbo.DocumentProcessingPayloads payload
                  ON payload.DocumentId=receipt.GoodsReceiptId
                 AND payload.DocumentType=N'GoodsReceipt' AND payload.BusinessId=receipt.BusinessId
                LEFT JOIN dbo.AccountingPostingJobs posting
                  ON posting.SourceDocumentId=receipt.GoodsReceiptId
                 AND posting.SourceDocumentType=N'GoodsReceipt' AND posting.BusinessId=receipt.BusinessId
                WHERE receipt.GoodsReceiptId=@ReceiptId AND receipt.BusinessId=@BusinessId
                  AND receipt.Status=N'Processed';
                SELECT TOP(1) revision.RevisionNumber,revision.Status,
                  payload.PayloadJson,posting.Status
                FROM purchasing.GoodsReceiptCostRevisions revision WITH(UPDLOCK,HOLDLOCK)
                JOIN dbo.DocumentProcessingPayloads payload
                  ON payload.DocumentId=revision.RevisionId
                 AND payload.DocumentType=N'GoodsReceiptCostRevision'
                 AND payload.BusinessId=revision.BusinessId
                LEFT JOIN dbo.AccountingPostingJobs posting
                  ON posting.SourceDocumentId=revision.RevisionId
                 AND posting.SourceDocumentType=N'GoodsReceiptCostRevision'
                 AND posting.BusinessId=revision.BusinessId
                WHERE revision.GoodsReceiptId=@ReceiptId AND revision.BusinessId=@BusinessId
                ORDER BY revision.RevisionNumber DESC;
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@TenantId", user.TenantId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@ReceiptId", request.CostEditOfReceiptId!.Value);
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                    throw new PurchasingValidationException(
                        "La recepción no está procesada o no pertenece a esta sede.");
                receipt = GoodsReceiptContractSerializer.Deserialize(reader.GetString(0));
                if (reader.IsDBNull(1) || reader.GetString(1) != "Posted")
                    throw new PurchasingConflictException(
                        "La recepción aún no está contabilizada. Espera antes de editar sus costos.");
                if (reader.GetBoolean(2))
                    throw new PurchasingConflictException(
                        "Una factura asociada todavía no termina de contabilizarse. Espera antes de editar los costos.");
                await reader.NextResultAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    priorRevision = reader.GetInt32(0);
                    if (reader.GetString(1) != "Processed" || reader.IsDBNull(3) ||
                        reader.GetString(3) is not ("Posted" or "CommercialEffectsApplied"))
                        throw new PurchasingConflictException(
                            "El ajuste anterior aún no terminó de contabilizarse.");
                    priorEdit = GoodsReceiptContractSerializer.DeserializeCostRevision(reader.GetString(2)).CostEdit;
                    if (priorEdit is null)
                        throw new PurchasingConflictException(
                            "La recepción tiene un reparto anterior que requiere conciliación antes de editar costos.");
                }
            }
            if (receipt.TenantId != user.TenantId || receipt.BusinessId != user.BusinessId ||
                receipt.DocumentId != request.CostEditOfReceiptId)
                throw new InvalidOperationException("La fuente inmutable de compra no coincide con la recepción.");
            if (request.BaseCostRevisionNumber != priorRevision)
                throw new PurchasingConflictException(
                    "Los costos de la recepción cambiaron. Vuelve a abrirla antes de confirmar.");
            ValidateImmutableCostEdit(receipt, request);
            if (receipt.PurchaseEvidenceType != PurchaseEvidenceTypes.InternalReceiptVoucher ||
                receipt.CurrencyCode != "COP")
                throw new PurchasingConflictException(
                    "Este soporte requiere una corrección fiscal o cambiaria enlazada antes de modificar sus costos.");
            var previousAdditional = priorEdit?.CurrentAdditionalDocuments ??
                receipt.AdditionalCostDocuments ?? [];
            var previousAdditionalById = previousAdditional.ToDictionary(item => item.CostDocumentId);
            var requestedAdditional = request.AdditionalCostDocuments ?? [];
            var newAdditional = calculation.AdditionalDocuments.Where(item =>
                !previousAdditionalById.ContainsKey(item.Request.CostDocumentId)).ToArray();
            if (calculation.AdditionalDocuments.Count > 100 ||
                calculation.AdditionalDocuments.Sum(item => item.Lines.Count) > 500)
                throw new PurchasingValidationException(
                    "La edición admite hasta 100 facturas y 500 conceptos de costo.");
            if (newAdditional.Any(item =>
                item.Request.PurchaseEvidenceType is not (PurchaseEvidenceTypes.SupplierElectronicInvoice or
                    PurchaseEvidenceTypes.InternalReceiptVoucher) ||
                item.Request.CurrencyCode != "COP"))
                throw new PurchasingConflictException(
                    "La factura nueva debe ser electrónica del proveedor o comprobante interno en COP. Los demás soportes requieren su trámite fiscal antes de asociarlos.");
            var calculatedAdditionalById = calculation.AdditionalDocuments
                .ToDictionary(item => item.Request.CostDocumentId);
            var retainedAdditional = previousAdditional.Where(item =>
                calculatedAdditionalById.ContainsKey(item.CostDocumentId)).ToArray();
            var removedAdditional = previousAdditional.Where(item =>
                !calculatedAdditionalById.ContainsKey(item.CostDocumentId)).ToArray();
            foreach (var old in retainedAdditional)
            {
                var calculated = calculatedAdditionalById[old.CostDocumentId];
                var source = calculated.Request;
                if (source.SupplierId != old.SupplierId || source.PartySiteId != old.PartySiteId ||
                    source.PurchaseEvidenceType != old.PurchaseEvidenceType ||
                    source.DocumentNumber != old.DocumentNumber || source.IssuedAt != old.IssuedAt ||
                    source.CreatesPayable != old.CreatesPayable || source.DueDate != old.DueDate ||
                    source.CurrencyCode != old.CurrencyCode || source.ExchangeRate != old.ExchangeRate ||
                    source.ExchangeRateDate != old.ExchangeRateDate ||
                    source.ExchangeRateSource != old.ExchangeRateSource ||
                    calculated.NetAmount != old.NetAmount || calculated.TaxAmount != old.TaxAmount ||
                    calculated.FunctionalGrandTotal != old.FunctionalGrandTotal ||
                    calculated.Lines.Count != old.Lines.Count ||
                    calculated.Lines.Any(line => !old.Lines.Any(previous =>
                        previous.LineNumber == line.LineNumber &&
                        previous.CostKind == line.CostKind &&
                        previous.Description == line.Description &&
                        previous.Amount == line.Amount && previous.TaxAmount == line.TaxAmount &&
                        previous.TaxTreatment == line.TaxTreatment &&
                        previous.CostTreatment == line.CostTreatment &&
                        previous.AllocationMethod == line.AllocationMethod &&
                        previous.FunctionalAmount == line.FunctionalAmount)))
                    throw new PurchasingValidationException(
                        "La factura ya contabilizada conserva sus datos originales. Solo puedes quitar su asociación con el costo.");
            }
            var previousReferences = priorEdit?.CurrentReferencedDocuments ??
                receipt.ReferencedCostDocuments ?? [];
            var requestedReferences = request.ReferencedCostDocuments ?? [];
            var previousById = previousReferences.ToDictionary(item => item.AssociationId);
            var currentIds = requestedReferences.Select(item => item.AssociationId).ToHashSet();
            var retained = previousReferences.Where(item => currentIds.Contains(item.AssociationId)).ToArray();
            var removed = previousReferences.Where(item => !currentIds.Contains(item.AssociationId)).ToArray();
            var newCalculated = (calculation.ReferencedDocuments ?? [])
                .Where(item => !previousById.ContainsKey(item.Request.AssociationId)).ToArray();
            var calculatedById = (calculation.ReferencedDocuments ?? [])
                .ToDictionary(item => item.Request.AssociationId);
            foreach (var old in retained)
            {
                var selected = calculatedById[old.AssociationId].Request;
                if (selected.EntryId != old.EntryId || selected.Mode != old.Mode ||
                    selected.Lines.Count != old.Lines.Count || selected.Lines.Any(line =>
                        !old.Lines.Any(previous => previous.EntryLineNumber == line.EntryLineNumber &&
                            previous.FunctionalAmount == line.FunctionalAmount &&
                            previous.AllocationMethod == line.AllocationMethod &&
                            (line.AllocationMethod != PurchaseCostAllocationMethods.Manual ||
                             (line.ManualAllocations ?? []).Select(value =>
                                (value.ReceiptLineNumber, value.FunctionalAmount)).OrderBy(value => value.ReceiptLineNumber)
                                .SequenceEqual(previous.Allocations.Select(value =>
                                    (value.ReceiptLineNumber, value.FunctionalAmount))
                                    .OrderBy(value => value.ReceiptLineNumber))))))
                    throw new PurchasingValidationException(
                        "Para cambiar un reparto existente, quítalo y agrega de nuevo el comprobante.");
            }
            var frozen = retained.SelectMany(item => item.Lines)
                .SelectMany(item => item.Allocations)
                .Select(item => (item.ReceiptLineNumber, item.FunctionalAmount))
                .Concat(retainedAdditional.SelectMany(item => item.Lines)
                    .SelectMany(item => item.Allocations)
                    .Select(item => (item.ReceiptLineNumber, item.FunctionalAmount)))
                .GroupBy(item => item.ReceiptLineNumber)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.FunctionalAmount));
            var recalculated = retained.Select(item => calculatedById[item.AssociationId])
                .SelectMany(item => item.Allocations)
                .Select(item => (item.ReceiptLineNumber, item.FunctionalAmount))
                .Concat(retainedAdditional.Select(item => calculatedAdditionalById[item.CostDocumentId])
                    .SelectMany(item => item.Lines).SelectMany(item => item.Allocations)
                    .Select(item => (item.ReceiptLineNumber, item.FunctionalAmount)))
                .GroupBy(item => item.ReceiptLineNumber)
                .ToDictionary(group => group.Key, group => group.Sum(item => item.FunctionalAmount));
            calculation = calculation with { ReceiptLines = calculation.ReceiptLines.Select(line =>
            {
                var correction = frozen.GetValueOrDefault(line.LineNumber) -
                    recalculated.GetValueOrDefault(line.LineNumber);
                return correction == 0m ? line : line with
                {
                    AllocatedLandedCostAmount = line.AllocatedLandedCostAmount + correction,
                    RecognizedInventoryCostAmount = line.RecognizedInventoryCostAmount + correction
                };
            }).ToArray() };
            var previousLines = priorEdit?.CurrentLines ?? receipt.Lines;
            var previousTotal = priorEdit?.CurrentFunctionalGrandTotal ?? receipt.FunctionalGrandTotal;
            var previousWithholding = priorEdit?.CurrentWithholding ?? receipt.Withholding;
            var previousDiscount = priorEdit?.CurrentFinancialDiscount ?? receipt.FinancialDiscount;
            var previousPayable = previousWithholding.NetAmount -
                (previousDiscount?.AnticipatedInCost == true ? previousDiscount.FunctionalAmount : 0m);
            var currentPayable = withholding.NetAmount -
                (calculation.FinancialDiscount?.AnticipatedInCost == true
                    ? calculation.FinancialDiscount.FunctionalAmount : 0m);
            if (currentPayable <= 0m || previousPayable <= 0m)
                throw new PurchasingConflictException(
                    "La corrección requiere una obligación vigente con valor positivo.");
            if (previousLines.Count != calculation.ReceiptLines.Count ||
                previousLines.All(line => calculation.ReceiptLines.Any(current =>
                    current.LineNumber == line.LineNumber &&
                    current.RecognizedInventoryCostAmount == line.RecognizedInventoryCostAmount)) &&
                previousTotal == calculation.FunctionalGrandTotal &&
                previousPayable == currentPayable && removed.Length == 0 &&
                removedAdditional.Length == 0 && newAdditional.Length == 0 &&
                newCalculated.Length == 0)
                throw new PurchasingValidationException("No hay cambios de costo que confirmar.");
            await RequireCostEditDraftAsync(connection, transaction, user, request, cancellationToken);
            var payableChange = currentPayable - previousPayable;
            if (payableChange < 0m)
            {
                await using var availability = new SqlCommand("""
                    SELECT available.AvailableAmount,receipt.ExchangeRate
                    FROM dbo.Payables payable WITH(UPDLOCK,HOLDLOCK)
                    JOIN dbo.GoodsReceipts receipt ON receipt.GoodsReceiptId=payable.SourceDocumentId
                      AND receipt.BusinessId=payable.BusinessId
                    CROSS APPLY accounting.ObligationAvailability(payable.BusinessId,
                      N'Payable',payable.PayableId,payable.OutstandingAmount,NULL) available
                    WHERE payable.BusinessId=@BusinessId AND payable.SourceDocumentId=@ReceiptId
                      AND payable.SourceDocumentType=N'GoodsReceipt' AND payable.SourceLineId IS NULL;
                    """, connection, transaction);
                availability.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                availability.Parameters.AddWithValue("@ReceiptId", receipt.DocumentId);
                await using var reader = await availability.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken) ||
                    reader.GetDecimal(0) < decimal.Round(-payableChange / reader.GetDecimal(1),
                        4, MidpointRounding.AwayFromZero))
                    throw new PurchasingConflictException(
                        "El proveedor ya recibió abonos o tiene pagos pendientes; el nuevo valor excede el saldo disponible para corregir.");
            }
            var now = timeProvider.GetUtcNow();
            var releasedByThisRevision = removed.SelectMany(document => document.Lines.Select(line =>
                    (document.EntryId, line.EntryLineNumber, line.FunctionalAmount)))
                .GroupBy(line => (line.EntryId, line.EntryLineNumber))
                .ToDictionary(group => group.Key, group => group.Sum(line => line.FunctionalAmount));
            var referencedDocuments = await ResolveReferencedCostsAsync(connection,
                transaction, user, newCalculated, now, cancellationToken, releasedByThisRevision)
                ?? [];
            var newAdditionalSnapshots = newAdditional.Select(item =>
                CreateCostDocumentSnapshot(item,
                    additionalWithholdings[item.Request.CostDocumentId]) with
                {
                    CreatesPayable = true,
                    DueDate = item.Request.DueDate ?? item.Request.IssuedAt
                }).ToArray();
            var currentAdditional = retainedAdditional.Concat(newAdditionalSnapshots).ToArray();
            var currentReferences = retained.Concat(referencedDocuments).ToArray();
            var revisionNumber = await NextCostRevisionNumberAndRequireOriginsAsync(connection,
                transaction, user.BusinessId, receipt.DocumentId,
                previousLines.Where(line => calculation.ReceiptLines.Any(current =>
                    current.LineNumber == line.LineNumber &&
                    current.RecognizedInventoryCostAmount != line.RecognizedInventoryCostAmount))
                    .Select(line => line.LineNumber).ToArray(),
                cancellationToken);
            var payload = new GoodsReceiptCostRevisionPayload(user.TenantId, user.BusinessId,
                request.DocumentId, receipt.DocumentId, revisionNumber, receipt.WarehouseId,
                receipt.SupplierId, user.UserId, receipt.DocumentNumber, now,
                receipt.Lines, referencedDocuments, new(previousLines, calculation.ReceiptLines,
                    previousTotal, calculation.FunctionalGrandTotal, previousWithholding,
                    withholding, previousPayable, currentPayable, previousDiscount,
                    calculation.FinancialDiscount, previousAdditional, currentAdditional,
                    previousReferences, currentReferences), newAdditionalSnapshots);
            var jobId = ids.NewId();
            var sequence = await AllocateProcessingSequenceAsync(connection, transaction,
                user.BusinessId, now, cancellationToken);
            await using (var command = new SqlCommand("""
                INSERT purchasing.GoodsReceiptCostRevisions
                  (RevisionId,GoodsReceiptId,BusinessId,RevisionNumber,IdempotencyKey,
                   RequestHash,Status,AcceptedByUserId,AcceptedAt,ProcessedAt,PendingPayableDecrease)
                VALUES(@RevisionId,@ReceiptId,@BusinessId,@RevisionNumber,@IdempotencyKey,
                  @RequestHash,@Status,@UserId,@Now,@ProcessedAt,@PendingPayableDecrease);
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@RevisionId", request.DocumentId);
                command.Parameters.AddWithValue("@ReceiptId", receipt.DocumentId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@RevisionNumber", revisionNumber);
                command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
                command.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = commandHash;
                command.Parameters.AddWithValue("@Status", "Accepted");
                command.Parameters.AddWithValue("@UserId", user.UserId);
                command.Parameters.AddWithValue("@Now", now);
                command.Parameters.AddWithValue("@ProcessedAt", DBNull.Value);
                command.Parameters.AddWithValue("@PendingPayableDecrease",
                    decimal.Round(Math.Max(0m, -payableChange / receipt.ExchangeRate), 4,
                        MidpointRounding.AwayFromZero));
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertCostDocumentsAsync(connection, transaction, receipt.DocumentId,
                newAdditionalSnapshots, referencedDocuments, now, cancellationToken, request.DocumentId);
            var payloadJson = GoodsReceiptContractSerializer.SerializeCostRevision(payload);
            await InsertJobAsync(connection, transaction, user.BusinessId, request.DocumentId,
                PurchasingDocumentTypes.GoodsReceiptCostRevision, jobId, sequence, payloadJson,
                SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)), now, cancellationToken);
            await DeleteDraftIfPresentAsync(connection, transaction, user.BusinessId,
                request.CostEditDraftId!.Value, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(receipt.DocumentId, jobId, receipt.DocumentNumber, "Accepted", sequence, false);
        }
        catch (Exception exception)
        {
            if (transaction.Connection?.State == ConnectionState.Open)
                await transaction.RollbackAsync(CancellationToken.None);
            if (exception is SqlException { Number: 2601 or 2627 })
                throw new PurchasingConflictException(
                    "La revisión, el comprobante o la clave de confirmación ya están en uso.", exception);
            throw;
        }
    }

    private static void ValidateImmutableCostEdit(GoodsReceiptDocumentPayload receipt,
        ConfirmGoodsReceiptRequest request)
    {
        var source = receipt.Lines.OrderBy(line => line.LineNumber).ToArray();
        var submitted = request.Lines.OrderBy(line => line.LineNumber).ToArray();
        if (receipt.WarehouseId != request.WarehouseId || receipt.SupplierId != request.SupplierId ||
            receipt.PartySiteId != request.PartySiteId || receipt.PurchaseOrderId != request.PurchaseOrderId ||
            receipt.PurchaseEvidenceType != request.PurchaseEvidenceType ||
            receipt.SupplierInvoiceNumber != request.SupplierInvoiceNumber ||
            (receipt.SupplierInvoiceDate is null ? (DateOnly?)null :
                DateOnly.FromDateTime(receipt.SupplierInvoiceDate.Value.Date)) !=
            (request.SupplierInvoiceDate is null ? (DateOnly?)null :
                DateOnly.FromDateTime(request.SupplierInvoiceDate.Value.Date)) ||
            receipt.ReceivedAt != request.ReceivedAt || receipt.CreatesPayable != request.CreatesPayable ||
            receipt.CurrencyCode != request.CurrencyCode ||
            (receipt.DueDate is null ? (DateOnly?)null :
                DateOnly.FromDateTime(receipt.DueDate.Value.Date)) !=
            (request.DueDate is null ? (DateOnly?)null :
                DateOnly.FromDateTime(request.DueDate.Value.Date)) ||
            receipt.ExchangeRate != request.ExchangeRate ||
            receipt.ExchangeRateDate != request.ExchangeRateDate ||
            receipt.ExchangeRateSource != request.ExchangeRateSource ||
            source.Length != submitted.Length || source.Where((line, index) =>
                line.LineNumber != submitted[index].LineNumber ||
                line.ProductId != submitted[index].ProductId ||
                line.Quantity != submitted[index].Quantity ||
                line.PresentationQuantity != submitted[index].PresentationQuantity ||
                line.UnitsPerPresentation != submitted[index].UnitsPerPresentation ||
                line.UnitGrossWeightKg != submitted[index].UnitGrossWeightKg ||
                line.TotalGrossWeightKg != submitted[index].TotalGrossWeightKg ||
                line.TotalVolumeM3 != submitted[index].TotalVolumeM3 ||
                line.PurchaseOrderLineId != submitted[index].PurchaseOrderLineId ||
                line.TaxCode != submitted[index].TaxCode ||
                line.TaxRate != submitted[index].TaxRate ||
                line.TaxTreatment != submitted[index].TaxTreatment).Any())
            throw new PurchasingValidationException(
                "La edición solo permite cambiar costos; la entrada física y sus datos fiscales permanecen intactos.");
    }

    private static async Task RequireCostEditDraftAsync(SqlConnection connection,
        SqlTransaction transaction, PurchasingUserIdentity user,
        ConfirmGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DraftConcurrencyToken))
            throw new PurchasingValidationException("Guarda el borrador de costos antes de confirmarlo.");
        await using var command = new SqlCommand("""
            SELECT d.RowVersion FROM dbo.GoodsReceiptDrafts d WITH(UPDLOCK,HOLDLOCK)
            WHERE d.GoodsReceiptDraftId=@DraftId AND d.BusinessId=@BusinessId
              AND d.CostEditOfReceiptId=@ReceiptId
              AND d.BaseCostRevisionNumber=@RevisionNumber;
            """, connection, transaction);
        command.Parameters.AddWithValue("@DraftId", request.CostEditDraftId!.Value);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@ReceiptId", request.CostEditOfReceiptId!.Value);
        command.Parameters.AddWithValue("@RevisionNumber", request.BaseCostRevisionNumber!.Value);
        var token = await command.ExecuteScalarAsync(cancellationToken) as byte[];
        if (token is null || !token.AsSpan().SequenceEqual(
            SqlGoodsReceiptWorkspaceStore.ParseToken(request.DraftConcurrencyToken)))
            throw new PurchasingConflictException(
                "El borrador de costos cambió o ya fue confirmado. Recárgalo antes de continuar.");
        var draft = await SqlGoodsReceiptWorkspaceStore.LoadDraftAsync(connection,
            transaction, user.BusinessId, request.CostEditDraftId.Value, cancellationToken);
        var submittedLines = request.Lines.OrderBy(line => line.LineNumber).ToArray();
        var savedLines = draft?.Lines.OrderBy(line => line.LineNumber).ToArray();
        if (draft is null || savedLines is null || savedLines.Length != submittedLines.Length ||
            savedLines.Where((line, index) =>
                line.LineNumber != submittedLines[index].LineNumber ||
                line.UnitCost != submittedLines[index].UnitCost ||
                line.DiscountAmount != submittedLines[index].DiscountAmount ||
                line.CommercialDiscountPercent != submittedLines[index].CommercialDiscountPercent ||
                line.TotalGrossWeightKg != submittedLines[index].TotalGrossWeightKg ||
                line.TotalVolumeM3 != submittedLines[index].TotalVolumeM3).Any() ||
            draft.FinancialDiscount != request.FinancialDiscount ||
            draft.WithholdingConceptCode != request.WithholdingConceptCode ||
            JsonSerializer.Serialize(draft.AdditionalCostDocuments ?? []) !=
                JsonSerializer.Serialize(request.AdditionalCostDocuments ?? []) ||
            JsonSerializer.Serialize(draft.ReferencedCostDocuments ?? []) !=
                JsonSerializer.Serialize(request.ReferencedCostDocuments ?? []) ||
            JsonSerializer.Serialize(draft.WithholdingAdjustments ?? []) !=
                JsonSerializer.Serialize(request.WithholdingAdjustments ?? []))
            throw new PurchasingConflictException(
                "La confirmación no coincide con los costos guardados en el borrador.");
    }

    private static async Task<GoodsReceiptAcceptance?> LoadCostRevisionReplayAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        string idempotencyKey, byte[] commandHash, ConfirmGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT revision.RevisionId,revision.GoodsReceiptId,revision.RevisionNumber,
              revision.RequestHash,revision.Status,job.JobId,job.ProcessingSequence,
              receipt.DocumentNumber
            FROM purchasing.GoodsReceiptCostRevisions revision WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business ON business.BusinessId=revision.BusinessId
              AND business.TenantId=@TenantId
            LEFT JOIN dbo.DocumentProcessingJobs job ON job.DocumentId=revision.RevisionId
              AND job.DocumentType=N'GoodsReceiptCostRevision' AND job.BusinessId=revision.BusinessId
            JOIN dbo.GoodsReceipts receipt ON receipt.GoodsReceiptId=revision.GoodsReceiptId
              AND receipt.BusinessId=revision.BusinessId
            WHERE revision.BusinessId=@BusinessId AND
              (revision.RevisionId=@RevisionId OR revision.IdempotencyKey=@IdempotencyKey);
            """, connection, transaction);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@RevisionId", request.DocumentId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var storedId = reader.GetGuid(0);
        var receiptId = reader.GetGuid(1);
        var number = reader.GetInt32(2);
        var storedHash = reader.GetFieldValue<byte[]>(3);
        var status = reader.GetString(4);
        var jobId = reader.IsDBNull(5) ? (Guid?)null : reader.GetGuid(5);
        var sequence = reader.IsDBNull(6) ? 0L : reader.GetInt64(6);
        var receiptNumber = reader.GetString(7);
        if (await reader.ReadAsync(cancellationToken) || storedId != request.DocumentId ||
            receiptId != request.CostEditOfReceiptId || !storedHash.AsSpan().SequenceEqual(commandHash))
            throw new PurchasingConflictException(
                "Esta revisión o clave ya se usó con otros datos. Recarga la recepción.");
        if (status == "Accepted" && jobId is null)
            throw new InvalidOperationException("La revisión aceptada no conserva su trabajo documental.");
        if (jobId is null || sequence <= 0 || number <= 0)
            throw new InvalidOperationException("La revisión no conserva su trabajo documental.");
        return new(receiptId, jobId.Value, receiptNumber, status, sequence, true);
    }

    // Fail before reserving a source voucher when a legacy receipt cannot support
    // the adjustment. The document handler rechecks the live quantities when it posts.
    private static async Task<int> NextCostRevisionNumberAndRequireOriginsAsync(SqlConnection connection,
        SqlTransaction transaction, Guid businessId, Guid receiptId,
        IReadOnlyList<int> affectedLines, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            DECLARE @Affected TABLE(LineNumber int PRIMARY KEY);
            INSERT @Affected SELECT [value] FROM OPENJSON(@LinesJson) WITH ([value] int '$');
            SELECT CONVERT(bit,CASE WHEN EXISTS(
              SELECT 1 FROM @Affected affected
              JOIN dbo.GoodsReceiptLines line ON line.GoodsReceiptId=@ReceiptId
                AND line.LineNumber=affected.LineNumber
              JOIN dbo.Businesses business ON business.BusinessId=@BusinessId
              JOIN dbo.Products product ON product.ProductId=line.ProductId
                AND product.TenantId=business.TenantId
              LEFT JOIN dbo.ProductLinks link ON link.ChildProductId=product.ProductId
                AND link.TenantId=business.TenantId AND link.SharesInventory=1 AND link.IsActive=1
              JOIN dbo.Products inventoryProduct
                ON inventoryProduct.ProductId=COALESCE(link.ParentProductId,product.ProductId)
                AND inventoryProduct.TenantId=business.TenantId
              LEFT JOIN dbo.InventoryMovements movement
                ON movement.DocumentId=@ReceiptId AND movement.DocumentType=N'GoodsReceipt'
               AND movement.BusinessId=@BusinessId AND movement.LineNumber=line.LineNumber
               AND movement.MovementType=N'GoodsReceipt'
               AND movement.ProductId=inventoryProduct.ProductId
              WHERE inventoryProduct.ManageStock=1 AND
                (movement.InventoryMovementId IS NULL OR NOT EXISTS(
                  SELECT 1 FROM inventory.MovementOrigins origin
                  WHERE origin.InventoryMovementId=movement.InventoryMovementId))
            ) OR EXISTS(
              SELECT 1 FROM @Affected affected
              JOIN dbo.InventoryMovements movement ON movement.DocumentId=@ReceiptId
                AND movement.DocumentType=N'GoodsReceipt' AND movement.BusinessId=@BusinessId
                AND movement.LineNumber=affected.LineNumber AND movement.MovementType=N'GoodsReceipt'
              JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
                AND balance.ProductId=movement.ProductId AND balance.QuantityOnHand>0
              WHERE balance.OriginContextId IS NULL OR
                balance.OriginSnapshotSequence<>balance.LastProcessingSequence
            ) THEN 1 ELSE 0 END);
            SELECT COALESCE(MAX(RevisionNumber),0)+1
            FROM purchasing.GoodsReceiptCostRevisions WITH(UPDLOCK,HOLDLOCK)
            WHERE GoodsReceiptId=@ReceiptId;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@ReceiptId", receiptId);
        command.Parameters.Add("@LinesJson", SqlDbType.NVarChar, -1).Value =
            JsonSerializer.Serialize(affectedLines);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("La revisión no devolvió su estado de origen.");
        var lacksOrigins = reader.GetBoolean(0);
        await reader.NextResultAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("La revisión no devolvió su consecutivo.");
        var number = reader.GetInt32(0);
        if (lacksOrigins)
            throw new PurchasingConflictException(
                "Esta recepción no conserva el origen de inventario necesario para actualizar su costo con seguridad.");
        return number;
    }
}
