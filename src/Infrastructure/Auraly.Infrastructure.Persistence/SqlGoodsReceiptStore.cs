using Auraly.Commerce.Taxation.Contracts;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.BuildingBlocks.Domain.Documents;
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
        WithholdingCalculationSnapshot withholding,
        CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(request, calculation, withholding);
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

            await ValidateDraftConcurrencyAsync(
                connection, transaction, user.BusinessId, request.DocumentId,
                request.DraftConcurrencyToken, cancellationToken);
            await ValidateScopeAsync(connection, transaction, user, request, cancellationToken);
            var overReceiptLines = await ValidatePurchaseOrderAsync(
                connection, transaction, user, request, cancellationToken);
            var number = await AllocateNumberAsync(connection, transaction, user.BusinessId, cancellationToken);
            var now = timeProvider.GetUtcNow();
            if (request.PurchaseEvidenceType == PurchaseEvidenceTypes.BuyerElectronicSupportDocument
                && !await SqlDianDocumentQuota.TryReserveAsync(connection, transaction,
                    user.BusinessId, request.DocumentId, "SupportDocument", now, cancellationToken))
                throw new PurchasingValidationException(
                    "No hay cupo de documentos DIAN. Compra un paquete antes de seleccionar documento soporte electrónico.");
            var support = request.PurchaseEvidenceType == PurchaseEvidenceTypes.BuyerElectronicSupportDocument
                ? await AllocateSupportFiscalAsync(connection, transaction, user.BusinessId,
                    request.SupplierId, request.ReceivedAt, now, cancellationToken)
                : null;
            var sequence = await AllocateProcessingSequenceAsync(
                connection, transaction, user.BusinessId, now, cancellationToken);
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
                calculation.Lines.Select(line =>
                {
                    var source = request.Lines.Single(item => item.LineNumber == line.LineNumber);
                    return new GoodsReceiptLineSnapshot(
                        line.LineNumber, line.ProductId, line.Description, line.Quantity,
                        line.UnitCost, line.DiscountAmount, line.TaxCode, line.TaxRate,
                        line.TaxTreatment.ToString(), line.NetAmount, line.TaxAmount, line.LineTotal,
                        source.PresentationName, source.PresentationQuantity, source.UnitsPerPresentation,
                        source.PurchaseOrderLineId, source.OverReceiptReason,
                        source.PurchaseOrderLineId is Guid lineId && overReceiptLines.Contains(lineId));
                }).ToArray(),
                withholding,
                PurchaseEvidenceType: request.PurchaseEvidenceType,
                PurchaseOrderId: request.PurchaseOrderId);
            var payloadJson = GoodsReceiptContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));

            var movementId = ids.NewId();
            await InsertReceiptAsync(
                connection, transaction, user, request, calculation, number,
                support, idempotencyKey, requestHash, now, cancellationToken);
            await InsertLinesAsync(connection, transaction, request.DocumentId, request.Lines, calculation, cancellationToken);
            await InsertJobAsync(
                connection, transaction, user.BusinessId, request.DocumentId, movementId,
                sequence, payloadJson, payloadHash, now, cancellationToken);
            if (support is not null)
                await InsertSupportFiscalAsync(connection, transaction, payload, support,
                    request.Lines, now, cancellationToken);
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
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new PurchasingConflictException(
                "The receipt number, supplier invoice or idempotency key is already in use.");
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
            throw new PurchasingConflictException("The idempotency key or DocumentId was reused with another payload.");
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
              THROW 51100,'The business is outside the authenticated tenant.',1;
            IF NOT EXISTS (SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId AND IsActive=1 AND IsSystem=0 AND UseForGoodsReceipts=1)
              THROW 51101,'Selecciona una bodega activa para recibir mercancía.',1;
            IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51102,'The supplier is outside the authenticated business.',1;
            IF NOT EXISTS (
              SELECT 1 FROM dbo.Suppliers
              WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1
                AND (
                  PurchaseEvidencePolicy IS NULL
                  OR PurchaseEvidencePolicy=N'InternalReceiptVoucher' AND @PurchaseEvidenceType=N'InternalReceiptVoucher'
                  OR PurchaseEvidencePolicy=N'SupplierElectronicInvoice' AND @PurchaseEvidenceType IN (N'SupplierElectronicInvoice',N'InternalReceiptVoucher')
                  OR PurchaseEvidencePolicy=N'BuyerElectronicSupportDocument' AND @PurchaseEvidenceType IN (N'BuyerElectronicSupportDocument',N'InternalReceiptVoucher')))
              THROW 51104,'The selected evidence type is not allowed by the supplier configuration.',1;
            IF EXISTS (
              SELECT x.ProductId
              FROM OPENJSON(@ProductsJson)
                WITH (ProductId UNIQUEIDENTIFIER '$') x
              LEFT JOIN dbo.Products p ON p.ProductId=x.ProductId AND p.IsActive=1
                AND (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                     OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId))
              LEFT JOIN dbo.SupplierProducts sp ON sp.ProductId=x.ProductId AND sp.SupplierId=@SupplierId AND sp.BusinessId=@BusinessId AND sp.IsActive=1
              WHERE p.ProductId IS NULL OR sp.SupplierProductId IS NULL)
              THROW 51103,'Every product must be active and associated with the selected supplier.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.AddWithValue("@PurchaseEvidenceType", request.PurchaseEvidenceType);
        command.Parameters.AddWithValue(
            "@ProductsJson",
            JsonSerializer.Serialize(request.Lines.Select(line => line.ProductId).Distinct()));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is >= 51100 and <= 51104)
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
               Notes,NetAmount,TaxAmount,GrandTotal,Status,ConfirmedByUserId,AcceptedAt)
            VALUES
              (@Id,@BusinessId,@WarehouseId,@SupplierId,@SeriesId,@Number,@Prefix,@SeriesCode,@Consecutive,
               @IdempotencyKey,@PayloadHash,@PurchaseOrderId,@PurchaseEvidenceType,@SupportFiscalSeriesId,@SupportFiscalAuthorizationId,@SupportFiscalNumber,
               @SupplierInvoiceNumber,@SupplierInvoiceDate,@ReceivedAt,
               @CreatesPayable,@DueDate,@CurrencyCode,@Notes,@NetAmount,@TaxAmount,@GrandTotal,N'Accepted',@UserId,@Now);
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
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertLinesAsync(SqlConnection connection, SqlTransaction transaction, Guid documentId,
        IReadOnlyCollection<GoodsReceiptLineRequest> requestLines, GoodsReceiptCalculation calculation, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.GoodsReceiptLines
              (GoodsReceiptId,LineNumber,ProductId,DescriptionSnapshot,Quantity,UnitCost,DiscountAmount,
               TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation,
               PurchaseOrderLineId,OverReceiptReason,OverReceiptAuthorized)
            VALUES(@Id,@Line,@ProductId,@Description,@Quantity,@UnitCost,@Discount,@TaxCode,
                   @TaxRate,@TaxTreatment,@Net,@Tax,@Total,@PresentationName,@PresentationQuantity,@UnitsPerPresentation,
                   @PurchaseOrderLineId,@OverReceiptReason,@OverReceiptAuthorized);
            """;
        foreach (var line in calculation.Lines)
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
            command.Parameters.AddWithValue("@TaxTreatment", line.TaxTreatment.ToString());
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
            await command.ExecuteNonQueryAsync(cancellationToken);
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
                throw new PurchasingValidationException("Receipt lines cannot reference a purchase order without PurchaseOrderId.");
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

    private static async Task<SupportFiscalAllocation> AllocateSupportFiscalAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid supplierId,
        DateTimeOffset issuedAt, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP(1) fs.SeriesId,fs.FiscalAuthorizationId,fs.Prefix,fs.RangeStart,fs.RangeEnd,
                   a.AuthorizationNumber,a.ValidFrom,a.ValidUntil,a.Environment,a.QrValidationUrl,
                   c.FiscalIssuerConfigurationId,
                   p.PartyType,p.Identification,p.VerificationDigit,p.IdentificationTypeCode,
                   COALESCE(p.LegalName,p.DisplayName),COALESCE(p.DisplayName,p.LegalName),
                   country.Code,country.Name,division.Code,division.Name,city.Code,city.Name,site.AddressLine,
                   email.Value,phone.Value
            FROM dbo.FiscalSeries fs WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.FiscalAuthorizations a ON a.FiscalAuthorizationId=fs.FiscalAuthorizationId
            JOIN dbo.FiscalIssuerConfigurations c ON c.BusinessId=fs.BusinessId AND c.IsActive=1
              AND c.Environment=a.Environment
              AND c.ValidFrom<=@IssuedAt AND (c.ValidTo IS NULL OR c.ValidTo>@IssuedAt)
            JOIN dbo.Suppliers s ON s.SupplierId=@SupplierId AND s.BusinessId=fs.BusinessId AND s.IsActive=1
            JOIN dbo.Parties p ON p.PartyId=s.PartyId AND p.IsActive=1
            OUTER APPLY(SELECT TOP(1) value.* FROM dbo.PartySites value
              WHERE value.PartyId=p.PartyId AND value.IsActive=1
              ORDER BY value.IsPrimary DESC,value.CreatedAt,value.PartySiteId) site
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
              AND a.IsActive=1 AND a.ValidFrom<=CONVERT(date,@IssuedAt) AND a.ValidUntil>=CONVERT(date,@IssuedAt)
            ORDER BY a.ValidUntil DESC,fs.SeriesId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@SupplierId", supplierId);
        command.Parameters.AddWithValue("@IssuedAt", issuedAt);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new PurchasingValidationException(
                "No hay numeración DIAN ni configuración fiscal activa para generar el documento soporte.");
        var seriesId = reader.GetGuid(0);
        var authorizationId = reader.GetGuid(1);
        var prefix = reader.GetString(2);
        var rangeStart = reader.GetInt64(3);
        var rangeEnd = reader.GetInt64(4);
        var authorization = new PosSaleUblAuthorizationContract(reader.GetString(5),
            DateOnly.FromDateTime(reader.GetDateTime(6)), DateOnly.FromDateTime(reader.GetDateTime(7)),
            prefix, rangeStart, rangeEnd);
        var environment = reader.GetByte(8);
        var qrUrl = reader.GetString(9);
        var issuerId = reader.GetGuid(10);
        if (reader.IsDBNull(12))
            throw new PurchasingValidationException("El proveedor necesita identificación para generar el documento soporte.");
        if (reader.IsDBNull(14) || Enumerable.Range(17, 7).Any(reader.IsDBNull))
            throw new PurchasingValidationException(
                "El proveedor necesita tipo de identificación y una sede principal con dirección DIAN completa para generar el documento soporte.");
        var seller = new PosSaleUblPartyContract(
            reader.GetString(12), reader.IsDBNull(13) ? "0" : reader.GetString(13),
            reader.GetString(14),
            reader.GetString(11) == "Organization" ? "1" : "2",
            reader.GetString(15), reader.GetString(16), "R-99-PN", "01", "IVA",
            new PosSaleUblAddressContract(
                reader.GetString(21), reader.GetString(22), reader.GetString(20),
                reader.GetString(19), reader.GetString(23), reader.GetString(17),
                reader.GetString(18)),
            reader.IsDBNull(24) ? null : reader.GetString(24),
            reader.IsDBNull(25) ? null : reader.GetString(25));
        await reader.CloseAsync();

        const string cursorSql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.FiscalSeriesCursors WITH (UPDLOCK,HOLDLOCK) WHERE SeriesId=@SeriesId)
              INSERT dbo.FiscalSeriesCursors(SeriesId,NextConsecutive,UpdatedAt) VALUES(@SeriesId,@RangeStart,@Now);
            DECLARE @Value BIGINT;
            SELECT @Value=NextConsecutive FROM dbo.FiscalSeriesCursors WITH (UPDLOCK,HOLDLOCK) WHERE SeriesId=@SeriesId;
            UPDATE dbo.FiscalSeriesCursors SET NextConsecutive=@Value+1,UpdatedAt=@Now WHERE SeriesId=@SeriesId;
            SELECT @Value;
            """;
        await using var cursor = new SqlCommand(cursorSql, connection, transaction);
        cursor.Parameters.AddWithValue("@SeriesId", seriesId);
        cursor.Parameters.AddWithValue("@RangeStart", rangeStart);
        cursor.Parameters.AddWithValue("@Now", now);
        var consecutive = Convert.ToInt64(await cursor.ExecuteScalarAsync(cancellationToken));
        if (consecutive > rangeEnd)
            throw new PurchasingValidationException("La numeración DIAN de documento soporte está agotada.");
        return new(seriesId, authorizationId, issuerId, prefix + consecutive,
            environment, qrUrl, authorization, seller);
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
            """;
        var metadata = new Dictionary<Guid, (string Code, string Unit)>();
        await using (var products = new SqlCommand(productsSql, connection, transaction))
        {
            products.Parameters.AddWithValue("@BusinessId", receipt.BusinessId);
            products.Parameters.AddWithValue("@Ids", JsonSerializer.Serialize(requestLines.Select(x => x.ProductId)));
            await using var reader = await products.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                metadata[reader.GetGuid(0)] = (reader.GetString(1), reader.GetString(2));
        }
        var snapshot = new PurchaseSupportFiscalSnapshot(receipt, support.IssuerConfigurationId,
            support.FiscalNumber, support.Environment, support.QrValidationUrl, support.Seller,
            support.Authorization, receipt.Lines.Select(line =>
            {
                var product = metadata[line.ProductId];
                return new PurchaseSupportLineMetadata(line.LineNumber, product.Code, "999",
                    product.Unit, line.TaxCode == "01" ? "IVA" : "Impuesto");
            }).ToArray());
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
        command.Parameters.AddWithValue("@IssuedAt", receipt.ReceivedAt);
        command.Parameters.AddWithValue("@Status", FiscalDocumentStatusCodes.PendingGeneration);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@SnapshotJson", PurchaseSupportFiscalSnapshotSerializer.Serialize(snapshot));
        command.Parameters.AddWithValue("@Environment", support.Environment);
        command.Parameters.AddWithValue("@IssuerId", support.IssuerConfigurationId);
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static async Task ValidateDraftConcurrencyAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid documentId,
        string? concurrencyToken,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT RowVersion
            FROM dbo.GoodsReceiptDrafts WITH (UPDLOCK,HOLDLOCK)
            WHERE GoodsReceiptDraftId=@DocumentId AND BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        var stored = (byte[]?)await command.ExecuteScalarAsync(cancellationToken);
        if (stored is null)
        {
            if (concurrencyToken is not null)
                throw new PurchasingConflictException("The draft no longer exists.");
            return;
        }

        if (string.IsNullOrWhiteSpace(concurrencyToken))
            throw new PurchasingConflictException("DraftConcurrencyToken is required for a saved draft.");
        byte[] expected;
        try { expected = Convert.FromBase64String(concurrencyToken); }
        catch (FormatException exception)
        { throw new PurchasingValidationException("DraftConcurrencyToken is invalid.", exception); }
        if (!stored.AsSpan().SequenceEqual(expected))
            throw new PurchasingConflictException("The draft changed in another session.");
    }
    private static byte[] HashRequest(ConfirmGoodsReceiptRequest request, GoodsReceiptCalculation calculation,
        WithholdingCalculationSnapshot withholding) =>
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
            Withholding = withholding
        }));

    private static void AddDecimal(SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }

    private sealed record SupportFiscalAllocation(
        Guid SeriesId, Guid AuthorizationId, Guid IssuerConfigurationId,
        string FiscalNumber, int Environment, string QrValidationUrl,
        PosSaleUblAuthorizationContract Authorization, PosSaleUblPartyContract Seller);
}
