using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Application.Inventory;
using Auraly.Contracts.Inventory;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlRestaurantAccountStore
{
    public async Task<RestaurantCancellation> RequestCancellationAsync(
        RestaurantAccountActor actor, Guid accountId, Guid additionId,
        RequestRestaurantCancellation request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var replay = await ReadCancellationAsync(connection, transaction,
            actor, request.CancellationId, ct);
        if (replay is not null)
        {
            if (replay.AccountId != accountId || replay.AdditionId != additionId ||
                replay.Quantity != request.Quantity ||
                replay.ReasonCode != request.ReasonCode ||
                replay.Explanation != request.Explanation ||
                replay.ExpectedAccountVersion != request.ExpectedAccountVersion ||
                replay.RequestedByUserId != actor.UserId)
                throw new RestaurantAccountConflictException(
                    "Esta solicitud ya corresponde a otra cancelación.");
            await transaction.CommitAsync(ct);
            return replay;
        }
        var account = await FindAsync(connection, transaction, actor,
            accountId, true, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        if (account.Status != "Open" ||
            account.Version != request.ExpectedAccountVersion)
            throw new RestaurantAccountConflictException(
                "La cuenta cambió. Revisa el pedido antes de cancelar.");

        const string sql = """
            INSERT sales.RestaurantCancellationRequests
              (CancellationId,TenantId,BusinessId,AccountId,AdditionId,
               CardId,Quantity,ExpectedAccountVersion,ReasonCode,Explanation,Status,
               RequestedByUserId,RequestedAt)
            SELECT @CancellationId,@TenantId,@BusinessId,@AccountId,
                   addition.AdditionId,line.CardId,@Quantity,@ExpectedVersion,@ReasonCode,
                   @Explanation,N'Requested',@UserId,@Now
            FROM sales.RestaurantAccountAdditions addition WITH(UPDLOCK,HOLDLOCK)
            JOIN sales.RestaurantCommandLines line
              ON line.AdditionId=addition.AdditionId
             AND line.AccountId=addition.AccountId
            WHERE addition.TenantId=@TenantId AND addition.BusinessId=@BusinessId
              AND addition.AccountId=@AccountId AND addition.AdditionId=@AdditionId
              AND addition.Status=N'Sent' AND addition.Quantity>=@Quantity
              AND @Quantity<=addition.Quantity-COALESCE((
                SELECT SUM(allocation.Quantity)
                FROM sales.RestaurantCollectionAllocations allocation
                JOIN sales.RestaurantAccountCollections collection
                  ON collection.CollectionId=allocation.CollectionId
                 AND collection.AccountId=allocation.AccountId
                 AND collection.Status=N'Captured'
                WHERE allocation.AccountId=addition.AccountId
                  AND allocation.AdditionId=addition.AdditionId),0)
              AND (addition.AllowsFractionalSale=1 OR @Quantity=FLOOR(@Quantity))
              AND EXISTS(SELECT 1 FROM dbo.BusinessReasons reason
                WHERE reason.BusinessId=@BusinessId
                  AND reason.ReasonType=N'RestaurantCancellation'
                  AND reason.Code=@ReasonCode AND reason.IsActive=1)
              AND NOT EXISTS(
                SELECT 1 FROM sales.RestaurantCancellationRequests existing
                WHERE existing.AdditionId=addition.AdditionId
                  AND existing.Status IN(N'Requested',N'Confirmed'));
            IF @@ROWCOUNT<>1
              THROW 51110,N'El producto ya no admite esta cancelación.',1;

            UPDATE sales.RestaurantAccounts
            SET Version=Version+1,UpdatedAt=@Now
            WHERE AccountId=@AccountId AND BusinessId=@BusinessId
              AND TenantId=@TenantId AND Status=N'Open'
              AND Version=@ExpectedVersion;
            IF @@ROWCOUNT<>1
              THROW 51111,N'La cuenta cambió durante la solicitud.',1;
            """;
        await using (var command = new SqlCommand(sql, connection, transaction))
        {
            AddScope(command, actor);
            command.Parameters.AddWithValue("@AccountId", accountId);
            command.Parameters.AddWithValue("@AdditionId", additionId);
            command.Parameters.AddWithValue("@CancellationId", request.CancellationId);
            command.Parameters.AddWithValue("@UserId", actor.UserId);
            command.Parameters.AddWithValue("@Quantity", request.Quantity);
            command.Parameters.AddWithValue("@ReasonCode", request.ReasonCode);
            command.Parameters.AddWithValue("@Explanation", request.Explanation);
            command.Parameters.AddWithValue("@ExpectedVersion", request.ExpectedAccountVersion);
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            try { await command.ExecuteNonQueryAsync(ct); }
            catch (SqlException error) when (error.Number is 51110 or 51111 or 2601 or 2627)
            { throw new RestaurantAccountConflictException(
                "El producto o la cuenta cambió durante la solicitud."); }
        }
        await AuditCancellationAsync(connection, transaction, actor,
            accountId, request.CancellationId, "RestaurantCancellationRequested",
            new { additionId, request.Quantity, request.ReasonCode,
                request.Explanation }, now, ct);
        await NotifyCancellationAsync(connection, transaction, actor,
            request.CancellationId, now, ct);
        var result = await ReadCancellationAsync(connection, transaction,
            actor, request.CancellationId, ct) ?? throw new InvalidDataException(
                "La solicitud de cancelación desapareció.");
        await transaction.CommitAsync(ct);
        await synchronization.DispatchPendingAsync(actor.TenantId,
            actor.BusinessId, CancellationToken.None);
        return result;
    }

    public async Task<RestaurantCancellation> ConfirmCancellationAsync(
        RestaurantAccountActor actor, Guid cancellationId,
        ConfirmRestaurantCancellation request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await ReadCancellationAsync(connection, transaction,
            actor, cancellationId, ct) ?? throw new RestaurantAccountConflictException(
                "La cancelación ya no está disponible.");
        var account = await FindAsync(connection, transaction, actor,
            current.AccountId, true, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        if (account.Status != "Open" || current.Status != "Requested")
            throw new RestaurantAccountConflictException(
                "La cancelación ya fue resuelta o la cuenta cambió.");
        var consumed = await ResolveConsumptionAsync(connection, transaction,
            actor, current, request, ct);
        await using (var command = new SqlCommand("""
            UPDATE sales.RestaurantCancellationRequests
            SET Status=N'Confirmed',PreparationState=@PreparationState,
                ConsumedComponentsJson=@ConsumedComponentsJson,
                ConfirmedByUserId=@UserId,ConfirmedAt=@Now
            WHERE CancellationId=@CancellationId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND Status=N'Requested'
              AND RowVersion=@Version;
            IF @@ROWCOUNT<>1
              THROW 51112,N'La solicitud cambió durante la confirmación.',1;
            UPDATE sales.RestaurantAccounts
            SET Version=Version+1,UpdatedAt=@Now
            WHERE AccountId=@AccountId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND Status=N'Open';
            """, connection, transaction))
        {
            AddScope(command, actor);
            command.Parameters.AddWithValue("@AccountId", current.AccountId);
            command.Parameters.AddWithValue("@CancellationId", cancellationId);
            command.Parameters.AddWithValue("@UserId", actor.UserId);
            command.Parameters.AddWithValue("@PreparationState", request.PreparationState);
            command.Parameters.Add("@ConsumedComponentsJson", SqlDbType.NVarChar, -1)
                .Value = consumed.Count == 0 ? DBNull.Value :
                    JsonSerializer.Serialize(consumed);
            command.Parameters.Add("@Version", SqlDbType.Binary, 8)
                .Value = Convert.FromBase64String(request.RowVersion);
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            try { await command.ExecuteNonQueryAsync(ct); }
            catch (SqlException error) when (error.Number == 51112)
            { throw new RestaurantAccountConflictException(
                "La solicitud cambió. Actualiza la vista."); }
        }
        await AuditCancellationAsync(connection, transaction, actor,
            current.AccountId, cancellationId,
            "RestaurantCancellationPreparationConfirmed",
            new { request.PreparationState }, now, ct);
        await NotifyCancellationAsync(connection, transaction, actor,
            cancellationId, now, ct);
        var result = await ReadCancellationAsync(connection, transaction,
            actor, cancellationId, ct) ?? throw new InvalidDataException(
                "La confirmación desapareció.");
        await transaction.CommitAsync(ct);
        await synchronization.DispatchPendingAsync(actor.TenantId,
            actor.BusinessId, CancellationToken.None);
        return result;
    }

    public async Task<RestaurantCancellation> ResolveCancellationAsync(
        RestaurantAccountActor actor, Guid cancellationId,
        ResolveRestaurantCancellation request, DateTimeOffset now,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var current = await ReadCancellationAsync(connection, transaction,
            actor, cancellationId, ct) ?? throw new RestaurantAccountConflictException(
                "La cancelación ya no está disponible.");
        if (current.Status == (request.Approve ? "Approved" : "Rejected"))
        {
            await transaction.CommitAsync(ct);
            return current;
        }
        var account = await FindAsync(connection, transaction, actor,
            current.AccountId, true, ct) ?? throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        if (account.Status != "Open" || current.Status is not ("Requested" or "Confirmed"))
            throw new RestaurantAccountConflictException(
                "La cancelación o la cuenta ya no admite esta resolución.");
        if (request.Approve && current.Status != "Confirmed")
            throw new RestaurantAccountConflictException(
                "La estación debe confirmar la preparación antes de aprobar.");
        Guid? inventoryDocumentId = null;
        if (request.Approve)
        {
            await using (var printClaim = new SqlCommand("""
                SELECT job.State,job.ClaimedAt,product.ManageStock,
                       addition.ManageStockAtSend
                FROM sales.RestaurantAccountAdditions addition
                JOIN dbo.Products product
                  ON product.TenantId=addition.TenantId
                 AND product.ProductId=addition.ProductId
                LEFT JOIN sales.RestaurantCommandPrintJobs job WITH(UPDLOCK,HOLDLOCK)
                  ON job.CardId=@CardId AND job.TenantId=addition.TenantId
                 AND job.BusinessId=addition.BusinessId
                WHERE addition.AdditionId=@AdditionId
                  AND addition.TenantId=@TenantId
                  AND addition.BusinessId=@BusinessId;
                """, connection, transaction))
            {
                AddScope(printClaim, actor);
                printClaim.Parameters.AddWithValue("@CardId", current.CardId);
                printClaim.Parameters.AddWithValue("@AdditionId", current.AdditionId);
                await using var reader = await printClaim.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    throw new RestaurantAccountConflictException(
                        "El producto de la comanda ya no está disponible.");
                if (current.ConsumedComponents is { Count: > 0 } &&
                    (reader.IsDBNull(3) || reader.GetBoolean(2) != reader.GetBoolean(3)))
                    throw new RestaurantAccountConflictException(
                        "Cambió el control de inventario del producto preparado. Restablece su modalidad antes de aprobar la pérdida.");
                if (!reader.IsDBNull(0) && reader.GetString(0) == "Claimed" &&
                    !reader.IsDBNull(1) &&
                    reader.GetDateTimeOffset(1) > now.AddMinutes(-5))
                    throw new RestaurantAccountConflictException(
                        "La tirilla está saliendo. Reintenta cuando termine la impresión.");
            }
            if (current.ConsumedComponents is { Count: > 0 } components)
            {
                inventoryDocumentId = Guid.NewGuid();
                var identity = new InventoryUserIdentity(actor.UserId,
                    actor.TenantId, actor.BusinessId,
                    new HashSet<string>(StringComparer.Ordinal)
                    { InventoryPermissionCodes.Damage });
                var damage = new ConfirmInventoryDamageRequest(
                    inventoryDocumentId.Value, actor.BusinessId,
                    account.WarehouseId, now, "RESTAURANT_LOSS", null,
                    $"Cancelación de comanda {cancellationId:D}: {current.Explanation}",
                    components.Select((item, index) =>
                        new InventoryDamageLineRequest(index + 1,
                            item.ProductId, item.Quantity)).ToArray(),
                    "Irreversible");
                try
                {
                    await inventoryOperations.ConfirmDamageAtomicallyAsync(
                        identity, $"restaurant-cancellation:{cancellationId:N}",
                        damage, connection, transaction, ct);
                }
                catch (Exception error) when (error is InventoryValidationException
                    or InventoryConflictException)
                {
                    throw new RestaurantAccountConflictException(
                        $"No se pudo registrar la pérdida de inventario: {error.Message}");
                }
            }
            await using var correction = new SqlCommand("""
                UPDATE sales.RestaurantAccountAdditions
                SET Quantity=CASE WHEN Quantity=@Quantity THEN Quantity
                                  ELSE Quantity-@Quantity END,
                    Status=CASE WHEN Quantity=@Quantity THEN N'Cancelled'
                                ELSE N'Sent' END,
                    CancelledAt=CASE WHEN Quantity=@Quantity THEN @Now
                                     ELSE NULL END,
                    UpdatedAt=@Now
                WHERE AdditionId=@AdditionId AND AccountId=@AccountId
                  AND TenantId=@TenantId AND BusinessId=@BusinessId
                  AND Status=N'Sent' AND Quantity>=@Quantity;
                IF @@ROWCOUNT<>1
                  THROW 51113,N'La cantidad del producto cambió.',1;
                """, connection, transaction);
            AddScope(correction, actor);
            correction.Parameters.AddWithValue("@AccountId", current.AccountId);
            correction.Parameters.AddWithValue("@AdditionId", current.AdditionId);
            correction.Parameters.AddWithValue("@Quantity", current.Quantity);
            correction.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            try { await correction.ExecuteNonQueryAsync(ct); }
            catch (SqlException error) when (error.Number == 51113)
            { throw new RestaurantAccountConflictException(
                "La cantidad del producto cambió. Actualiza la cuenta."); }
            await using var closeCard = new SqlCommand("""
                UPDATE sales.RestaurantCommandCards
                SET State=N'Cancelled',UpdatedAt=@Now
                WHERE CardId=@CardId AND TenantId=@TenantId
                  AND BusinessId=@BusinessId AND State<>N'Cancelled'
                  AND NOT EXISTS(
                    SELECT 1 FROM sales.RestaurantCommandLines line
                    JOIN sales.RestaurantAccountAdditions addition
                      ON addition.AdditionId=line.AdditionId
                     AND addition.AccountId=line.AccountId
                    WHERE line.CardId=@CardId
                      AND addition.Status=N'Sent');
                """, connection, transaction);
            AddScope(closeCard, actor);
            closeCard.Parameters.AddWithValue("@CardId", current.CardId);
            closeCard.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            await closeCard.ExecuteNonQueryAsync(ct);
        }
        await using (var command = new SqlCommand("""
            UPDATE sales.RestaurantCancellationRequests
            SET Status=@Status,ResolvedByUserId=@UserId,ResolvedAt=@Now,
                InventoryDocumentId=@InventoryDocumentId
            WHERE CancellationId=@CancellationId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND Status IN(N'Requested',N'Confirmed')
              AND RowVersion=@Version;
            IF @@ROWCOUNT<>1
              THROW 51114,N'La solicitud cambió durante la resolución.',1;
            UPDATE sales.RestaurantAccounts
            SET Version=Version+1,UpdatedAt=@Now
            WHERE AccountId=@AccountId AND TenantId=@TenantId
              AND BusinessId=@BusinessId AND Status=N'Open';
            """, connection, transaction))
        {
            AddScope(command, actor);
            command.Parameters.AddWithValue("@AccountId", current.AccountId);
            command.Parameters.AddWithValue("@CancellationId", cancellationId);
            command.Parameters.AddWithValue("@UserId", actor.UserId);
            command.Parameters.AddWithValue("@Status", request.Approve ? "Approved" : "Rejected");
            command.Parameters.Add("@InventoryDocumentId", SqlDbType.UniqueIdentifier)
                .Value = (object?)inventoryDocumentId ?? DBNull.Value;
            command.Parameters.Add("@Version", SqlDbType.Binary, 8)
                .Value = Convert.FromBase64String(request.RowVersion);
            command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
            try { await command.ExecuteNonQueryAsync(ct); }
            catch (SqlException error) when (error.Number == 51114)
            { throw new RestaurantAccountConflictException(
                "La solicitud cambió. Actualiza la vista."); }
        }
        await AuditCancellationAsync(connection, transaction, actor,
            current.AccountId, cancellationId,
            request.Approve ? "RestaurantCancellationApproved" :
                "RestaurantCancellationRejected",
            new { current.AdditionId, current.Quantity,
                current.PreparationState }, now, ct);
        if (request.Approve)
            await QueuePrintCorrectionAsync(connection, transaction, actor,
                current.CardId, cancellationId, now, ct);
        await NotifyCancellationAsync(connection, transaction, actor,
            cancellationId, now, ct);
        var result = await ReadCancellationAsync(connection, transaction,
            actor, cancellationId, ct) ?? throw new InvalidDataException(
                "La resolución desapareció.");
        await transaction.CommitAsync(ct);
        await synchronization.DispatchPendingAsync(actor.TenantId,
            actor.BusinessId, CancellationToken.None);
        return result;
    }

    public async Task<IReadOnlyList<RestaurantCancellation>> CancellationsAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        if (await FindAsync(connection, null, actor, accountId, false, ct) is null)
            throw new RestaurantAccountConflictException(
                "La cuenta ya no está disponible.");
        await using var command = new SqlCommand("""
            SELECT CancellationId,AccountId,AdditionId,CardId,Quantity,
                   ReasonCode,Explanation,Status,PreparationState,
                   RequestedByUserId,RequestedAt,RowVersion,ExpectedAccountVersion,
                   ConsumedComponentsJson
            FROM sales.RestaurantCancellationRequests
            WHERE TenantId=@TenantId AND BusinessId=@BusinessId
              AND AccountId=@AccountId
              AND Status IN(N'Requested',N'Confirmed')
            ORDER BY RequestedAt,CancellationId;
            """, connection);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AccountId", accountId);
        var result = new List<RestaurantCancellation>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) result.Add(ReadCancellation(reader));
        return result;
    }

    private static async Task<RestaurantCancellation?> ReadCancellationAsync(
        SqlConnection connection, SqlTransaction? transaction,
        RestaurantAccountActor actor, Guid cancellationId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT CancellationId,AccountId,AdditionId,CardId,Quantity,
                   ReasonCode,Explanation,Status,PreparationState,
                   RequestedByUserId,RequestedAt,RowVersion,ExpectedAccountVersion,
                   ConsumedComponentsJson
            FROM sales.RestaurantCancellationRequests
            WHERE CancellationId=@CancellationId AND TenantId=@TenantId
              AND BusinessId=@BusinessId;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@CancellationId", cancellationId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadCancellation(reader) : null;
    }

    private static RestaurantCancellation ReadCancellation(SqlDataReader reader) =>
        new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetGuid(3), reader.GetDecimal(4), reader.GetString(5),
            reader.GetString(6), reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            reader.GetGuid(9), reader.GetDateTimeOffset(10),
            Convert.ToBase64String((byte[])reader.GetValue(11)),reader.GetInt64(12),
            reader.IsDBNull(13) ? null :
                JsonSerializer.Deserialize<List<RestaurantCancellationConsumption>>(
                    reader.GetString(13)));

    public async Task<IReadOnlyList<RestaurantCancellationComponentOption>>
        CancellationComponentsAsync(RestaurantAccountActor actor,
            Guid cancellationId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var cancellation = await ReadCancellationAsync(connection, null,
            actor, cancellationId, ct) ?? throw new RestaurantAccountConflictException(
                "La cancelación ya no está disponible.");
        var (_, options) = await LoadExpectedConsumptionAsync(connection,
            null, actor, cancellation, ct);
        return options;
    }

    private static async Task<IReadOnlyList<RestaurantCancellationConsumption>>
        ResolveConsumptionAsync(SqlConnection connection,
            SqlTransaction transaction, RestaurantAccountActor actor,
            RestaurantCancellation cancellation,
            ConfirmRestaurantCancellation request, CancellationToken ct)
    {
        if (request.PreparationState == "NotPrepared") return [];
        var (manageStock, options) = await LoadExpectedConsumptionAsync(
            connection, transaction, actor, cancellation, ct);
        var allowed = options.Select(item => item.ProductId).ToHashSet();
        var supplied = request.ConsumedComponents;
        if (supplied is { Count: > 0 })
        {
            if (supplied.Any(item => !allowed.Contains(item.ProductId)))
                throw new RestaurantAccountValidationException(
                    "Los componentes consumidos no pertenecen al producto enviado.");
            if (manageStock && supplied.Sum(item => item.Quantity) >
                cancellation.Quantity)
                throw new RestaurantAccountValidationException(
                    "El consumo de producto terminado supera la cantidad cancelada.");
            return supplied;
        }
        if (request.PreparationState == "Partial")
            throw new RestaurantAccountValidationException(
                "Registra los componentes realmente consumidos.");
        return options.Select(item => new RestaurantCancellationConsumption(
            item.ProductId, item.ExpectedQuantity)).ToArray();
    }

    private static async Task<(bool ManageStock,
        IReadOnlyList<RestaurantCancellationComponentOption> Options)>
        LoadExpectedConsumptionAsync(SqlConnection connection,
            SqlTransaction? transaction, RestaurantAccountActor actor,
            RestaurantCancellation cancellation, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            SELECT product.ManageStock,addition.ProductId,
                   ingredient.IngredientProductId,ingredient.QuantityPerUnit,
                   CASE WHEN product.ManageStock=1 THEN product.Name
                        ELSE ingredientProduct.Name END,
                   addition.ManageStockAtSend
            FROM sales.RestaurantAccountAdditions addition
            JOIN dbo.Products product ON product.TenantId=addition.TenantId
              AND product.ProductId=addition.ProductId
            LEFT JOIN catalog.ProductRecipeIngredients ingredient
              ON ingredient.TenantId=addition.TenantId
             AND ingredient.ProductRecipeVersionId=addition.ProductRecipeVersionId
            LEFT JOIN dbo.Products ingredientProduct
              ON ingredientProduct.TenantId=ingredient.TenantId
             AND ingredientProduct.ProductId=ingredient.IngredientProductId
            WHERE addition.AdditionId=@AdditionId
              AND addition.AccountId=@AccountId
              AND addition.TenantId=@TenantId
              AND addition.BusinessId=@BusinessId
            ORDER BY ingredient.LineNumber;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@AdditionId", cancellation.AdditionId);
        command.Parameters.AddWithValue("@AccountId", cancellation.AccountId);
        var expected = new List<RestaurantCancellationComponentOption>();
        var allowed = new HashSet<Guid>();
        var manageStock = false;
        await using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                manageStock = reader.GetBoolean(0);
                if (reader.IsDBNull(5) || reader.GetBoolean(5) != manageStock)
                    throw new RestaurantAccountConflictException(
                        "Cambió el control de inventario del producto enviado. Restablece su modalidad antes de confirmar el consumo.");
                if (manageStock)
                {
                    if (allowed.Add(reader.GetGuid(1)))
                        expected.Add(new(reader.GetGuid(1), reader.GetString(4),
                            cancellation.Quantity));
                }
                else if (!reader.IsDBNull(2))
                {
                    var productId = reader.GetGuid(2);
                    allowed.Add(productId);
                    var quantity = decimal.Round(reader.GetDecimal(3) *
                        cancellation.Quantity, 6, MidpointRounding.AwayFromZero);
                    if (quantity > 0)
                        expected.Add(new(productId, reader.GetString(4), quantity));
                }
            }
        }
        return (manageStock, expected);
    }

    private static async Task AuditCancellationAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid accountId, Guid cancellationId,
        string action, object values, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            INSERT dbo.AuditLogs
              (AuditLogId,UserId,TenantId,BusinessId,Action,EntityType,
               EntityId,NewValues,CorrelationId,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,@Action,
                   N'RestaurantAccount',@AccountId,@Values,
                   @CancellationId,@Now);
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@UserId", actor.UserId);
        command.Parameters.AddWithValue("@Action", action);
        command.Parameters.AddWithValue("@AccountId", accountId.ToString());
        command.Parameters.AddWithValue("@Values", JsonSerializer.Serialize(values));
        command.Parameters.AddWithValue("@CancellationId", cancellationId.ToString());
        command.Parameters.Add("@Now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task NotifyCancellationAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid cancellationId,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommands';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,
               OccurredAt,EntityType,EntityId,ChangeKind)
            VALUES(NEWID(),@BusinessId,N'RestaurantCommands',@Cursor,
                   @Now,N'RestaurantCancellation',@CancellationId,N'Upsert');
            """, connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", actor.BusinessId);
        command.Parameters.AddWithValue("@CancellationId", cancellationId);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task QueuePrintCorrectionAsync(
        SqlConnection connection, SqlTransaction transaction,
        RestaurantAccountActor actor, Guid cardId, Guid cancellationId,
        DateTimeOffset now, CancellationToken ct)
    {
        await using var command = new SqlCommand("""
            DECLARE @Changed TABLE(DeviceId UNIQUEIDENTIFIER);
            UPDATE sales.RestaurantCommandPrintJobs
            SET State=N'Pending',LeaseToken=NULL,ClaimedAt=NULL,
                SubmittedAt=NULL,LastError=NULL,CopyNumber=0,
                PrintKind=N'Correction',
                CorrectionCancellationId=@CancellationId,UpdatedAt=@Now
            OUTPUT inserted.DeviceId INTO @Changed
            WHERE CardId=@CardId AND TenantId=@TenantId
              AND BusinessId=@BusinessId
              AND (State<>N'Claimed' OR
                   ClaimedAt<DATEADD(MINUTE,-5,@Now));
            IF EXISTS(SELECT 1 FROM sales.RestaurantCommandPrintJobs
                      WHERE CardId=@CardId AND TenantId=@TenantId
                        AND BusinessId=@BusinessId)
               AND NOT EXISTS(SELECT 1 FROM @Changed)
              THROW 51115,N'La tirilla sigue en proceso.',1;

            DECLARE @Cursor BIGINT;
            SELECT @Cursor=ISNULL(MAX(AvailableThroughCursor),0)+1
            FROM dbo.PosSynchronizationOutboxMessages WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND Stream=N'RestaurantCommandPrint';
            INSERT dbo.PosSynchronizationOutboxMessages
              (NotificationId,BusinessId,Stream,AvailableThroughCursor,
               OccurredAt,EntityType,EntityId,ChangeKind,TargetDeviceId)
            SELECT NEWID(),@BusinessId,N'RestaurantCommandPrint',@Cursor,
                   @Now,N'RestaurantCommandPrintJob',@CardId,N'Upsert',DeviceId
            FROM @Changed;
            """, connection, transaction);
        AddScope(command, actor);
        command.Parameters.AddWithValue("@CardId", cardId);
        command.Parameters.AddWithValue("@CancellationId", cancellationId);
        command.Parameters.Add("@Now", SqlDbType.DateTimeOffset).Value = now;
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException error) when (error.Number == 51115)
        { throw new RestaurantAccountConflictException(
            "La tirilla sigue en proceso. Reintenta la aprobación."); }
    }
}
