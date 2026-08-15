using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Purchasing;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Purchasing;
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
        CancellationToken cancellationToken)
    {
        var requestHash = HashRequest(request, calculation);
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
            var number = await AllocateNumberAsync(connection, transaction, user.BusinessId, cancellationToken);
            var now = timeProvider.GetUtcNow();
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
                        source.PresentationName, source.PresentationQuantity, source.UnitsPerPresentation);
                }).ToArray());
            var payloadJson = GoodsReceiptContractSerializer.Serialize(payload);
            var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));

            var movementId = ids.NewId();
            await InsertReceiptAsync(
                connection, transaction, user, request, calculation, number,
                idempotencyKey, requestHash, now, cancellationToken);
            await InsertLinesAsync(connection, transaction, request.DocumentId, request.Lines, calculation, cancellationToken);
            await InsertJobAsync(
                connection, transaction, user.BusinessId, request.DocumentId, movementId,
                sequence, payloadJson, payloadHash, now, cancellationToken);
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
            IF NOT EXISTS (SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId)
              THROW 51101,'The warehouse is outside the authenticated business.',1;
            IF NOT EXISTS (SELECT 1 FROM dbo.Suppliers WHERE SupplierId=@SupplierId AND BusinessId=@BusinessId AND IsActive=1)
              THROW 51102,'The supplier is outside the authenticated business.',1;
            IF EXISTS (
              SELECT x.ProductId
              FROM OPENJSON(@ProductsJson)
                WITH (ProductId UNIQUEIDENTIFIER '$') x
              LEFT JOIN dbo.Products p ON p.ProductId=x.ProductId AND p.BusinessId=@BusinessId AND p.IsActive=1
              LEFT JOIN dbo.SupplierProducts sp ON sp.ProductId=x.ProductId AND sp.SupplierId=@SupplierId AND sp.BusinessId=@BusinessId AND sp.IsActive=1
              WHERE p.ProductId IS NULL OR sp.SupplierProductId IS NULL)
              THROW 51103,'Every product must be active and associated with the selected supplier.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
        command.Parameters.AddWithValue("@SupplierId", request.SupplierId);
        command.Parameters.AddWithValue(
            "@ProductsJson",
            JsonSerializer.Serialize(request.Lines.Select(line => line.ProductId).Distinct()));
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is >= 51100 and <= 51103)
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
        AuralyDocumentNumberAssignment number, string idempotencyKey, byte[] requestHash,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.GoodsReceipts
              (GoodsReceiptId,BusinessId,WarehouseId,SupplierId,DocumentSeriesId,DocumentNumber,
               DocumentPrefix,DocumentSeriesCode,DocumentConsecutive,IdempotencyKey,PayloadHash,
               SupplierInvoiceNumber,SupplierInvoiceDate,ReceivedAt,CreatesPayable,DueDate,CurrencyCode,
               Notes,NetAmount,TaxAmount,GrandTotal,Status,ConfirmedByUserId,AcceptedAt)
            VALUES
              (@Id,@BusinessId,@WarehouseId,@SupplierId,@SeriesId,@Number,@Prefix,@SeriesCode,@Consecutive,
               @IdempotencyKey,@PayloadHash,@SupplierInvoiceNumber,@SupplierInvoiceDate,@ReceivedAt,
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
               TaxCode,TaxRate,TaxTreatment,NetAmount,TaxAmount,LineTotal,PresentationNameSnapshot,PresentationQuantity,UnitsPerPresentation)
            VALUES(@Id,@Line,@ProductId,@Description,@Quantity,@UnitCost,@Discount,@TaxCode,
                   @TaxRate,@TaxTreatment,@Net,@Tax,@Total,@PresentationName,@PresentationQuantity,@UnitsPerPresentation);
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
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
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
    private static byte[] HashRequest(ConfirmGoodsReceiptRequest request, GoodsReceiptCalculation calculation) =>
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
            calculation.NetAmount,
            calculation.TaxAmount,
            calculation.GrandTotal,
            Lines = calculation.Lines
        }));

    private static void AddDecimal(SqlCommand command, string name, decimal value, byte precision, byte scale)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value;
    }
}
