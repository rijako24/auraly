using Auraly.Commerce.Taxation.Contracts;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identity;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Purchasing;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Application.Fiscal;
using Auraly.Domain.Purchasing;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlGoodsReceiptStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IGoodsReceiptStore
{
    public async Task<GoodsReceiptAcceptance> AcceptAsync(
        PurchasingUserIdentity user,
        string idempotencyKey,
        ConfirmGoodsReceiptRequest request,
        GoodsReceiptCalculation calculation,
        GoodsReceiptCostCalculation costCalculation,
        WithholdingCalculationSnapshot withholding,
        IReadOnlyDictionary<Guid, WithholdingCalculationSnapshot> additionalWithholdings,
        CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(request, calculation, costCalculation, withholding, additionalWithholdings);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await AcceptAttemptAsync(
                    user, idempotencyKey, request, calculation, costCalculation,
                    withholding, additionalWithholdings, requestHash, cancellationToken);
            }
            catch (SqlException exception) when (exception.Number == 1205 && attempt < 4)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(25 * attempt), timeProvider, cancellationToken);
            }
        }
    }

    private async Task<GoodsReceiptAcceptance> AcceptAttemptAsync(
        PurchasingUserIdentity user,
        string idempotencyKey,
        ConfirmGoodsReceiptRequest request,
        GoodsReceiptCalculation calculation,
        GoodsReceiptCostCalculation costCalculation,
        WithholdingCalculationSnapshot withholding,
        IReadOnlyDictionary<Guid, WithholdingCalculationSnapshot> additionalWithholdings,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var replay = await TryLoadReplayAsync(
                connection, transaction, user.BusinessId, request.DocumentId,
                idempotencyKey, requestHash, cancellationToken);
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            await ValidateScopeAsync(connection, transaction, user, request, cancellationToken);
            var overReceiptLines = await ValidatePurchaseOrderAsync(
                connection, transaction, user, request, cancellationToken);
            var number = await AllocateNumberAsync(connection, transaction, user.BusinessId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var supportRequests = new List<(Guid DocumentId, Guid SupplierId, DateTimeOffset IssuedAt)>();
            if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.BuyerElectronicSupportDocument)
                supportRequests.Add((request.DocumentId, request.SupplierId, request.SupplierInvoiceDate!.Value));
            supportRequests.AddRange(costCalculation.AdditionalDocuments
                .Where(item => item.Request.PurchaseEvidenceType == PurchaseEvidenceTypes.BuyerElectronicSupportDocument)
                .Select(item => (item.Request.CostDocumentId, item.Request.SupplierId, item.Request.IssuedAt)));
            if (supportRequests.Count > 100)
                throw new PurchasingValidationException(
                    "Una recepción admite hasta 100 documentos soporte electrónicos.");
            if (supportRequests.Count > 0 &&
                !await SqlDianDocumentQuota.TryReserveManyAsync(connection, transaction,
                    user.BusinessId, supportRequests.Select(item => item.DocumentId).ToArray(),
                    "SupportDocument", now, cancellationToken))
                throw new PurchasingValidationException(
                    "No hay cupo de documentos DIAN. Compra un paquete antes de seleccionar documento soporte electrónico.");
            var supports = supportRequests.Count == 0
                ? new Dictionary<Guid, SupportFiscalAllocation>()
                : (await AllocateSupportFiscalBatchAsync(connection, transaction, user.BusinessId,
                    supportRequests.Select(item => (item.SupplierId, item.IssuedAt)).ToArray(),
                    now, cancellationToken))
                    .Select((allocation, index) => (supportRequests[index].DocumentId, allocation))
                    .ToDictionary(item => item.DocumentId, item => item.allocation);
            supports.TryGetValue(request.DocumentId, out var support);
            var sequence = await AllocateProcessingSequenceAsync(
                connection, transaction, user.BusinessId, now, cancellationToken);
            var additionalDocuments = costCalculation.AdditionalDocuments.Select(document =>
                new GoodsReceiptCostDocumentSnapshot(
                    document.Request.CostDocumentId, document.Request.SupplierId,
                    document.Request.PurchaseEvidenceType,
                    supports.TryGetValue(document.Request.CostDocumentId, out var costSupport)
                        ? costSupport.FiscalNumber : document.Request.DocumentNumber,
                    document.Request.IssuedAt, document.Request.CreatesPayable,
                    document.Request.DueDate, document.Request.CurrencyCode,
                    document.Request.ExchangeRate, document.Request.ExchangeRateDate!.Value,
                    document.Request.ExchangeRateSource, document.NetAmount, document.TaxAmount,
                    document.GrandTotal, document.FunctionalNetAmount, document.FunctionalTaxAmount,
                    document.FunctionalGrandTotal,
                    additionalWithholdings[document.Request.CostDocumentId], document.Lines)).ToArray();
            var payload = new GoodsReceiptDocumentPayload(
                user.TenantId,
                user.BusinessId,
                request.DocumentId,
                request.WarehouseId,
                request.SupplierId,
                user.UserId,
                number.FullNumber,
                number.SeriesId,
                number.Prefix,
                number.SeriesCode,
                number.Consecutive,
                request.SupplierInvoiceNumber,
                request.SupplierInvoiceDate,
                request.ReceivedAt,
                request.CreatesPayable,
                request.DueDate,
                request.CurrencyCode,
                request.Notes,
                calculation.NetAmount,
                calculation.TaxAmount,
                calculation.GrandTotal,
                costCalculation.ReceiptLines.Select(line =>
                {
                    var source = request.Lines.Single(item => item.LineNumber == line.LineNumber);
                    return line with { OverReceiptAuthorized =
                        source.PurchaseOrderLineId is Guid lineId && overReceiptLines.Contains(lineId) };
                }).ToArray(),
                withholding,
                PurchaseEvidenceType: request.PurchaseEvidenceType,
                PurchaseOrderId: request.PurchaseOrderId,
                ExchangeRate: costCalculation.ExchangeRate,
                ExchangeRateDate: costCalculation.ExchangeRateDate,
                ExchangeRateSource: costCalculation.ExchangeRateSource,
                FunctionalNetAmount: costCalculation.FunctionalNetAmount,
                FunctionalTaxAmount: costCalculation.FunctionalTaxAmount,
                FunctionalGrandTotal: costCalculation.FunctionalGrandTotal,
                AdditionalCostDocuments: additionalDocuments);
            var payloadJson = GoodsReceiptContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));

            var movementId = ids.NewId();
            await InsertReceiptAsync(
                connection, transaction, user, request, calculation, costCalculation, number,
                support, idempotencyKey, requestHash, now, cancellationToken);
            await InsertLinesAsync(connection, transaction, request.DocumentId, request.Lines,
                costCalculation, cancellationToken);
            await InsertCostDocumentsAsync(connection, transaction, request.DocumentId,
                additionalDocuments, now, cancellationToken);
            await InsertJobAsync(
                connection, transaction, user.BusinessId, request.DocumentId, movementId,
                sequence, payloadJson, payloadHash, now, cancellationToken);
            if (support is not null)
                await InsertSupportFiscalAsync(connection, transaction, payload, support,
                    request.Lines, now, cancellationToken);
            await InsertCostSupportFiscalBatchAsync(connection, transaction, payload,
                additionalDocuments.Where(item => item.PurchaseEvidenceType ==
                    PurchaseEvidenceTypes.BuyerElectronicSupportDocument).ToArray(),
                supports, now, cancellationToken);
            await DeleteDraftIfPresentAsync(
                connection, transaction, user.BusinessId, request.DocumentId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new GoodsReceiptAcceptance(
                request.DocumentId, movementId, number.FullNumber, "Accepted", sequence, false);
        }
        catch (PurchasingConflictException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
        catch (SqlException exception) when (exception.Number == 1205)
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            throw;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new PurchasingConflictException(
                "El número de recepción, la factura del proveedor o la clave de confirmación ya está en uso.");
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task<GoodsReceiptAcceptance?> TryLoadReplayAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid documentId,
        string idempotencyKey,
        byte[] requestHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT r.GoodsReceiptId,r.DocumentNumber,r.Status,r.PayloadHash,j.ProcessingSequence,j.JobId
            FROM dbo.GoodsReceipts r WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j
              ON j.DocumentId=r.GoodsReceiptId AND j.DocumentType=N'GoodsReceipt'
            WHERE r.BusinessId=@BusinessId
              AND (r.GoodsReceiptId=@DocumentId OR r.IdempotencyKey=@IdempotencyKey);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(3).AsSpan().SequenceEqual(requestHash))
            throw new PurchasingConflictException("Esta recepción ya se intentó confirmar con datos diferentes. Recárgala antes de continuar.");
        return new GoodsReceiptAcceptance(
            reader.GetGuid(0), reader.GetGuid(5), reader.GetString(1), reader.GetString(2), reader.GetInt64(4), true);
    }

    private static async Task ValidateScopeAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        PurchasingUserIdentity user,
        ConfirmGoodsReceiptRequest request,
        CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51100,'La sede no pertenece a la empresa autenticada.',1;
            IF NOT EXISTS (SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1 AND IsSystem=0 AND UseForGoodsReceipts=1)
              THROW 51101,'Selecciona una bodega activa para recibir mercancía.',1;
            IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51102,'El proveedor no está activo en esta sede.',1;
            IF EXISTS (
              SELECT 1 FROM OPENJSON(@CostDocumentsJson)
              WITH (SupplierId uniqueidentifier '$.SupplierId') x
              LEFT JOIN dbo.Suppliers s ON s.SupplierId=x.SupplierId AND s.BusinessId=@BusinessId AND s.IsActive=1
              WHERE s.SupplierId IS NULL)
              THROW 51105,'Un proveedor de costo adicional no está activo en esta sede.',1;
            IF @CurrencyCode<>N'COP' AND NOT EXISTS (
              SELECT 1 FROM reference.Options
              WHERE CatalogCode=N'exchange-rate-source' AND Code=@ExchangeRateSource AND IsActive=1)
              THROW 51107,'La fuente de la tasa de cambio no está activa.',1;
            IF EXISTS (
              SELECT 1 FROM OPENJSON(@CostDocumentsJson)
              WITH (CurrencyCode nvarchar(3) '$.CurrencyCode',ExchangeRateSource nvarchar(64) '$.ExchangeRateSource') x
              LEFT JOIN reference.Options optionValue
                ON optionValue.CatalogCode=N'exchange-rate-source'
               AND optionValue.Code=x.ExchangeRateSource AND optionValue.IsActive=1
              WHERE UPPER(x.CurrencyCode)<>N'COP' AND optionValue.OptionId IS NULL)
              THROW 51108,'Un documento adicional usa una fuente de tasa de cambio que no está activa.',1;
            IF EXISTS (
              SELECT 1 FROM OPENJSON(@CostDocumentsJson)
              WITH (SupplierId uniqueidentifier '$.SupplierId',PurchaseEvidenceType nvarchar(64) '$.PurchaseEvidenceType') x
              INNER JOIN dbo.Suppliers supplier
                ON supplier.SupplierId=x.SupplierId AND supplier.BusinessId=@BusinessId AND supplier.IsActive=1
              WHERE NOT (
                x.PurchaseEvidenceType IN (N'ForeignCommercialInvoice',N'ImportDeclaration')
                OR supplier.PurchaseEvidencePolicy IS NULL
                OR supplier.PurchaseEvidencePolicy=N'InternalReceiptVoucher' AND x.PurchaseEvidenceType=N'InternalReceiptVoucher'
                OR supplier.PurchaseEvidencePolicy=N'SupplierElectronicInvoice' AND x.PurchaseEvidenceType IN (N'SupplierElectronicInvoice',N'InternalReceiptVoucher')
                OR supplier.PurchaseEvidencePolicy=N'BuyerElectronicSupportDocument' AND x.PurchaseEvidenceType IN (N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher')))
              THROW 51109,'El tipo de soporte de un documento adicional no está permitido para su proveedor.',1;
            DECLARE @RepeatedCostNumber nvarchar(80), @RepeatedSupplier nvarchar(200), @PreviousReceipt nvarchar(80);
            SELECT TOP (1) @RepeatedCostNumber=x.DocumentNumber,
              @RepeatedSupplier=s.Name,@PreviousReceipt=r.DocumentNumber
            FROM OPENJSON(@CostDocumentsJson)
              WITH (SupplierId uniqueidentifier '$.SupplierId',DocumentNumber nvarchar(80) '$.DocumentNumber',
                PurchaseEvidenceType nvarchar(64) '$.PurchaseEvidenceType') x
            INNER JOIN purchasing.GoodsReceiptCostDocuments d
              ON d.SupplierId=x.SupplierId AND d.DocumentNumber=x.DocumentNumber
            INNER JOIN dbo.GoodsReceipts r ON r.GoodsReceiptId=d.GoodsReceiptId AND r.BusinessId=@BusinessId
            INNER JOIN dbo.Suppliers s ON s.SupplierId=x.SupplierId AND s.BusinessId=@BusinessId
            WHERE x.PurchaseEvidenceType<>N'BuyerElectronicSupportDocument';
            IF @RepeatedCostNumber IS NOT NULL
            BEGIN
              DECLARE @RepeatedCostMessage nvarchar(2048)=CONCAT(N'La factura ',@RepeatedCostNumber,
                N' de ',@RepeatedSupplier,N' ya está registrada en la recepción ',@PreviousReceipt,
                N'. Revisa el número o abre esa recepción antes de confirmar.');
              THROW 51106,@RepeatedCostMessage,1;
            END;
            SELECT TOP (1) @RepeatedCostNumber=x.DocumentNumber,@RepeatedSupplier=s.Name
            FROM OPENJSON(@CostDocumentsJson)
              WITH (SupplierId uniqueidentifier '$.SupplierId',DocumentNumber nvarchar(80) '$.DocumentNumber',
                PurchaseEvidenceType nvarchar(64) '$.PurchaseEvidenceType') x
            INNER JOIN dbo.Suppliers s ON s.SupplierId=x.SupplierId AND s.BusinessId=@BusinessId
            WHERE x.PurchaseEvidenceType<>N'BuyerElectronicSupportDocument'
            GROUP BY x.SupplierId,x.DocumentNumber,s.Name
            HAVING COUNT(*)>1;
            IF @RepeatedCostNumber IS NOT NULL
            BEGIN
              SET @RepeatedCostMessage=CONCAT(N'La factura ',@RepeatedCostNumber,N' de ',
                @RepeatedSupplier,N' se agregó más de una vez a esta recepción.');
              THROW 51106,@RepeatedCostMessage,1;
            END;
            IF NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers
              WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1
                AND (
                  @PurchaseEvidenceType=N'ForeignCommercialInvoice'
                  OR PurchaseEvidencePolicy IS NULL
                  OR PurchaseEvidencePolicy=N'InternalReceiptVoucher' AND @PurchaseEvidenceType=N'InternalReceiptVoucher'
                  OR PurchaseEvidencePolicy=N'SupplierElectronicInvoice' AND @PurchaseEvidenceType IN (N'SupplierElectronicInvoice',N'InternalReceiptVoucher')
                  OR PurchaseEvidencePolicy=N'BuyerElectronicSupportDocument' AND @PurchaseEvidenceType IN (N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher')))
              THROW 51104,'El tipo de soporte seleccionado no está permitido para este proveedor.',1;
            IF EXISTS (
              SELECT x.ProductId
              FROM OPENJSON(@ProductsJson)
                WITH (ProductId UNIQUEIDENTIFIER '$') x
              LEFT JOIN dbo.Products p ON p.ProductId=x.ProductId AND p.IsActive=1
                AND (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                     OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId))
              LEFT JOIN dbo.SupplierProducts sp ON sp.ProductId=x.ProductId AND sp.SupplierId=@SupplierId AND sp.BusinessId=@BusinessId AND sp.IsActive=1
              WHERE p.ProductId IS NULL OR sp.SupplierProductId IS NULL)
              THROW 51103,'Cada producto debe estar activo y asociado con el proveedor seleccionado.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.AddWithValue("@PurchaseEvidenceType", request.PurchaseEvidenceType);
        command.Parameters.AddWithValue("@CurrencyCode", request.CurrencyCode);
        command.Parameters.AddWithValue("@ExchangeRateSource", request.ExchangeRateSource);
        command.Parameters.AddWithValue("@CreatesPayable", request.CreatesPayable);
        command.Parameters.AddWithValue("@IssueDate", request.SupplierInvoiceDate!.Value);
        command.Parameters.AddWithValue("@DueDate", (object?)request.DueDate ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "@ProductsJson",
            JsonSerializer.Serialize(request.Lines.Select(line => line.ProductId).Distinct()));
        command.Parameters.AddWithValue("@CostDocumentsJson", JsonSerializer.Serialize(
            request.AdditionalCostDocuments ?? []));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is >= 51100 and <= 51109)
        {
            throw new PurchasingValidationException(exception.Message);
        }
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        CancellationToken cancellationToken)
    {
        const string ensureSql = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.DocumentSeries WITH (UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND DocumentType=N'GoodsReceipt' AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries
                (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,NULL,N'GoodsReceipt',N'EMC',N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        await using (var ensure = new SqlCommand(ensureSql, connection, transaction))
        {
            ensure.Parameters.AddWithValue("@BusinessId", businessId);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }        const string sql = """
            SELECT TOP (1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,
                   ds.RangeStart,ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH (UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH (UPDLOCK,HOLDLOCK)
              ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=N'GoodsReceipt'
              AND ds.DeviceId IS NULL AND ds.IsActive=1
            ORDER BY ds.DocumentSeriesId;
            """;
        Guid seriesId;
        string prefix;
        string seriesCode;
        byte padding;
        long rangeEnd;
        long consecutive;
        await using (var select = new SqlCommand(sql, connection, transaction))
        {
            select.Parameters.AddWithValue("@BusinessId", businessId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                throw new PurchasingValidationException("La serie de entradas de mercancía no está activa para esta sede.");
            seriesId = reader.GetGuid(0);
            prefix = reader.GetString(1);
            seriesCode = reader.GetString(2);
            padding = reader.GetByte(3);
            rangeEnd = reader.GetInt64(5);
            consecutive = reader.GetInt64(6);
        }
        if (consecutive > rangeEnd) throw new PurchasingValidationException("La numeración de entradas de mercancía se agotó.");
        const string update = """
            IF EXISTS (SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@SeriesId)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@SeriesId;
            ELSE
              INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt) VALUES(@SeriesId,@Next,@Now);
            """;
        await using var command = new SqlCommand(update, connection, transaction);
        command.Parameters.AddWithValue("@SeriesId", seriesId);
        command.Parameters.AddWithValue("@Next", consecutive + 1);
        command.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(
            seriesId, AuralyDocumentTypes.GoodsReceipt, prefix, seriesCode, consecutive, padding);
    }

    private static async Task<long> AllocateProcessingSequenceAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS (SELECT 1 FROM dbo.BusinessProcessingCursors WITH (UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt) VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH (UPDLOCK,HOLDLOCK)
            SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
            OUTPUT inserted.LastAssignedSequence
            WHERE BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task InsertReceiptAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        ConfirmGoodsReceiptRequest request, GoodsReceiptCalculation calculation,
        GoodsReceiptCostCalculation costCalculation,
        AuralyDocumentNumberAssignment number, SupportFiscalAllocation? support,
        string idempotencyKey, byte[] requestHash,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.GoodsReceipts
              (GoodsReceiptId,BusinessId,WarehouseId,SupplierId,DocumentSeriesId,DocumentNumber,
               DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,
               PurchaseOrderId,PurchaseEvidenceType,SupportFiscalSeriesId,SupportFiscalAuthorizationId,SupportFiscalNumber,
               SupplierInvoiceNumber,SupplierInvoiceDate,ReceivedAt,CreatesPayable,DueDate,CurrencyCode,
               Notes,NetAmount,TaxAmount,GrandTotal,ExchangeRate,ExchangeRateDate,ExchangeRateSource,
               FunctionalNetAmount,FunctionalTaxAmount,FunctionalGrandTotal,Status,ConfirmedByUserId,AcceptedAt)
            VALUES
              (@Id,@BusinessId,@WarehouseId,@SupplierId,@SeriesId,@Number,@Prefix,@SeriesCode,@Consecutive,
               @IdempotencyKey,@PayloadHash,@PurchaseOrderId,@PurchaseEvidenceType,@SupportFiscalSeriesId,@SupportFiscalAuthorizationId,@SupportFiscalNumber,
               @SupplierInvoiceNumber,@SupplierInvoiceDate,@ReceivedAt,
               @CreatesPayable,@DueDate,@CurrencyCode,@Notes,@NetAmount,@TaxAmount,@GrandTotal,
               @ExchangeRate,@ExchangeRateDate,@ExchangeRateSource,@FunctionalNet,@FunctionalTax,@FunctionalTotal,
               N'Accepted',@UserId,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", request.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@PurchaseOrderId", (object?)request.PurchaseOrderId ?? DBNull.Value);
        command.Parameters.AddWithValue("@PurchaseEvidenceType", request.PurchaseEvidenceType);
        command.Parameters.AddWithValue("@SupportFiscalSeriesId", (object?)support?.SeriesId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupportFiscalAuthorizationId", (object?)support?.AuthorizationId ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupportFiscalNumber", (object?)support?.FiscalNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierInvoiceNumber", (object?)request.SupplierInvoiceNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("@SupplierInvoiceDate", (object?)request.SupplierInvoiceDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@ReceivedAt", request.ReceivedAt);
        command.Parameters.AddWithValue("@CreatesPayable", request.CreatesPayable);
        command.Parameters.AddWithValue("@DueDate", (object?)request.DueDate ?? DBNull.Value);
        command.Parameters.AddWithValue("@CurrencyCode", request.CurrencyCode);
        command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
        AddDecimal(command, "@NetAmount", calculation.NetAmount, 19, 4);
        AddDecimal(command, "@TaxAmount", calculation.TaxAmount, 19, 4);
        AddDecimal(command, "@GrandTotal", calculation.GrandTotal, 19, 4);
        AddDecimal(command, "@ExchangeRate", costCalculation.ExchangeRate, 19, 8);
        command.Parameters.AddWithValue("@ExchangeRateDate", costCalculation.ExchangeRateDate.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@ExchangeRateSource", costCalculation.ExchangeRateSource);
        AddDecimal(command, "@FunctionalNet", costCalculation.FunctionalNetAmount, 19, 4);
        AddDecimal(command, "@FunctionalTax", costCalculation.FunctionalTaxAmount, 19, 4);
        AddDecimal(command, "@FunctionalTotal", costCalculation.FunctionalGrandTotal, 19, 4);
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertLinesAsync(SqlConnection connection, SqlTransaction transaction, Guid documentId,
        IReadOnlyCollection<GoodsReceiptLineRequest> requestLines, GoodsReceiptCostCalculation calculation,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.GoodsReceiptLines
              (GoodsReceiptId,LineNumber,ProductId,DescriptionSnapshot,Quantity,UnitCost,DiscountAmount,
               TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation,
               PurchaseOrderLineId,OverReceiptReason,OverReceiptAuthorized,TotalGrossWeightKg,TotalVolumeM3,
               FunctionalNetAmount,FunctionalTaxAmount,FunctionalLineTotal,AllocatedLandedCostAmount,RecognizedInventoryCostAmount)
            VALUES(@Id,@Line,@ProductId,@Description,@Quantity,@UnitCost,@Discount,@TaxCode,
                   @TaxRate,@TaxTreatment,@Net,@Tax,@Total,@PresentationName,@PresentationQuantity,@UnitsPerPresentation,
                   @PurchaseOrderLineId,@OverReceiptReason,@OverReceiptAuthorized,@Weight,@Volume,
                   @FunctionalNet,@FunctionalTax,@FunctionalTotal,@LandedCost,@RecognizedCost);
            """;
        foreach (var line in calculation.ReceiptLines)
        {
            var source = requestLines.Single(item => item.LineNumber == line.LineNumber);
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Id", documentId);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            command.Parameters.AddWithValue("@ProductId", line.ProductId);
            command.Parameters.AddWithValue("@Description", line.Description);
            AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
            AddDecimal(command, "@UnitCost", line.UnitCost, 19, 6);
            AddDecimal(command, "@Discount", line.DiscountAmount, 19, 4);
            command.Parameters.AddWithValue("@TaxCode", line.TaxCode);
            AddDecimal(command, "@TaxRate", line.TaxRate, 9, 6);
            command.Parameters.AddWithValue("@TaxTreatment", line.TaxTreatment);
            AddDecimal(command, "@Net", line.NetAmount, 19, 4);
            AddDecimal(command, "@Tax", line.TaxAmount, 19, 4);
            AddDecimal(command, "@Total", line.LineTotal, 19, 4);
            command.Parameters.AddWithValue("@PresentationName", source.PresentationName);
            AddDecimal(command, "@PresentationQuantity", source.PresentationQuantity, 19, 6);
            AddDecimal(command, "@UnitsPerPresentation", source.UnitsPerPresentation, 19, 6);
            command.Parameters.AddWithValue("@PurchaseOrderLineId", (object?)source.PurchaseOrderLineId ?? DBNull.Value);
            command.Parameters.AddWithValue("@OverReceiptReason", (object?)source.OverReceiptReason ?? DBNull.Value);
            command.Parameters.AddWithValue("@OverReceiptAuthorized", source.PurchaseOrderLineId is not null &&
                source.OverReceiptReason is not null);
            AddNullableDecimal(command, "@Weight", line.TotalGrossWeightKg, 19, 6);
            AddNullableDecimal(command, "@Volume", line.TotalVolumeM3, 19, 6);
            AddDecimal(command, "@FunctionalNet", line.FunctionalNetAmount, 19, 4);
            AddDecimal(command, "@FunctionalTax", line.FunctionalTaxAmount, 19, 4);
            AddDecimal(command, "@FunctionalTotal", line.FunctionalLineTotal, 19, 4);
            AddDecimal(command, "@LandedCost", line.AllocatedLandedCostAmount, 19, 4);
            AddDecimal(command, "@RecognizedCost", line.RecognizedInventoryCostAmount, 19, 4);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task InsertCostDocumentsAsync(
        SqlConnection connection, SqlTransaction transaction, Guid goodsReceiptId,
        IReadOnlyCollection<GoodsReceiptCostDocumentSnapshot> documents,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var document in documents)
        {
            await using (var command = new SqlCommand("""
                INSERT purchasing.GoodsReceiptCostDocuments
                  (CostDocumentId,GoodsReceiptId,SupplierId,PurchaseEvidenceType,DocumentNumber,
                   IssuedAt,CreatesPayable,DueDate,CurrencyCode,ExchangeRate,ExchangeRateDate,
                   ExchangeRateSource,NetAmount,TaxAmount,GrandTotal,FunctionalNetAmount,
                   FunctionalTaxAmount,FunctionalGrandTotal,CreatedAt)
                VALUES(@Id,@ReceiptId,@SupplierId,@Evidence,@Number,@IssuedAt,@CreatesPayable,@DueDate,
                   @Currency,@ExchangeRate,@ExchangeRateDate,@ExchangeRateSource,@Net,@Tax,@Total,
                   @FunctionalNet,@FunctionalTax,@FunctionalTotal,@Now);
                """, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", document.CostDocumentId);
                command.Parameters.AddWithValue("@ReceiptId", goodsReceiptId);
                command.Parameters.AddWithValue("@SupplierId", document.SupplierId);
                command.Parameters.AddWithValue("@Evidence", document.PurchaseEvidenceType);
                command.Parameters.AddWithValue("@Number", document.DocumentNumber);
                command.Parameters.AddWithValue("@IssuedAt", document.IssuedAt);
                command.Parameters.AddWithValue("@CreatesPayable", document.CreatesPayable);
                command.Parameters.AddWithValue("@DueDate", (object?)document.DueDate ?? DBNull.Value);
                command.Parameters.AddWithValue("@Currency", document.CurrencyCode);
                AddDecimal(command, "@ExchangeRate", document.ExchangeRate, 19, 8);
                command.Parameters.AddWithValue("@ExchangeRateDate", document.ExchangeRateDate.ToDateTime(TimeOnly.MinValue));
                command.Parameters.AddWithValue("@ExchangeRateSource", document.ExchangeRateSource);
                AddDecimal(command, "@Net", document.NetAmount, 19, 4);
                AddDecimal(command, "@Tax", document.TaxAmount, 19, 4);
                AddDecimal(command, "@Total", document.GrandTotal, 19, 4);
                AddDecimal(command, "@FunctionalNet", document.FunctionalNetAmount, 19, 4);
                AddDecimal(command, "@FunctionalTax", document.FunctionalTaxAmount, 19, 4);
                AddDecimal(command, "@FunctionalTotal", document.FunctionalGrandTotal, 19, 4);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            foreach (var line in document.Lines)
            {
                await using (var command = new SqlCommand("""
                    INSERT purchasing.GoodsReceiptCostLines
                      (CostDocumentId,LineNumber,CostKind,DescriptionSnapshot,Amount,TaxableBaseAmount,
                       TaxCode,TaxRate,TaxAmount,TaxTreatment,CostTreatment,AllocationMethod,
                       FunctionalAmount,FunctionalTaxableBaseAmount,FunctionalTaxAmount,FunctionalDocumentAmount)
                    VALUES(@DocumentId,@Line,@Kind,@Description,@Amount,@TaxableBase,@TaxCode,@TaxRate,@Tax,
                       @TaxTreatment,@CostTreatment,@AllocationMethod,@FunctionalAmount,@FunctionalTaxableBase,
                       @FunctionalTax,@FunctionalDocumentAmount);
                    """, connection, transaction))
                {
                    command.Parameters.AddWithValue("@DocumentId", document.CostDocumentId);
                    command.Parameters.AddWithValue("@Line", line.LineNumber);
                    command.Parameters.AddWithValue("@Kind", line.CostKind);
                    command.Parameters.AddWithValue("@Description", line.Description);
                    AddDecimal(command, "@Amount", line.Amount, 19, 4);
                    AddDecimal(command, "@TaxableBase", line.TaxableBaseAmount, 19, 4);
                    command.Parameters.AddWithValue("@TaxCode", line.TaxCode);
                    AddDecimal(command, "@TaxRate", line.TaxRate, 9, 6);
                    AddDecimal(command, "@Tax", line.TaxAmount, 19, 4);
                    command.Parameters.AddWithValue("@TaxTreatment", line.TaxTreatment);
                    command.Parameters.AddWithValue("@CostTreatment", line.CostTreatment);
                    command.Parameters.AddWithValue("@AllocationMethod", line.AllocationMethod);
                    AddDecimal(command, "@FunctionalAmount", line.FunctionalAmount, 19, 4);
                    AddDecimal(command, "@FunctionalTaxableBase", line.FunctionalTaxableBaseAmount, 19, 4);
                    AddDecimal(command, "@FunctionalTax", line.FunctionalTaxAmount, 19, 4);
                    AddDecimal(command, "@FunctionalDocumentAmount", line.FunctionalDocumentAmount, 19, 4);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                foreach (var allocation in line.Allocations)
                {
                    await using var command = new SqlCommand("""
                        INSERT purchasing.GoodsReceiptCostAllocations
                          (CostDocumentId,CostLineNumber,GoodsReceiptId,ReceiptLineNumber,
                           AllocationMethod,AllocationFactor,FunctionalAmount)
                        VALUES(@DocumentId,@CostLine,@ReceiptId,@ReceiptLine,@Method,@Factor,@Amount);
                        """, connection, transaction);
                    command.Parameters.AddWithValue("@DocumentId", document.CostDocumentId);
                    command.Parameters.AddWithValue("@CostLine", line.LineNumber);
                    command.Parameters.AddWithValue("@ReceiptId", goodsReceiptId);
                    command.Parameters.AddWithValue("@ReceiptLine", allocation.ReceiptLineNumber);
                    command.Parameters.AddWithValue("@Method", allocation.AllocationMethod);
                    AddDecimal(command, "@Factor", allocation.Factor, 19, 12);
                    AddDecimal(command, "@Amount", allocation.FunctionalAmount, 19, 4);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }
    }

    private static async Task<HashSet<Guid>> ValidatePurchaseOrderAsync(
        SqlConnection connection, SqlTransaction transaction, PurchasingUserIdentity user,
        ConfirmGoodsReceiptRequest request, CancellationToken cancellationToken)
    {
        var result = new HashSet<Guid>();
        if (request.PurchaseOrderId is null)
        {
            if (request.Lines.Any(line => line.PurchaseOrderLineId is not null || line.OverReceiptReason is not null))
                throw new PurchasingValidationException("Las líneas de la recepción no pueden referir una orden de compra sin seleccionarla.");
            return result;
        }

        await using var command = new SqlCommand(
            "purchasing.ReceiptOrderValidate", connection, transaction)
        {
            CommandType = CommandType.StoredProcedure
        };
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@PurchaseOrderId", request.PurchaseOrderId.Value);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.AddWithValue("@CanAuthorizeOverReceipt",
            user.Permissions.Contains(PurchasingPermissionCodes.AuthorizeOverReceipt));
        command.Parameters.AddWithValue("@LinesJson", JsonSerializer.Serialize(request.Lines.Select(line => new
        {
            line.PurchaseOrderLineId,
            line.ProductId,
            line.Quantity,
            line.OverReceiptReason
        })));
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetGuid(0));
        }
        catch (SqlException exception) when (exception.Number == 51211)
        {
            throw new PurchasingForbiddenException(exception.Message);
        }
        catch (SqlException exception) when (exception.Number == 51209)
        {
            throw new PurchasingValidationException(exception.Message);
        }
        return result;
    }

    internal static async Task<SupportFiscalAllocation> AllocateSupportFiscalAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid supplierId,
        DateTimeOffset issuedAt, DateTimeOffset now, CancellationToken cancellationToken) =>
        (await AllocateSupportFiscalBatchAsync(connection, transaction, businessId, [supplierId],
            issuedAt, now, cancellationToken))[0];

    internal static async Task<IReadOnlyList<SupportFiscalAllocation>> AllocateSupportFiscalBatchAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, IReadOnlyList<Guid> supplierIds,
        DateTimeOffset issuedAt, DateTimeOffset now, CancellationToken cancellationToken) =>
        await AllocateSupportFiscalBatchAsync(connection, transaction, businessId,
            supplierIds.Select(id => (SupplierId: id, IssuedAt: issuedAt)).ToArray(), now, cancellationToken);

    internal static async Task<IReadOnlyList<SupportFiscalAllocation>> AllocateSupportFiscalBatchAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        IReadOnlyList<(Guid SupplierId, DateTimeOffset IssuedAt)> requests,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (requests.Count is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(requests));
        const string sql = """
            SELECT requested.[key],fs.SeriesId,fs.FiscalAuthorizationId,fs.Prefix,fs.RangeStart,fs.RangeEnd,
                   a.AuthorizationNumber,a.ValidFrom,a.ValidUntil,a.Environment,a.QrValidationUrl,
                   c.FiscalIssuerConfigurationId,
                   p.PartyType,p.Identification,p.VerificationDigit,p.IdentificationTypeCode,
                   COALESCE(p.LegalName,p.DisplayName),COALESCE(p.DisplayName,p.LegalName),
                   country.Code,country.Name,division.Code,division.Name,city.Code,city.Name,site.AddressLine,
                   email.Value,phone.Value,site.PostalCode
            FROM OPENJSON(@Requests) requested
            CROSS APPLY OPENJSON(requested.value) WITH(SupplierId uniqueidentifier,IssuedAt datetimeoffset) item
            CROSS APPLY (SELECT TOP(1) value.* FROM dbo.FiscalSeries value WITH(UPDLOCK,HOLDLOCK)
              JOIN dbo.FiscalAuthorizations auth ON auth.FiscalAuthorizationId=value.FiscalAuthorizationId
              WHERE value.BusinessId=@BusinessId AND value.DocumentType=N'SupportDocument'
                AND value.EmitterKind=N'Server' AND value.DeviceId IS NULL AND value.IsActive=1
                AND auth.IsActive=1 AND auth.ValidFrom<=CONVERT(date,item.IssuedAt) AND auth.ValidUntil>=CONVERT(date,item.IssuedAt)
                AND EXISTS(SELECT 1 FROM dbo.FiscalIssuerConfigurations issuer WHERE issuer.BusinessId=value.BusinessId
                  AND issuer.IsActive=1 AND issuer.Environment=auth.Environment
                  AND issuer.ValidFrom<=item.IssuedAt AND (issuer.ValidTo IS NULL OR issuer.ValidTo>item.IssuedAt))
              ORDER BY auth.ValidUntil DESC,value.SeriesId) fs
            JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=fs.FiscalAuthorizationId
            JOIN dbo.FiscalIssuerConfigurations c ON c.BusinessId=fs.BusinessId AND c.IsActive=1
              AND c.Environment=a.Environment
              AND c.ValidFrom<=item.IssuedAt AND (c.ValidTo IS NULL OR c.ValidTo>item.IssuedAt)
            JOIN dbo.Suppliers s ON s.SupplierId=item.SupplierId AND s.BusinessId=fs.BusinessId AND s.IsActive=1
            JOIN dbo.Parties p ON p.PartyId=s.PartyId AND p.IsActive=1
            LEFT JOIN dbo.PartySites site ON site.PartyId=p.PartyId
              AND site.IsActive=1 AND site.IsPrimary=1
            LEFT JOIN dbo.Countries country ON country.CountryId=site.CountryId
            LEFT JOIN dbo.AdministrativeDivisions division ON division.AdministrativeDivisionId=site.AdministrativeDivisionId
            LEFT JOIN dbo.Cities city ON city.CityId=site.CityId
            OUTER APPLY(SELECT TOP(1) value.Value FROM dbo.PartyContacts value
              WHERE value.PartyId=p.PartyId AND value.ContactType=N'Email' AND value.IsActive=1
              ORDER BY value.IsPrimary DESC,value.CreatedAt) email
            OUTER APPLY(SELECT TOP(1) value.Value FROM dbo.PartyContacts value
              WHERE value.PartyId=p.PartyId AND value.ContactType=N'Phone' AND value.IsActive=1
              ORDER BY value.IsPrimary DESC,value.CreatedAt) phone
            WHERE fs.BusinessId=@BusinessId AND fs.DocumentType=N'SupportDocument'
              AND fs.EmitterKind=N'Server' AND fs.DeviceId IS NULL AND fs.IsActive=1
              AND a.IsActive=1 AND a.ValidFrom<=CONVERT(date,item.IssuedAt) AND a.ValidUntil>=CONVERT(date,item.IssuedAt)
            ORDER BY CONVERT(int,requested.[key]);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Requests", JsonSerializer.Serialize(
            requests.Select(item => new { item.SupplierId, item.IssuedAt })));
        var allocations = new List<SupportFiscalAllocation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        int? failedIndex = null;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Convert.ToInt32(reader.GetString(0)) != allocations.Count)
            {
                failedIndex = allocations.Count;
                break;
            }
            var seriesId = reader.GetGuid(1);
            var authorizationId = reader.GetGuid(2);
            var prefix = reader.GetString(3);
            var rangeStart = reader.GetInt64(4);
            var rangeEnd = reader.GetInt64(5);
            var authorization = new PosSaleUblAuthorizationContract(reader.GetString(6),
                DateOnly.FromDateTime(reader.GetDateTime(7)), DateOnly.FromDateTime(reader.GetDateTime(8)),
                prefix, rangeStart, rangeEnd);
            var environment = reader.GetByte(9);
            var qrUrl = reader.GetString(10);
            var issuerId = reader.GetGuid(11);
            if (reader.IsDBNull(13))
                throw new PurchasingValidationException("El proveedor necesita identificación para generar el documento soporte.");
            if (reader.IsDBNull(15) || Enumerable.Range(18, 7).Any(reader.IsDBNull))
                throw new PurchasingValidationException(
                    "El proveedor necesita tipo de identificación y una sede principal activa con país, departamento, ciudad y dirección. Completa estos datos en Terceros > Proveedores.");
            var sellerIdentification = reader.GetString(13);
            if (!ColombianNit.TryCalculateVerificationDigit(
                    sellerIdentification, out var sellerVerificationDigit))
                throw new PurchasingValidationException(
                    "El proveedor residente necesita un NIT numérico válido para generar el documento soporte.");
            var postalZone = reader.IsDBNull(27) ? string.Empty : reader.GetString(27).Trim();
            if (postalZone.Length != 6 || postalZone.Any(character => character is < '0' or > '9'))
                throw new PurchasingValidationException(
                    "El código postal de la sede principal activa del proveedor debe tener seis dígitos. Corrígelo en Terceros > Proveedores > Ubicación principal para generar el documento soporte.");
            var seller = new PosSaleUblPartyContract(
                sellerIdentification, sellerVerificationDigit.ToString(), "31",
                reader.GetString(12) == "Organization" ? "1" : "2",
                reader.GetString(16), reader.GetString(17), "R-99-PN", "ZZ", "No aplica",
                new PosSaleUblAddressContract(
                    reader.GetString(22), reader.GetString(23), reader.GetString(21),
                    reader.GetString(20), reader.GetString(24), reader.GetString(18),
                    reader.GetString(19)),
                reader.IsDBNull(25) ? null : reader.GetString(25),
                reader.IsDBNull(26) ? null : reader.GetString(26));
            allocations.Add(new(seriesId, authorizationId, issuerId, string.Empty,
                environment, qrUrl, authorization, seller, postalZone));
        }
        await reader.CloseAsync();
        if (failedIndex is not null || allocations.Count != requests.Count)
        {
            var failed = requests[Math.Min(failedIndex ?? allocations.Count, requests.Count - 1)];
            throw new PurchasingValidationException(await ExplainSupportFiscalAllocationFailureAsync(
                connection, transaction, businessId, [failed.SupplierId],
                failed.IssuedAt, cancellationToken));
        }
        var groups = allocations.GroupBy(item => item.SeriesId)
            .Select(group => new
            {
                SeriesId = group.Key,
                RangeStart = group.First().Authorization.RangeStart,
                RangeEnd = group.First().Authorization.RangeEnd,
                Count = group.Count()
            }).ToArray();
        await using var cursor = new SqlCommand("""
            DECLARE @Groups TABLE(SeriesId uniqueidentifier PRIMARY KEY,RangeStart bigint,RangeEnd bigint,DocumentCount int);
            INSERT @Groups(SeriesId,RangeStart,RangeEnd,DocumentCount)
            SELECT SeriesId,RangeStart,RangeEnd,DocumentCount
            FROM OPENJSON(@GroupsJson) WITH(
              SeriesId uniqueidentifier,RangeStart bigint,RangeEnd bigint,DocumentCount int);
            INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt)
            SELECT requested.SeriesId,requested.RangeStart,@Now FROM @Groups requested
            WHERE NOT EXISTS(SELECT 1 FROM dbo.FiscalSeriesCursors existing WITH(UPDLOCK,HOLDLOCK)
              WHERE existing.SeriesId=requested.SeriesId);
            IF EXISTS(SELECT 1 FROM @Groups requested
              JOIN dbo.FiscalSeriesCursors currentCursor WITH(UPDLOCK,HOLDLOCK)
                ON currentCursor.SeriesId=requested.SeriesId
              WHERE currentCursor.NextConsecutive>requested.RangeEnd-requested.DocumentCount+1)
              THROW 51734,N'La numeración DIAN de documento soporte está agotada.',1;
            DECLARE @Assigned TABLE(SeriesId uniqueidentifier PRIMARY KEY,FirstConsecutive bigint);
            UPDATE currentCursor SET NextConsecutive=currentCursor.NextConsecutive+requested.DocumentCount,
              UpdatedAt=@Now
            OUTPUT inserted.SeriesId,deleted.NextConsecutive
              INTO @Assigned(SeriesId,FirstConsecutive)
            FROM dbo.FiscalSeriesCursors currentCursor WITH(UPDLOCK,HOLDLOCK)
            JOIN @Groups requested ON requested.SeriesId=currentCursor.SeriesId;
            IF (SELECT COUNT(*) FROM @Assigned)<>(SELECT COUNT(*) FROM @Groups)
              THROW 51735,N'No se pudo reservar la numeración DIAN completa.',1;
            SELECT SeriesId,FirstConsecutive FROM @Assigned;
            """, connection, transaction);
        cursor.Parameters.AddWithValue("@GroupsJson", JsonSerializer.Serialize(groups.Select(group => new
        {
            group.SeriesId, group.RangeStart, group.RangeEnd, DocumentCount = group.Count
        })));
        cursor.Parameters.AddWithValue("@Now", now);
        var nextBySeries = new Dictionary<Guid, long>();
        try
        {
            await using var assigned = await cursor.ExecuteReaderAsync(cancellationToken);
            while (await assigned.ReadAsync(cancellationToken))
                nextBySeries.Add(assigned.GetGuid(0), assigned.GetInt64(1));
        }
        catch (SqlException exception) when (exception.Number is 51734 or 51735)
        {
            throw new PurchasingValidationException(exception.Message);
        }
        return allocations.Select(item =>
        {
            var consecutive = nextBySeries[item.SeriesId];
            nextBySeries[item.SeriesId] = consecutive + 1;
            return item with { FiscalNumber = item.Authorization.Prefix + consecutive };
        }).ToArray();
    }

    private static async Task<string> ExplainSupportFiscalAllocationFailureAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId,
        IReadOnlyList<Guid> supplierIds, DateTimeOffset issuedAt, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("""
            SELECT
              SUM(CASE WHEN supplier.SupplierId IS NULL THEN 1 ELSE 0 END),
              SUM(CASE WHEN supplier.SupplierId IS NOT NULL AND party.PartyId IS NULL THEN 1 ELSE 0 END),
              (SELECT COUNT_BIG(*) FROM dbo.FiscalSeries series
               JOIN dbo.FiscalAuthorizations authorization
                 ON authorization.FiscalAuthorizationId=series.FiscalAuthorizationId
               WHERE series.BusinessId=@BusinessId AND series.DocumentType=N'SupportDocument'
                 AND series.EmitterKind=N'Server' AND series.DeviceId IS NULL AND series.IsActive=1
                 AND authorization.IsActive=1
                 AND authorization.ValidFrom<=CONVERT(date,@IssuedAt)
                 AND authorization.ValidUntil>=CONVERT(date,@IssuedAt)),
              (SELECT COUNT_BIG(*) FROM dbo.FiscalSeries series
               JOIN dbo.FiscalAuthorizations authorization
                 ON authorization.FiscalAuthorizationId=series.FiscalAuthorizationId
               JOIN dbo.FiscalIssuerConfigurations issuer
                 ON issuer.BusinessId=series.BusinessId AND issuer.IsActive=1
                 AND issuer.Environment=authorization.Environment
                 AND issuer.ValidFrom<=@IssuedAt
                 AND (issuer.ValidTo IS NULL OR issuer.ValidTo>@IssuedAt)
               WHERE series.BusinessId=@BusinessId AND series.DocumentType=N'SupportDocument'
                 AND series.EmitterKind=N'Server' AND series.DeviceId IS NULL AND series.IsActive=1
                 AND authorization.IsActive=1
                 AND authorization.ValidFrom<=CONVERT(date,@IssuedAt)
                 AND authorization.ValidUntil>=CONVERT(date,@IssuedAt))
            FROM OPENJSON(@SupplierIds) requested
            LEFT JOIN dbo.Suppliers supplier
              ON supplier.SupplierId=TRY_CONVERT(uniqueidentifier,requested.value)
             AND supplier.BusinessId=@BusinessId AND supplier.IsActive=1
            LEFT JOIN dbo.Parties party ON party.PartyId=supplier.PartyId AND party.IsActive=1;
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SupplierIds", JsonSerializer.Serialize(supplierIds));
        command.Parameters.AddWithValue("@IssuedAt", issuedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("No fue posible consultar el diagnóstico del documento soporte.");
        if (reader.GetInt32(0) > 0)
            return "El proveedor no está activo en esta sede. Revísalo en Terceros > Proveedores antes de registrar el documento soporte.";
        if (reader.GetInt32(1) > 0)
            return "La identidad del proveedor está inactiva. Actívala en Terceros > Proveedores antes de registrar el documento soporte.";
        if (reader.GetInt64(2) == 0)
            return "No hay una resolución DIAN de documento soporte vigente para la fecha de emisión. Revísala en Configuración fiscal.";
        if (reader.GetInt64(3) == 0)
            return "La configuración fiscal del emisor no está activa para el ambiente de la resolución de documento soporte. Revísala en Configuración fiscal.";
        return "Hay más de una numeración DIAN o configuración fiscal aplicable al documento soporte. Revisa sus vigencias en Configuración fiscal.";
    }

    private static async Task InsertSupportFiscalAsync(
        SqlConnection connection, SqlTransaction transaction, GoodsReceiptDocumentPayload receipt,
        SupportFiscalAllocation support, IReadOnlyCollection<GoodsReceiptLineRequest> requestLines,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string productsSql = """
            SELECT ProductId,COALESCE(NULLIF(ProductCode,N''),CONVERT(nvarchar(36),ProductId)),COALESCE(BaseUnitCode,N'EA')
            FROM dbo.Products WHERE TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId) AND ProductId IN
              (SELECT value FROM OPENJSON(@Ids) WITH (value uniqueidentifier '$'));
            SELECT Code,DianTaxCode,Rate
            FROM dbo.TaxProfiles
            WHERE BusinessId=@BusinessId AND IsActive=1
              AND (Code IN (SELECT value FROM OPENJSON(@TaxCodes) WITH (value nvarchar(32) '$'))
                OR DianTaxCode IN (SELECT value FROM OPENJSON(@TaxCodes) WITH (value nvarchar(32) '$')));
            """;
        var metadata = new Dictionary<Guid, (string Code, string Unit)>();
        var taxProfiles = new Dictionary<string, (string DianCode, decimal Rate)>(
            StringComparer.OrdinalIgnoreCase);
        var taxProfilesByDianCode = new Dictionary<(string DianCode, decimal Rate), string>();
        await using (var products = new SqlCommand(productsSql, connection, transaction))
        {
            products.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
            products.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(requestLines.Select(x => x.ProductId)));
            products.Parameters.AddWithValue("@TaxCodes", JsonSerializer.Serialize(
                requestLines.Select(line => line.TaxCode).Distinct(StringComparer.OrdinalIgnoreCase)));
            await using var reader = await products.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                metadata[reader.GetGuid(0)] = (reader.GetString(1), reader.GetString(2));
            await reader.NextResultAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var code = reader.GetString(0);
                var dianCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                var rate = reader.GetDecimal(2);
                taxProfiles[code] = (dianCode, rate);
                if (!string.IsNullOrWhiteSpace(dianCode))
                    taxProfilesByDianCode[(dianCode.ToUpperInvariant(), rate)] = dianCode;
            }
        }
        var snapshot = new PurchaseSupportFiscalSnapshot(receipt, support.IssuerConfigurationId,
            support.FiscalNumber, support.Environment, support.QrValidationUrl, support.Seller,
            support.Authorization, receipt.Lines.Select(line =>
            {
                var product = metadata[line.ProductId];
                var dianTaxCode = ResolveDianTaxCode(
                    line, taxProfiles, taxProfilesByDianCode);
                return new PurchaseSupportLineMetadata(line.LineNumber, product.Code, "999",
                    product.Unit, PosSaleFiscalMappings.TaxName(dianTaxCode), dianTaxCode);
            }).ToArray(), SellerPostalZone: support.SellerPostalZone);
        const string sql = """
            INSERT dbo.FiscalDocuments(DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
              AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,N'GoodsReceipt',N'SupportDocument',@AuralyNumber,@FiscalNumber,
              N'CUDS',NULL,@IssuedAt,@Status,@Now,@Now);
            INSERT fiscal.PurchaseSupportFiscalSnapshots(DocumentId,SnapshotJson,Environment,CreatedAt)
            VALUES(@DocumentId,@SnapshotJson,@Environment,@Now);
            INSERT dbo.FiscalDocumentProcesses(DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,
              AttemptCount,NextAttemptAt,CreatedAt,UpdatedAt)
            VALUES(@DocumentId,@BusinessId,@IssuerId,@Status,0,@Now,@Now,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", receipt.DocumentId);
        command.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
        command.Parameters.AddWithValue("@AuralyNumber", receipt.DocumentNumber);
        command.Parameters.AddWithValue("@FiscalNumber", support.FiscalNumber);
        command.Parameters.AddWithValue("@IssuedAt", receipt.SupplierInvoiceDate ?? receipt.ReceivedAt);
        command.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@SnapshotJson", PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("@Environment", support.Environment);
        command.Parameters.AddWithValue("@IssuerId", support.IssuerConfigurationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCostSupportFiscalBatchAsync(
        SqlConnection connection, SqlTransaction transaction, GoodsReceiptDocumentPayload receipt,
        IReadOnlyList<GoodsReceiptCostDocumentSnapshot> documents,
        IReadOnlyDictionary<Guid, SupportFiscalAllocation> supports,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (documents.Count == 0) return;
        var taxCodes = documents.SelectMany(item => item.Lines)
            .Select(item => item.TaxCode).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var taxProfiles = new Dictionary<string, (string DianCode, decimal Rate)>(
            StringComparer.OrdinalIgnoreCase);
        var taxProfilesByDianCode = new Dictionary<(string DianCode, decimal Rate), string>();
        await using (var taxCommand = new SqlCommand("""
            SELECT Code,DianTaxCode,Rate FROM dbo.TaxProfiles
            WHERE BusinessId=@BusinessId AND IsActive=1
              AND (Code IN (SELECT value FROM OPENJSON(@TaxCodes) WITH (value nvarchar(32) '$'))
                OR DianTaxCode IN (SELECT value FROM OPENJSON(@TaxCodes) WITH (value nvarchar(32) '$')));
            """, connection, transaction))
        {
            taxCommand.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
            taxCommand.Parameters.AddWithValue("@TaxCodes", JsonSerializer.Serialize(taxCodes));
            await using var reader = await taxCommand.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var code = reader.GetString(0);
                var dianCode = reader.IsDBNull(1) ? string.Empty : reader.GetString(1).Trim();
                var rate = reader.GetDecimal(2);
                taxProfiles[code] = (dianCode, rate);
                if (!string.IsNullOrWhiteSpace(dianCode))
                    taxProfilesByDianCode[(dianCode.ToUpperInvariant(), rate)] = dianCode;
            }
        }
        var rows = documents.Select(document =>
        {
            var support = supports[document.CostDocumentId];
            var snapshot = new PurchaseSupportFiscalSnapshot(null, support.IssuerConfigurationId,
                support.FiscalNumber, support.Environment, support.QrValidationUrl, support.Seller,
                support.Authorization, document.Lines.Select(line =>
            {
                var dianTaxCode = ResolveDianTaxCode(line.LineNumber, line.TaxCode, line.TaxRate,
                    taxProfiles, taxProfilesByDianCode);
                return new PurchaseSupportLineMetadata(line.LineNumber,
                    $"COSTO-{line.CostKind}", "999", "EA",
                    PosSaleFiscalMappings.TaxName(dianTaxCode), dianTaxCode);
            }).ToArray(), SellerPostalZone: support.SellerPostalZone,
                CostDocument: new GoodsReceiptCostDocumentAccountingPayload(
                    receipt.TenantId, receipt.BusinessId, receipt.DocumentId, document));
            return new
            {
                DocumentId = document.CostDocumentId, receipt.BusinessId,
                AuralyNumber = document.DocumentNumber, support.FiscalNumber,
                document.IssuedAt, support.IssuerConfigurationId,
                support.Environment,
                SnapshotJson = PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot)
            };
        }).ToArray();
        await using var command = new SqlCommand("""
            DECLARE @Rows TABLE(DocumentId uniqueidentifier PRIMARY KEY,BusinessId uniqueidentifier,
              AuralyNumber nvarchar(80),FiscalNumber nvarchar(80),IssuedAt datetimeoffset,
              IssuerId uniqueidentifier,Environment tinyint,SnapshotJson nvarchar(max));
            INSERT @Rows SELECT DocumentId,BusinessId,AuralyNumber,FiscalNumber,IssuedAt,
              IssuerConfigurationId,Environment,SnapshotJson
            FROM OPENJSON(@RowsJson) WITH(DocumentId uniqueidentifier,BusinessId uniqueidentifier,
              AuralyNumber nvarchar(80),FiscalNumber nvarchar(80),IssuedAt datetimeoffset,
              IssuerConfigurationId uniqueidentifier,Environment tinyint,SnapshotJson nvarchar(max));
            INSERT dbo.FiscalDocuments(DocumentId,BusinessId,SourceDocumentType,FiscalDocumentType,
              AuralyDocumentNumber,FiscalNumber,UniqueCodeType,UniqueCode,IssuedAt,FiscalStatus,CreatedAt,UpdatedAt)
            SELECT DocumentId,BusinessId,N'GoodsReceiptCostDocument',N'SupportDocument',
              AuralyNumber,FiscalNumber,N'CUDS',NULL,IssuedAt,@Status,@Now,@Now FROM @Rows;
            INSERT fiscal.PurchaseSupportFiscalSnapshots(DocumentId,SnapshotJson,Environment,CreatedAt)
            SELECT DocumentId,SnapshotJson,Environment,@Now FROM @Rows;
            INSERT dbo.FiscalDocumentProcesses(DocumentId,BusinessId,FiscalIssuerConfigurationId,Status,
              AttemptCount,NextAttemptAt,CreatedAt,UpdatedAt)
            SELECT DocumentId,BusinessId,IssuerId,@Status,0,@Now,@Now,@Now FROM @Rows;
            """, connection, transaction);
        command.Parameters.AddWithValue("@RowsJson", JsonSerializer.Serialize(rows));
        command.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string ResolveDianTaxCode(
        GoodsReceiptLineSnapshot line,
        IReadOnlyDictionary<string, (string DianCode, decimal Rate)> taxProfiles,
        IReadOnlyDictionary<(string DianCode, decimal Rate), string> taxProfilesByDianCode) =>
        ResolveDianTaxCode(line.LineNumber, line.TaxCode, line.TaxRate,
            taxProfiles, taxProfilesByDianCode);

    private static string ResolveDianTaxCode(int lineNumber, string taxCode, decimal taxRate,
        IReadOnlyDictionary<string, (string DianCode, decimal Rate)> taxProfiles,
        IReadOnlyDictionary<(string DianCode, decimal Rate), string> taxProfilesByDianCode)
    {
        if (taxProfiles.TryGetValue(taxCode, out var profile) &&
            profile.Rate == taxRate && !string.IsNullOrWhiteSpace(profile.DianCode))
            return profile.DianCode;
        if (taxProfilesByDianCode.TryGetValue(
                (taxCode.Trim().ToUpperInvariant(), taxRate), out var dianCode))
            return dianCode;
        throw new PurchasingValidationException(
            $"La línea {lineNumber} no tiene un código tributario DIAN congelable.");
    }

    private async Task InsertJobAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid documentId, Guid movementId, long sequence, string payload, byte[] payloadHash,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,N'GoodsReceipt',N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,N'GoodsReceipt',@BusinessId,1,@Payload,@PayloadHash,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@Payload", payload);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task DeleteDraftIfPresentAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand(
            "DELETE dbo.GoodsReceiptDrafts WHERE GoodsReceiptDraftId=@Id AND BusinessId=@BusinessId;",
            connection, transaction);
        command.Parameters.AddWithValue("@Id", documentId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static byte[] HashRequest(ConfirmGoodsReceiptRequest request, GoodsReceiptCalculation calculation,
        GoodsReceiptCostCalculation costCalculation, WithholdingCalculationSnapshot withholding,
        IReadOnlyDictionary<Guid, WithholdingCalculationSnapshot> additionalWithholdings) =>
        SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            request.DocumentId,
            request.BusinessId,
            request.WarehouseId,
            request.SupplierId,
            request.SupplierInvoiceNumber,
            request.SupplierInvoiceDate,
            request.ReceivedAt,
            request.CreatesPayable,
            request.DueDate,
            Currency = request.CurrencyCode.ToUpperInvariant(),
            request.Notes,
            request.PurchaseEvidenceType,
            request.PurchaseOrderId,
            calculation.NetAmount,
            calculation.TaxAmount,
            calculation.GrandTotal,
            Lines = calculation.Lines,
            CostCalculation = costCalculation,
            Withholding = withholding,
            AdditionalWithholdings = additionalWithholdings.OrderBy(value => value.Key)
        }));

    private static void AddDecimal(SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

    private static void AddNullableDecimal(SqlCommand command, string name, decimal? value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    internal sealed record SupportFiscalAllocation(
        Guid SeriesId, Guid AuthorizationId, Guid IssuerConfigurationId,
        string FiscalNumber, int Environment, string QrValidationUrl,
        PosSaleUblAuthorizationContract Authorization, PosSaleUblPartyContract Seller,
        string SellerPostalZone);
}
