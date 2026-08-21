using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Inventory;
using Auraly.BuildingBlocks.Domain.Documents;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlInventoryOperationStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider timeProvider) : IInventoryOperationStore
{
    public async Task<StockCountDraft> StartCountAsync(InventoryUserIdentity user, StartStockCountRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            await ValidateScopeAsync(connection, transaction, user, request.WarehouseId, null, request.ProductIds, cancellationToken);
            var reasonDescription = await LoadActiveReasonAsync(connection, transaction, user.BusinessId, InventoryDocumentTypes.StockCount, request.ReasonCode, cancellationToken);
            var baseSequence = await ReadBaseSequenceAsync(connection, transaction, request.BusinessId, cancellationToken);
            var products = await LoadProductsAsync(connection, transaction, request.BusinessId, request.WarehouseId, request.ProductIds, cancellationToken);
            var lines = products.OrderBy(product => product.Code, StringComparer.Ordinal).Select((product, index) =>
                new InventoryOperationLineSnapshot(index + 1, "COUNT", product.Id, product.Code, product.Name, 0m, product.Quantity, null, null)).ToArray();
            var now = timeProvider.GetUtcNow();
            const string insert = """
                INSERT dbo.InventoryOperations
                  (InventoryOperationId,BusinessId,DocumentType,WarehouseId,OccurredAt,ReasonCode,ReasonDescription,
                   BaseInventorySequence,Notes,Status,CreatedAt)
                VALUES(@Id,@BusinessId,N'StockCount',@WarehouseId,@OccurredAt,@Reason,@ReasonDescription,@BaseSequence,@Notes,N'Draft',@Now);
                """;
            await using (var command = new SqlCommand(insert, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", request.DocumentId);
                command.Parameters.AddWithValue("@BusinessId", request.BusinessId);
                command.Parameters.AddWithValue("@WarehouseId", request.WarehouseId);
                command.Parameters.AddWithValue("@OccurredAt", request.OccurredAt);
                command.Parameters.AddWithValue("@Reason", request.ReasonCode);
                command.Parameters.AddWithValue("@ReasonDescription", reasonDescription);
                command.Parameters.AddWithValue("@BaseSequence", baseSequence);
                command.Parameters.AddWithValue("@Notes", (object?)request.Notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertLinesAsync(connection, transaction, request.DocumentId, lines, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new StockCountDraft(request.DocumentId, "Draft", baseSequence, lines);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InventoryConflictException("The stock count DocumentId is already in use.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public async Task<InventoryOperationAcceptance> ConfirmCountAsync(InventoryUserIdentity user, Guid documentId, string idempotencyKey, ConfirmStockCountRequest request, CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var requestHash = Hash(new { documentId, request.BusinessId, request.Lines });
            var replay = await TryReplayAsync(connection, transaction, request.BusinessId, documentId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null) { await transaction.CommitAsync(cancellationToken); return replay; }

            var draft = await LoadCountDraftAsync(connection, transaction, user, documentId, cancellationToken);
            var counted = request.Lines.ToDictionary(line => line.ProductId);
            if (counted.Count != draft.Lines.Count || draft.Lines.Any(line => !counted.ContainsKey(line.ProductId)))
                throw new InventoryValidationException("The confirmed count must contain exactly the products captured at start.");
            var lines = draft.Lines.Select(line => line with { Quantity = counted[line.ProductId].CountedQuantity }).ToArray();
            var acceptance = await AcceptDraftAsync(connection, transaction, user, documentId, InventoryDocumentTypes.StockCount,
                draft.WarehouseId, null, draft.OccurredAt, draft.ReasonCode, null, null, draft.BaseSequence, draft.Notes,
                idempotencyKey, requestHash, lines, cancellationToken);
            foreach (var line in lines)
            {
                const string update = "UPDATE dbo.InventoryOperationLines SET Quantity=@Quantity WHERE InventoryOperationId=@Id AND ProductId=@ProductId;";
                await using var command = new SqlCommand(update, connection, transaction);
                command.Parameters.AddWithValue("@Id", documentId);
                command.Parameters.AddWithValue("@ProductId", line.ProductId);
                AddDecimal(command, "@Quantity", line.Quantity, 19, 6);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
            return acceptance;
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    public Task<InventoryOperationAcceptance> ConfirmAdjustmentAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryAdjustmentRequest request, CancellationToken cancellationToken) =>
        AcceptNewAsync(user, idempotencyKey, request.DocumentId, InventoryDocumentTypes.Adjustment,
            request.WarehouseId, null, request.OccurredAt, request.ReasonCode, null, request.CostCenterId, null, request.Notes,
            request.Lines.Select(line => new LineInput(line.LineNumber, "ADJUSTMENT", line.ProductId, line.QuantityChange, null, line.ExplicitUnitCost, null)).ToArray(), request, cancellationToken);

    public Task<InventoryOperationAcceptance> ConfirmTransferAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmWarehouseTransferRequest request, CancellationToken cancellationToken) =>
        AcceptNewAsync(user, idempotencyKey, request.DocumentId, InventoryDocumentTypes.Transfer,
            request.SourceWarehouseId, request.DestinationWarehouseId, request.OccurredAt, request.ReasonCode, null, null, null, request.Notes,
            request.Lines.Select(line => new LineInput(line.LineNumber, "TRANSFER", line.ProductId, line.Quantity, null, null, null)).ToArray(), request, cancellationToken);

    public Task<InventoryOperationAcceptance> ConfirmDamageAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmInventoryDamageRequest request, CancellationToken cancellationToken) =>
        AcceptNewAsync(user, idempotencyKey, request.DocumentId, InventoryDocumentTypes.Damage,
            request.WarehouseId, null, request.OccurredAt, request.ReasonCode, null, request.CostCenterId, null, request.Notes,
            request.Lines.Select(line => new LineInput(line.LineNumber, "DAMAGE", line.ProductId, line.Quantity, null, null, null)).ToArray(), request, cancellationToken);

    public Task<InventoryOperationAcceptance> ConfirmConversionAsync(InventoryUserIdentity user, string idempotencyKey, ConfirmProductConversionRequest request, CancellationToken cancellationToken) =>
        AcceptNewAsync(user, idempotencyKey, request.DocumentId, InventoryDocumentTypes.Conversion,
            request.WarehouseId, null, request.OccurredAt, request.ReasonCode, request.ConversionType, request.CostCenterId, null, request.Notes,
            request.Lines.Select(line => new LineInput(line.LineNumber, line.Direction, line.ProductId, line.Quantity, null, null, line.AllocationWeight)).ToArray(), request, cancellationToken);

    private async Task<InventoryOperationAcceptance> AcceptNewAsync(
        InventoryUserIdentity user, string idempotencyKey, Guid documentId, string documentType,
        Guid warehouseId, Guid? destinationWarehouseId, DateTimeOffset occurredAt, string reasonCode,
        string? conversionType, Guid? costCenterId, long? baseSequence, string? notes,
        IReadOnlyList<LineInput> inputLines, object requestForHash, CancellationToken cancellationToken)
    {
        var requestHash = Hash(requestForHash);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var replay = await TryReplayAsync(connection, transaction, user.BusinessId, documentId, idempotencyKey, requestHash, cancellationToken);
            if (replay is not null) { await transaction.CommitAsync(cancellationToken); return replay; }
            await ValidateScopeAsync(connection, transaction, user, warehouseId, destinationWarehouseId, inputLines.Select(line => line.ProductId), cancellationToken);
            var reasonDescription = await LoadActiveReasonAsync(connection, transaction, user.BusinessId, documentType, reasonCode, cancellationToken);
            var products = (await LoadProductsAsync(connection, transaction, user.BusinessId, warehouseId, inputLines.Select(line => line.ProductId), cancellationToken)).ToDictionary(product => product.Id);
            var lines = inputLines.Select(line => new InventoryOperationLineSnapshot(
                line.LineNumber, line.Direction, line.ProductId, products[line.ProductId].Code,
                products[line.ProductId].Name, line.Quantity, line.SystemQuantityAtBase,
                line.ExplicitUnitCost, line.AllocationWeight)).ToArray();
            var now = timeProvider.GetUtcNow();
            const string insert = """
                INSERT dbo.InventoryOperations
                  (InventoryOperationId,BusinessId,DocumentType,WarehouseId,DestinationWarehouseId,
                   OccurredAt,ReasonCode,ReasonDescription,ConversionType,CostCenterId,BaseInventorySequence,Notes,Status,CreatedAt)
                VALUES(@Id,@BusinessId,@Type,@WarehouseId,@Destination,@OccurredAt,@Reason,@ReasonDescription,@ConversionType,
                   @CostCenterId,@BaseSequence,@Notes,N'Draft',@Now);
                """;
            await using (var command = new SqlCommand(insert, connection, transaction))
            {
                command.Parameters.AddWithValue("@Id", documentId);
                command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
                command.Parameters.AddWithValue("@Type", documentType);
                command.Parameters.AddWithValue("@WarehouseId", warehouseId);
                command.Parameters.AddWithValue("@Destination", (object?)destinationWarehouseId ?? DBNull.Value);
                command.Parameters.AddWithValue("@OccurredAt", occurredAt);
                command.Parameters.AddWithValue("@Reason", reasonCode);
                command.Parameters.AddWithValue("@ReasonDescription", reasonDescription);
                command.Parameters.AddWithValue("@ConversionType", (object?)conversionType ?? DBNull.Value);
                command.Parameters.AddWithValue("@CostCenterId", (object?)costCenterId ?? DBNull.Value);
                command.Parameters.AddWithValue("@BaseSequence", (object?)baseSequence ?? DBNull.Value);
                command.Parameters.AddWithValue("@Notes", (object?)notes ?? DBNull.Value);
                command.Parameters.AddWithValue("@Now", now);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await InsertLinesAsync(connection, transaction, documentId, lines, cancellationToken);
            var acceptance = await AcceptDraftAsync(connection, transaction, user, documentId, documentType,
                warehouseId, destinationWarehouseId, occurredAt, reasonCode, conversionType, costCenterId,
                baseSequence, notes, idempotencyKey, requestHash, lines, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return acceptance;
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw new InventoryConflictException("The document or idempotency key is already in use.");
        }
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task<InventoryOperationAcceptance> AcceptDraftAsync(
        SqlConnection connection, SqlTransaction transaction, InventoryUserIdentity user,
        Guid documentId, string documentType, Guid warehouseId, Guid? destinationWarehouseId,
        DateTimeOffset occurredAt, string reasonCode, string? conversionType, Guid? costCenterId,
        long? baseSequence, string? notes, string idempotencyKey, byte[] requestHash,
        IReadOnlyList<InventoryOperationLineSnapshot> lines, CancellationToken cancellationToken)
    {
        var number = await AllocateNumberAsync(connection, transaction, user.BusinessId, documentType, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var sequence = await AllocateSequenceAsync(connection, transaction, user.BusinessId, now, cancellationToken);
        var payload = new InventoryOperationDocumentPayload(user.TenantId, user.BusinessId, documentId,
            documentType, warehouseId, destinationWarehouseId, user.UserId, number.FullNumber,
            number.SeriesId, number.Prefix, number.SeriesCode, number.Consecutive, occurredAt,
            reasonCode, conversionType, costCenterId, baseSequence, notes, lines);
        var payloadJson = InventoryOperationContractSerializer.Serialize(payload);
        var payloadHash = SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson));
        var movementId = ids.NewId();
        const string sql = """
            UPDATE dbo.InventoryOperations
            SET DocumentSeriesId=@SeriesId,DocumentNumber=@Number,DocumentPrefix=@Prefix,
                DocumentSeriesCode=@SeriesCode,DocumentConsecutive=@Consecutive,
                IdempotencyKey=@IdempotencyKey,PayloadHash=@RequestHash,Status=N'Accepted',
                ConfirmedByUserId=@UserId,AcceptedAt=@Now
            WHERE InventoryOperationId=@DocumentId AND BusinessId=@BusinessId AND Status=N'Draft';
            IF @@ROWCOUNT<>1 THROW 51210,'The inventory operation is not an editable draft.',1;
            INSERT dbo.DocumentProcessingJobs
              (JobId,BusinessId,ProcessingSequence,DocumentId,DocumentType,Status,AvailableAt,CreatedAt)
            VALUES(@JobId,@BusinessId,@Sequence,@DocumentId,@DocumentType,N'Pending',@Now,@Now);
            INSERT dbo.DocumentProcessingPayloads
              (DocumentId,DocumentType,BusinessId,ContractVersion,PayloadJson,PayloadHash,AcceptedAt)
            VALUES(@DocumentId,@DocumentType,@BusinessId,1,@Payload,@PayloadHash,@Now);
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@SeriesId", number.SeriesId);
        command.Parameters.AddWithValue("@Number", number.FullNumber);
        command.Parameters.AddWithValue("@Prefix", number.Prefix);
        command.Parameters.AddWithValue("@SeriesCode", number.SeriesCode);
        command.Parameters.AddWithValue("@Consecutive", number.Consecutive);
        command.Parameters.AddWithValue("@IdempotencyKey", idempotencyKey);
        command.Parameters.Add("@RequestHash", SqlDbType.Binary, 32).Value = requestHash;
        command.Parameters.AddWithValue("@UserId", user.UserId);
        command.Parameters.AddWithValue("@Now", now);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@JobId", movementId);
        command.Parameters.AddWithValue("@Sequence", sequence);
        command.Parameters.AddWithValue("@DocumentType", documentType);
        command.Parameters.AddWithValue("@Payload", payloadJson);
        command.Parameters.Add("@PayloadHash", SqlDbType.Binary, 32).Value = payloadHash;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return new InventoryOperationAcceptance(documentId, movementId, documentType, number.FullNumber, "Accepted", sequence, false);
    }

    private static async Task<InventoryOperationAcceptance?> TryReplayAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid documentId, string idempotencyKey, byte[] requestHash, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT o.InventoryOperationId,o.DocumentType,o.DocumentNumber,o.Status,o.PayloadHash,j.ProcessingSequence,j.JobId
            FROM dbo.InventoryOperations o WITH (UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.DocumentProcessingJobs j ON j.DocumentId=o.InventoryOperationId AND j.DocumentType=o.DocumentType
            WHERE o.BusinessId=@BusinessId AND (o.InventoryOperationId=@DocumentId OR o.IdempotencyKey=@Key) AND o.Status<>N'Draft';
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@DocumentId", documentId);
        command.Parameters.AddWithValue("@Key", idempotencyKey);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        if (!reader.GetFieldValue<byte[]>(4).AsSpan().SequenceEqual(requestHash))
            throw new InventoryConflictException("The idempotency key or DocumentId was reused with another payload.");
        return new InventoryOperationAcceptance(reader.GetGuid(0), reader.GetGuid(6), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt64(5), true);
    }

    private static async Task ValidateScopeAsync(SqlConnection connection, SqlTransaction transaction,
        InventoryUserIdentity user, Guid warehouseId, Guid? destinationWarehouseId,
        IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.Businesses WHERE BusinessId=@BusinessId AND TenantId=@TenantId)
              THROW 51200,'The business is outside the authenticated tenant.',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@WarehouseId AND BusinessId=@BusinessId
              AND IsActive=1 AND UseForSales=1)
              THROW 51201,'La bodega no está habilitada como bodega de venta.',1;
            IF @Destination IS NOT NULL AND NOT EXISTS(SELECT 1 FROM dbo.Warehouses WHERE WarehouseId=@Destination AND BusinessId=@BusinessId
              AND IsActive=1 AND (UseForSales=1 OR (@AllowSystemDestination=1 AND Code IN(N'PED',N'AVE'))))
              THROW 51202,'La bodega de destino no está habilitada como bodega de venta.',1;
            IF EXISTS(SELECT x.ProductId FROM OPENJSON(@Products) WITH(ProductId UNIQUEIDENTIFIER '$') x
              LEFT JOIN dbo.Products p ON p.ProductId=x.ProductId AND p.BusinessId=@BusinessId AND p.IsActive=1 AND p.ManageStock=1
              WHERE p.ProductId IS NULL)
              THROW 51203,'Every product must be active, belong to the business and manage stock.',1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", user.BusinessId);
        command.Parameters.AddWithValue("@TenantId", user.TenantId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@Destination", (object?)destinationWarehouseId ?? DBNull.Value);
        command.Parameters.AddWithValue("@AllowSystemDestination", user.Permissions.Contains("inventory.system-warehouses.use"));
        command.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(productIds.Distinct()));
        try { await command.ExecuteNonQueryAsync(cancellationToken); }
        catch (SqlException exception) when (exception.Number is >= 51200 and <= 51203) { throw new InventoryValidationException(exception.Message); }
    }

    private static async Task<string> LoadActiveReasonAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, string operationType, string reasonCode, CancellationToken cancellationToken)
    {
        const string sql = "SELECT Name FROM dbo.InventoryReasons WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId AND OperationType=@OperationType AND Code=@Code AND IsActive=1;";
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@OperationType", operationType);
        command.Parameters.AddWithValue("@Code", reasonCode);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string ?? throw new InventoryValidationException("Select an active reason compatible with the inventory operation.");
    }

    private static async Task<IReadOnlyList<ProductState>> LoadProductsAsync(SqlConnection connection, SqlTransaction transaction,
        Guid businessId, Guid warehouseId, IEnumerable<Guid> productIds, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT p.ProductId,COALESCE(p.ProductCode,p.Sku),p.Name,
                   COALESCE(b.QuantityOnHand / NULLIF(CASE WHEN link.ProductLinkId IS NULL THEN 1 ELSE link.InventoryFactor END,0),0)
            FROM dbo.Products p WITH (UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.ProductLinks link WITH (UPDLOCK,HOLDLOCK)
              ON link.BusinessId=p.BusinessId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            INNER JOIN dbo.Products inventoryProduct WITH (UPDLOCK,HOLDLOCK)
              ON inventoryProduct.ProductId=COALESCE(link.ParentProductId,p.ProductId) AND inventoryProduct.BusinessId=p.BusinessId
            LEFT JOIN dbo.InventoryBalances b WITH (UPDLOCK,HOLDLOCK)
              ON b.BusinessId=p.BusinessId AND b.WarehouseId=@WarehouseId AND b.ProductId=inventoryProduct.ProductId
            INNER JOIN OPENJSON(@Products) WITH(ProductId UNIQUEIDENTIFIER '$') x ON x.ProductId=p.ProductId
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1 AND inventoryProduct.ManageStock=1;
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", businessId);
        command.Parameters.AddWithValue("@WarehouseId", warehouseId);
        command.Parameters.AddWithValue("@Products", JsonSerializer.Serialize(productIds.Distinct()));
        var result = new List<ProductState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetDecimal(3)));
        return result;
    }

    private static async Task InsertLinesAsync(SqlConnection connection, SqlTransaction transaction, Guid documentId,
        IEnumerable<InventoryOperationLineSnapshot> lines, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT dbo.InventoryOperationLines
              (InventoryOperationId,LineNumber,Direction,ProductId,ProductCodeSnapshot,DescriptionSnapshot,
               Quantity,SystemQuantityAtBase,ExplicitUnitCost,AllocationWeight)
            VALUES(@Id,@Line,@Direction,@ProductId,@Code,@Description,@Quantity,@SystemAtBase,@UnitCost,@Weight);
            """;
        foreach (var line in lines)
        {
            await using var command = new SqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("@Id", documentId);
            command.Parameters.AddWithValue("@Line", line.LineNumber);
            command.Parameters.AddWithValue("@Direction", line.Direction);
            command.Parameters.AddWithValue("@ProductId", line.ProductId);
            command.Parameters.AddWithValue("@Code", line.ProductCode);
            command.Parameters.AddWithValue("@Description", line.Description);
            AddNullableDecimal(command, "@Quantity", line.Direction == "COUNT" ? null : line.Quantity, 19, 6);
            AddNullableDecimal(command, "@SystemAtBase", line.SystemQuantityAtBase, 19, 6);
            AddNullableDecimal(command, "@UnitCost", line.ExplicitUnitCost, 19, 6);
            AddNullableDecimal(command, "@Weight", line.AllocationWeight, 9, 6);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task<CountDraftState> LoadCountDraftAsync(SqlConnection connection, SqlTransaction transaction,
        InventoryUserIdentity user, Guid documentId, CancellationToken cancellationToken)
    {
        const string header = """
            SELECT o.WarehouseId,o.OccurredAt,o.ReasonCode,o.BaseInventorySequence,o.Notes
            FROM dbo.InventoryOperations o WITH(UPDLOCK,HOLDLOCK)
            INNER JOIN dbo.Businesses b ON b.BusinessId=o.BusinessId
            WHERE o.InventoryOperationId=@Id AND o.BusinessId=@BusinessId AND b.TenantId=@TenantId
              AND o.DocumentType=N'StockCount' AND o.Status=N'Draft';
            """;
        Guid warehouseId; DateTimeOffset occurred; string reason; long sequence; string? notes;
        await using (var command = new SqlCommand(header, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", documentId); command.Parameters.AddWithValue("@BusinessId", user.BusinessId); command.Parameters.AddWithValue("@TenantId", user.TenantId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new InventoryValidationException("The stock count draft was not found.");
            warehouseId=reader.GetGuid(0); occurred=reader.GetDateTimeOffset(1); reason=reader.GetString(2); sequence=reader.GetInt64(3); notes=reader.IsDBNull(4)?null:reader.GetString(4);
        }
        const string detail = "SELECT LineNumber,ProductId,ProductCodeSnapshot,DescriptionSnapshot,SystemQuantityAtBase FROM dbo.InventoryOperationLines WHERE InventoryOperationId=@Id ORDER BY LineNumber;";
        var lines = new List<InventoryOperationLineSnapshot>();
        await using (var command = new SqlCommand(detail, connection, transaction))
        {
            command.Parameters.AddWithValue("@Id", documentId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while(await reader.ReadAsync(cancellationToken)) lines.Add(new(reader.GetInt32(0),"COUNT",reader.GetGuid(1),reader.GetString(2),reader.GetString(3),0m,reader.GetDecimal(4),null,null));
        }
        return new(warehouseId,occurred,reason,sequence,notes,lines);
    }

    private static async Task<long> ReadBaseSequenceAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT COALESCE(LastAssignedSequence,0) FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId;";
        await using var command = new SqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("@BusinessId", businessId);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static async Task<long> AllocateSequenceAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        const string sql = """
            IF NOT EXISTS(SELECT 1 FROM dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) WHERE BusinessId=@BusinessId)
              INSERT dbo.BusinessProcessingCursors(BusinessId,LastAssignedSequence,LastCompletedSequence,UpdatedAt) VALUES(@BusinessId,0,0,@Now);
            UPDATE dbo.BusinessProcessingCursors WITH(UPDLOCK,HOLDLOCK) SET LastAssignedSequence=LastAssignedSequence+1,UpdatedAt=@Now
            OUTPUT inserted.LastAssignedSequence WHERE BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection, transaction); command.Parameters.AddWithValue("@BusinessId", businessId); command.Parameters.AddWithValue("@Now", now);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task<AuralyDocumentNumberAssignment> AllocateNumberAsync(SqlConnection connection, SqlTransaction transaction, Guid businessId, string documentType, CancellationToken cancellationToken)
    {
        var defaultPrefix = AuralyDocumentTypes.DefaultPrefix(documentType);
        const string ensureSql = """
            IF NOT EXISTS (
              SELECT 1 FROM dbo.DocumentSeries WITH(UPDLOCK,HOLDLOCK)
              WHERE BusinessId=@BusinessId AND DocumentType=@Type AND DeviceId IS NULL AND IsActive=1)
              INSERT dbo.DocumentSeries
                (DocumentSeriesId,BusinessId,DeviceId,DocumentType,Prefix,SeriesCode,Padding,RangeStart,RangeEnd,IsOfflineCapable,IsActive,CreatedAt)
              VALUES(NEWID(),@BusinessId,NULL,@Type,@Prefix,N'00',8,1,99999999,0,1,SYSDATETIMEOFFSET());
            """;
        await using (var ensure = new SqlCommand(ensureSql, connection, transaction))
        {
            ensure.Parameters.AddWithValue("@BusinessId", businessId);
            ensure.Parameters.AddWithValue("@Type", documentType);
            ensure.Parameters.AddWithValue("@Prefix", defaultPrefix);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }        const string selectSql = """
            SELECT TOP(1) ds.DocumentSeriesId,ds.Prefix,ds.SeriesCode,ds.Padding,ds.RangeEnd,COALESCE(c.NextConsecutive,ds.RangeStart)
            FROM dbo.DocumentSeries ds WITH(UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.DocumentSeriesCursors c WITH(UPDLOCK,HOLDLOCK) ON c.DocumentSeriesId=ds.DocumentSeriesId
            WHERE ds.BusinessId=@BusinessId AND ds.DocumentType=@Type AND ds.DeviceId IS NULL AND ds.IsActive=1 ORDER BY ds.DocumentSeriesId;
            """;
        Guid seriesId; string prefix; string code; byte padding; long rangeEnd; long consecutive;
        await using (var command = new SqlCommand(selectSql, connection, transaction))
        {
            command.Parameters.AddWithValue("@BusinessId", businessId); command.Parameters.AddWithValue("@Type", documentType);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if(!await reader.ReadAsync(cancellationToken)) throw new InventoryValidationException("La serie de este movimiento de inventario no está activa para la sede.");
            seriesId=reader.GetGuid(0); prefix=reader.GetString(1); code=reader.GetString(2); padding=reader.GetByte(3); rangeEnd=reader.GetInt64(4); consecutive=reader.GetInt64(5);
        }
        if(consecutive>rangeEnd) throw new InventoryValidationException("La numeración de este movimiento de inventario se agotó.");
        const string update="""
            IF EXISTS(SELECT 1 FROM dbo.DocumentSeriesCursors WHERE DocumentSeriesId=@Id)
              UPDATE dbo.DocumentSeriesCursors SET NextConsecutive=@Next,UpdatedAt=@Now WHERE DocumentSeriesId=@Id;
            ELSE INSERT dbo.DocumentSeriesCursors(DocumentSeriesId,NextConsecutive,UpdatedAt) VALUES(@Id,@Next,@Now);
            """;
        await using var updateCommand=new SqlCommand(update,connection,transaction); updateCommand.Parameters.AddWithValue("@Id",seriesId); updateCommand.Parameters.AddWithValue("@Next",consecutive+1); updateCommand.Parameters.AddWithValue("@Now",DateTimeOffset.UtcNow); await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        return AuralyDocumentNumberAssignment.Create(seriesId,documentType,prefix,code,consecutive,padding);
    }

    private static byte[] Hash(object value) => SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value));
    private static void AddDecimal(SqlCommand command,string name,decimal value,byte precision,byte scale){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=precision;p.Scale=scale;p.Value=value;}
    private static void AddNullableDecimal(SqlCommand command,string name,decimal? value,byte precision,byte scale){var p=command.Parameters.Add(name,SqlDbType.Decimal);p.Precision=precision;p.Scale=scale;p.Value=(object?)value??DBNull.Value;}
    private sealed record LineInput(int LineNumber,string Direction,Guid ProductId,decimal Quantity,decimal? SystemQuantityAtBase,decimal? ExplicitUnitCost,decimal? AllocationWeight);
    private sealed record ProductState(Guid Id,string Code,string Name,decimal Quantity);
    private sealed record CountDraftState(Guid WarehouseId,DateTimeOffset OccurredAt,string ReasonCode,long BaseSequence,string? Notes,IReadOnlyList<InventoryOperationLineSnapshot> Lines);
}
