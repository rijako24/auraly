using System.Data;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;
using Auraly.Fiscal.Core;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<PreparedOnlineOrderCheckout?> FindRestaurantReplayAsync(
        OnlineSalesUserIdentity user, Guid accountId, Guid workSessionId,
        CompleteOnlineSalesDraftRequest request, string idempotencyKey,
        CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand("""
            SELECT receipt.IdempotencyKey,receipt.RequestHash,
                   receipt.PayloadJson
            FROM dbo.OnlineSalesCheckoutReceipts receipt
            JOIN sales.RestaurantAccounts account
              ON account.AccountId=receipt.RestaurantAccountId
             AND account.BusinessId=receipt.BusinessId
            WHERE receipt.RestaurantAccountId=@AccountId
              AND receipt.CheckoutUserId=@UserId
              AND receipt.WorkSessionId=@WorkSessionId
              AND account.TenantId=@TenantId;
            """, connection);
        command.Parameters.AddRange([
            P("@AccountId", accountId), P("@UserId", user.UserId),
            P("@WorkSessionId", workSessionId),
            P("@TenantId", user.TenantId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var hash = CapturedCheckoutHash(
            Guid.Empty, accountId, BitConverter.GetBytes(request.ExpectedVersion),
            null, request);
        if (!string.Equals(reader.GetString(0), idempotencyKey,
                StringComparison.Ordinal) ||
            !string.Equals(reader.GetString(1), hash,
                StringComparison.Ordinal))
            throw new OnlineSalesDraftConcurrencyException(
                "La cuenta ya tiene una emisión preparada con otra solicitud.");
        return new(PosSaleContractSerializer.Deserialize(reader.GetString(2)), true);
    }

    public async Task<OnlineSalesRestaurantCheckoutSource> ReadRestaurantSourceAsync(
        OnlineSalesUserIdentity user, Guid accountId, Guid workSessionId,
        CancellationToken ct)
    {
        const string sql = """
            SELECT account.BusinessId,account.TableId,account.WarehouseId,
                   account.Version,account.Status,
                   CAST(CASE WHEN EXISTS(
                     SELECT 1 FROM sales.RestaurantCancellationRequests cancellation
                     WHERE cancellation.AccountId=account.AccountId
                       AND cancellation.TenantId=account.TenantId
                       AND cancellation.BusinessId=account.BusinessId
                       AND cancellation.Status IN(N'Requested',N'Confirmed'))
                     THEN 1 ELSE 0 END AS bit)
            FROM sales.RestaurantAccounts account
            JOIN dbo.Businesses business
              ON business.BusinessId=account.BusinessId
             AND business.TenantId=account.TenantId
             AND business.IsActive=1
             AND business.PointOfSaleMode=N'RestaurantBar'
            JOIN dbo.WorkSessions sessionRow
              ON sessionRow.WorkSessionId=@WorkSessionId
             AND sessionRow.TenantId=account.TenantId
             AND sessionRow.BusinessId=account.BusinessId
             AND sessionRow.UserId=@UserId
             AND sessionRow.Status=N'Open'
             AND sessionRow.DeviceId IS NULL
            WHERE account.AccountId=@AccountId
              AND account.TenantId=@TenantId
              AND account.Status IN(N'Open',N'Issuing');

            SELECT addition.AdditionId,addition.ProductId,addition.ProductCode,
                   addition.Description,addition.UnitCode,addition.TaxCode,
                   addition.TaxRate,addition.Quantity,addition.PublicUnitPrice,
                   addition.PromotionDiscountPerUnit,addition.CurrencyCode,
                   addition.PriceSource,addition.AddedByUserId,addition.AddedAt,
                   product.ManageStock,
                   CASE WHEN product.ManageStock=1 THEN COALESCE(balance.AverageUnitCost,0)
                        ELSE COALESCE(recipeCost.UnitCost,0) END,
                   addition.ProductRecipeVersionId,
                   addition.Status,addition.ManageStockAtSend
            FROM sales.RestaurantAccountAdditions addition
            JOIN sales.RestaurantAccounts account
              ON account.AccountId=addition.AccountId
             AND account.BusinessId=addition.BusinessId
             AND account.TenantId=addition.TenantId
            JOIN dbo.Products product
              ON product.ProductId=addition.ProductId
             AND product.TenantId=addition.TenantId
            LEFT JOIN dbo.InventoryBalances balance
              ON balance.BusinessId=addition.BusinessId
             AND balance.WarehouseId=account.WarehouseId
             AND balance.ProductId=addition.ProductId
            OUTER APPLY (
              SELECT SUM(ingredient.QuantityPerUnit *
                         COALESCE(ingredientBalance.AverageUnitCost,0)) AS UnitCost
              FROM dbo.ProductRecipeIngredients ingredient
              LEFT JOIN dbo.InventoryBalances ingredientBalance
                ON ingredientBalance.BusinessId=addition.BusinessId
               AND ingredientBalance.WarehouseId=account.WarehouseId
               AND ingredientBalance.ProductId=ingredient.IngredientProductId
              WHERE ingredient.ProductRecipeVersionId=addition.ProductRecipeVersionId
                AND ingredient.TenantId=addition.TenantId
            ) recipeCost
            WHERE addition.AccountId=@AccountId
              AND addition.TenantId=@TenantId
              AND addition.Status<>N'Cancelled'
            ORDER BY addition.AddedAt,addition.AdditionId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@AccountId", accountId), P("@TenantId", user.TenantId),
            P("@UserId", user.UserId), P("@WorkSessionId", workSessionId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "La cuenta o la sesión de caja no está disponible para facturar.");
        var businessId = reader.GetGuid(0);
        var tableId = reader.GetGuid(1);
        var warehouseId = reader.GetGuid(2);
        var version = reader.GetInt64(3);
        if (reader.GetBoolean(5))
            throw new OnlineSalesDraftValidationException(
                "Resuelve la cancelación pendiente antes de facturar.");
        await reader.NextResultAsync(ct);
        var lines = new List<OnlineSalesOrderCheckoutLine>();
        while (await reader.ReadAsync(ct))
        {
            if (reader.GetString(17) is not ("Sent" or "Direct"))
                throw new OnlineSalesDraftValidationException(
                    "Envía o retira los productos pendientes antes de facturar.");
            var manageStock = reader.GetBoolean(14);
            if (reader.IsDBNull(18) || reader.GetBoolean(18) != manageStock)
                throw new OnlineSalesDraftValidationException(
                    "Cambió el control de inventario de un producto enviado. Restablece su modalidad antes de facturar esta mesa.");
            var recipeVersionId = reader.IsDBNull(16) ? (Guid?)null : reader.GetGuid(16);
            var quantity = reader.GetDecimal(7);
            var publicPrice = reader.GetDecimal(8);
            var discountPerUnit = reader.GetDecimal(9);
            lines.Add(new OnlineSalesOrderCheckoutLine(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.GetString(5),
                reader.GetDecimal(6), quantity, publicPrice,
                decimal.Round(quantity * discountPerUnit, 4),
                decimal.Round(quantity * (publicPrice - discountPerUnit), 4),
                reader.GetDecimal(15),
                reader.GetString(10), reader.GetString(11),
                AddedByUserId: reader.GetGuid(12),
                AddedAt: reader.GetDateTimeOffset(13),
                RecipeVersionId: recipeVersionId));
        }
        return new OnlineSalesRestaurantCheckoutSource(
            accountId, businessId, tableId, warehouseId,
            workSessionId, version, lines);
    }

    public async Task<OnlineSalesFiscalKeyContext> ResolveRestaurantFiscalKeyContextAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesRestaurantCheckoutSource source,
        CancellationToken ct)
    {
        const string sql = """
            SELECT TOP(2) fiscalAuth.FiscalAuthorizationId,
                   fiscalAuth.AuthorizationNumber,
                   fiscalAuth.TechnicalKeyVersion,
                   fiscalAuth.Environment
            FROM sales.RestaurantAccounts account
            JOIN dbo.WorkSessions sessionRow
              ON sessionRow.WorkSessionId=@WorkSessionId
             AND sessionRow.BusinessId=account.BusinessId
             AND sessionRow.TenantId=account.TenantId
             AND sessionRow.UserId=@UserId
             AND sessionRow.Status=N'Open'
            JOIN dbo.FiscalSeries series
              ON series.BusinessId=account.BusinessId
             AND series.DeviceId IS NULL
             AND series.EmitterKind=N'Server'
             AND series.DocumentType=@DocumentType
             AND series.IsActive=1
            JOIN dbo.FiscalAuthorizations fiscalAuth
              ON fiscalAuth.FiscalAuthorizationId=series.FiscalAuthorizationId
             AND fiscalAuth.BusinessId=account.BusinessId
             AND fiscalAuth.IsActive=1
            WHERE account.AccountId=@AccountId
              AND account.TenantId=@TenantId
              AND account.BusinessId=@BusinessId
              AND CONVERT(date,@Now) BETWEEN fiscalAuth.ValidFrom
                                          AND fiscalAuth.ValidUntil
            ORDER BY series.SeriesId;
            """;
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddRange([
            P("@AccountId", source.AccountId), P("@TenantId", user.TenantId),
            P("@BusinessId", source.BusinessId), P("@UserId", user.UserId),
            P("@WorkSessionId", source.WorkSessionId),
            P("@DocumentType", PosSaleDocumentTypes.Invoice),
            P("@Now", time.GetUtcNow())
        ]);
        var rows = new List<OnlineSalesFiscalKeyContext>(2);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(new FiscalKeyReference(
                user.TenantId, source.BusinessId, reader.GetGuid(0),
                reader.GetString(1), reader.GetString(2),
                (FiscalEnvironment)reader.GetByte(3))));
        if (rows.Count != 1)
            throw new OnlineSalesDraftValidationException(
                rows.Count == 0
                    ? "La sede no tiene una resolución fiscal activa y vigente."
                    : "La sede tiene más de una serie fiscal activa.");
        return rows[0];
    }

    public async Task<PreparedOnlineOrderCheckout> PrepareRestaurantAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesRestaurantCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial? fiscalMaterial,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await PrepareRestaurantAttemptAsync(
                    user, source, request, idempotencyKey,
                    fiscalMaterial, settlement, ct);
            }
            catch (SqlException error) when (error.Number == 1205 && attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * attempt), ct);
            }
        }
    }

    private async Task<PreparedOnlineOrderCheckout> PrepareRestaurantAttemptAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesRestaurantCheckoutSource source,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial? fiscalMaterial,
        PreparedOnlineSaleSettlement settlement,
        CancellationToken ct)
    {
        var hash = CapturedCheckoutHash(
            Guid.Empty, source.AccountId, BitConverter.GetBytes(source.Version),
            null, request);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using (var receipt = new SqlCommand("""
            SELECT IdempotencyKey,RequestHash,PayloadJson,
                   CheckoutUserId,WorkSessionId
            FROM dbo.OnlineSalesCheckoutReceipts WITH(UPDLOCK,HOLDLOCK)
            WHERE RestaurantAccountId=@AccountId AND BusinessId=@BusinessId;
            """, connection, transaction))
        {
            receipt.Parameters.AddRange([
                P("@AccountId", source.AccountId),
                P("@BusinessId", source.BusinessId)
            ]);
            await using var reader = await receipt.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                if (reader.GetGuid(3) != user.UserId ||
                    reader.GetGuid(4) != source.WorkSessionId ||
                    !string.Equals(reader.GetString(0), idempotencyKey,
                        StringComparison.Ordinal) ||
                    !string.Equals(reader.GetString(1), hash,
                        StringComparison.Ordinal))
                    throw new OnlineSalesDraftConcurrencyException(
                        "La cuenta ya tiene una emisión preparada con otra solicitud.");
                var replay = PosSaleContractSerializer.Deserialize(
                    reader.GetString(2));
                await reader.DisposeAsync();
                await transaction.CommitAsync(ct);
                return new(replay, true);
            }
        }

        await using (var account = new SqlCommand("""
            SELECT account.Version,account.Status
            FROM sales.RestaurantAccounts account WITH(UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses business
              ON business.BusinessId=account.BusinessId
             AND business.TenantId=account.TenantId
             AND business.IsActive=1
             AND business.PointOfSaleMode=N'RestaurantBar'
            JOIN dbo.WorkSessions sessionRow
              ON sessionRow.WorkSessionId=@WorkSessionId
             AND sessionRow.BusinessId=account.BusinessId
             AND sessionRow.TenantId=account.TenantId
             AND sessionRow.UserId=@UserId
             AND sessionRow.Status=N'Open'
             AND sessionRow.DeviceId IS NULL
            WHERE account.AccountId=@AccountId
              AND account.BusinessId=@BusinessId
              AND account.TenantId=@TenantId
              AND account.TableId=@TableId
              AND account.WarehouseId=@WarehouseId
              AND NOT EXISTS (
                SELECT 1 FROM sales.RestaurantAccountAdditions addition
                WHERE addition.AccountId=account.AccountId
                  AND addition.Status=N'Pending')
              AND NOT EXISTS (
                SELECT 1 FROM sales.RestaurantCancellationRequests cancellation
                WHERE cancellation.AccountId=account.AccountId
                  AND cancellation.TenantId=account.TenantId
                  AND cancellation.BusinessId=account.BusinessId
                  AND cancellation.Status IN(N'Requested',N'Confirmed'));
            """, connection, transaction))
        {
            account.Parameters.AddRange([
                P("@AccountId", source.AccountId),
                P("@BusinessId", source.BusinessId),
                P("@TenantId", user.TenantId),
                P("@TableId", source.TableId),
                P("@WarehouseId", source.WarehouseId),
                P("@WorkSessionId", source.WorkSessionId),
                P("@UserId", user.UserId)
            ]);
            await using var reader = await account.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct) ||
                reader.GetInt64(0) != source.Version ||
                reader.GetString(1) != "Open")
                throw new OnlineSalesDraftConcurrencyException(
                    "La cuenta cambió o tiene productos sin enviar.");
        }

        var upload = await BuildCapturedSaleUploadAsync(
            connection, transaction, user,
            CapturedCheckoutSource.FromRestaurant(source),
            request, fiscalMaterial, settlement, ct);
        await using (var mapping = new SqlCommand("""
            INSERT sales.RestaurantCheckoutLines
              (DocumentId,LineNumber,TenantId,BusinessId,AccountId,
               AdditionId,ProductRecipeVersionId)
            SELECT @DocumentId,input.LineNumber,@TenantId,@BusinessId,@AccountId,
                   input.AdditionId,input.RecipeVersionId
            FROM OPENJSON(@LinesJson) WITH (
              LineNumber int '$.LineNumber',
              AdditionId uniqueidentifier '$.AdditionId',
              RecipeVersionId uniqueidentifier '$.RecipeVersionId') input;
            """, connection, transaction))
        {
            mapping.Parameters.AddRange([
                P("@DocumentId", upload.DocumentId),
                P("@TenantId", user.TenantId),
                P("@BusinessId", source.BusinessId),
                P("@AccountId", source.AccountId),
                P("@LinesJson", JsonSerializer.Serialize(source.Lines.Select(
                    (line, index) => new {
                        LineNumber = index + 1,
                        AdditionId = line.LineId,
                        line.RecipeVersionId
                    })))
            ]);
            if (await mapping.ExecuteNonQueryAsync(ct) != source.Lines.Count)
                throw new InvalidOperationException(
                    "La emisión no pudo vincular todas las incorporaciones.");
        }
        var now = settlement.Context.OccurredAt;
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.OnlineSalesCheckoutReceipts(
              OnlineSalesCheckoutReceiptId,BusinessId,SalesDraftId,
              NextSalesDraftId,SourceOrderId,RestaurantAccountId,
              OperationId,CheckoutUserId,WorkSessionId,
              IdempotencyKey,RequestHash,DocumentId,PayloadJson,
              Status,CreatedAt)
            VALUES(@ReceiptId,@BusinessId,NULL,NULL,NULL,@AccountId,
                   NULL,@UserId,@WorkSessionId,@Key,@Hash,@DocumentId,
                   @Payload,N'Prepared',@Now);

            UPDATE sales.RestaurantAccounts
            SET Status=N'Issuing',UpdatedAt=@Now
            WHERE AccountId=@AccountId AND BusinessId=@BusinessId
              AND TenantId=@TenantId AND Status=N'Open'
              AND Version=@Version;
            IF @@ROWCOUNT<>1
              THROW 51084,N'La cuenta cambió durante la emisión.',1;

            INSERT dbo.AuditLogs(
              AuditLogId,UserId,TenantId,BusinessId,Action,
              EntityType,EntityId,NewValues,CorrelationId,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantCheckoutPrepared',N'RestaurantAccount',
                   CONVERT(nvarchar(100),@AccountId),
                   CONVERT(nvarchar(100),@DocumentId),
                   CONVERT(nvarchar(100),@DocumentId),@AuditTime);
            """, [
                P("@ReceiptId", ids.NewId()),
                P("@BusinessId", source.BusinessId),
                P("@AccountId", source.AccountId),
                P("@TenantId", user.TenantId),
                P("@UserId", user.UserId),
                P("@WorkSessionId", source.WorkSessionId),
                P("@Version", source.Version),
                P("@Key", idempotencyKey), P("@Hash", hash),
                P("@DocumentId", upload.DocumentId),
                P("@Payload", PosSaleContractSerializer.Serialize(upload)),
                P("@Now", now), P("@AuditTime", now.UtcDateTime)
            ], ct);
        await transaction.CommitAsync(ct);
        return new(upload, false);
    }

    public async Task MarkRestaurantResultAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesRestaurantCheckoutSource source,
        Guid documentId, string status, CancellationToken ct)
    {
        if (status is not ("Completed" or "FiscalConflict"))
            throw new ArgumentOutOfRangeException(nameof(status));
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand("""
            DECLARE @Prior nvarchar(24);
            SELECT @Prior=Status
            FROM dbo.OnlineSalesCheckoutReceipts WITH(UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId
              AND RestaurantAccountId=@AccountId
              AND CheckoutUserId=@UserId
              AND WorkSessionId=@WorkSessionId
              AND DocumentId=@DocumentId;
            IF @Prior IS NULL OR @Prior NOT IN (N'Prepared',@Status)
              THROW 51085,N'La emisión no pertenece a esta sesión.',1;
            IF @Prior=@Status RETURN;

            UPDATE dbo.OnlineSalesCheckoutReceipts
            SET Status=@Status,
                CompletedAt=CASE WHEN @Status=N'Completed' THEN @Now ELSE NULL END
            WHERE BusinessId=@BusinessId
              AND RestaurantAccountId=@AccountId
              AND CheckoutUserId=@UserId
              AND WorkSessionId=@WorkSessionId
              AND DocumentId=@DocumentId
              AND Status=N'Prepared';

            UPDATE sales.RestaurantAccounts
            SET Status=N'Released',InvoicedAt=@Now,ReleasedAt=@Now,UpdatedAt=@Now
            WHERE @Status=N'Completed'
              AND AccountId=@AccountId AND BusinessId=@BusinessId
              AND TenantId=@TenantId AND Status=N'Issuing';

            INSERT dbo.AuditLogs(
              AuditLogId,UserId,TenantId,BusinessId,Action,
              EntityType,EntityId,NewValues,CorrelationId,Timestamp)
            VALUES(NEWID(),@UserId,@TenantId,@BusinessId,
                   N'RestaurantCheckoutResult',N'RestaurantAccount',
                   CONVERT(nvarchar(100),@AccountId),@Status,
                   CONVERT(nvarchar(100),@DocumentId),@AuditTime);
            """, connection, transaction);
        var now = time.GetUtcNow();
        command.Parameters.AddRange([
            P("@Status", status), P("@Now", now),
            P("@BusinessId", source.BusinessId),
            P("@TenantId", user.TenantId),
            P("@AccountId", source.AccountId),
            P("@UserId", user.UserId),
            P("@WorkSessionId", source.WorkSessionId),
            P("@DocumentId", documentId),
            P("@AuditTime", now.UtcDateTime)
        ]);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task AbandonUnissuedRestaurantAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesRestaurantCheckoutSource source,
        Guid documentId, CancellationToken ct)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection
            .BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await using var command = new SqlCommand("""
            DECLARE @Deleted table(DocumentId uniqueidentifier);
            DELETE component
            FROM sales.RestaurantRecipeConsumptionLines component
            JOIN sales.RestaurantCheckoutLines mapping
              ON mapping.DocumentId=component.DocumentId
             AND mapping.LineNumber=component.SaleLineNumber
            WHERE mapping.DocumentId=@DocumentId
              AND mapping.BusinessId=@BusinessId
              AND mapping.AccountId=@AccountId
              AND NOT EXISTS (SELECT 1 FROM dbo.SalesDocuments document
                              WHERE document.DocumentId=@DocumentId);
            DELETE mapping
            FROM sales.RestaurantCheckoutLines mapping
            WHERE mapping.DocumentId=@DocumentId
              AND mapping.BusinessId=@BusinessId
              AND mapping.AccountId=@AccountId
              AND NOT EXISTS (SELECT 1 FROM dbo.SalesDocuments document
                              WHERE document.DocumentId=@DocumentId);

            DELETE receipt
            OUTPUT deleted.DocumentId INTO @Deleted
            FROM dbo.OnlineSalesCheckoutReceipts receipt
            WHERE receipt.BusinessId=@BusinessId
              AND receipt.RestaurantAccountId=@AccountId
              AND receipt.CheckoutUserId=@UserId
              AND receipt.WorkSessionId=@WorkSessionId
              AND receipt.DocumentId=@DocumentId
              AND receipt.Status=N'Prepared'
              AND NOT EXISTS(
                SELECT 1 FROM dbo.SalesDocuments document
                WHERE document.DocumentId=receipt.DocumentId);

            IF EXISTS(SELECT 1 FROM @Deleted)
            BEGIN
              UPDATE sales.RestaurantAccounts
              SET Status=N'Open',UpdatedAt=@Now
              WHERE AccountId=@AccountId AND BusinessId=@BusinessId
                AND TenantId=@TenantId AND Status=N'Issuing';
              DECLARE @Released bit;
              EXEC dbo.TenantDianDocumentQuotaReserve
                @BusinessId=@BusinessId,@DocumentId=@DocumentId,
                @DocumentKind=N'Invoice',@Now=@Now,
                @Reserved=@Released OUTPUT,@Release=1;
            END;
            """, connection, transaction);
        command.Parameters.AddRange([
            P("@BusinessId", source.BusinessId),
            P("@TenantId", user.TenantId),
            P("@AccountId", source.AccountId),
            P("@UserId", user.UserId),
            P("@WorkSessionId", source.WorkSessionId),
            P("@DocumentId", documentId),
            P("@Now", time.GetUtcNow())
        ]);
        await command.ExecuteNonQueryAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
