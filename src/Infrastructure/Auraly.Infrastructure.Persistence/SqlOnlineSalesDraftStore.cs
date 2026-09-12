using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.Application.Inventory;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Auraly.Contracts.Authorization;
using Auraly.Domain.Inventory;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time,
    SqlInventoryOperationStore inventoryOperations) : IOnlineSalesDraftStore, IOnlineSalesCheckoutStore,
    IOnlineSalesHistoryStore
{
    public async Task<OnlineSalesDraft> GetOrCreateActiveAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext requested,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);

        var context = await ResolveOnlineContextAsync(
            connection, transaction, user, requested, cancellationToken);

        var draftId = await FindActiveAsync(
            connection, transaction, context.BusinessId,
            context.WorkSessionId, user.UserId, cancellationToken);
        if (draftId is null)
        {
            draftId = ids.NewId();
            var now = time.GetUtcNow();
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDrafts(
                  SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
                  Status,Version,CreatedAt,UpdatedAt)
                VALUES(
                  @DraftId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,
                  N'Active',1,@Now,@Now);
                """,
                [
                    P("@DraftId", draftId.Value),
                    P("@BusinessId", context.BusinessId),
                    P("@WarehouseId", context.WarehouseId),
                    P("@WorkSessionId", context.WorkSessionId),
                    P("@UserId", user.UserId),
                    P("@Now", now)
                ],
                cancellationToken);
        }

        var result = await ReadDraftAsync(
            connection, transaction, draftId.Value, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> AddProductAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        string selector,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "AddProduct";
        var hash = Hash($"{operation}|{draftId:D}|{selector}|{Invariant(quantity)}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        var productId = await ResolveProductIdAsync(
            connection, transaction, state.BusinessId,
            selector, cancellationToken);
        var product = await ReadProductAsync(
            connection, transaction, state.BusinessId, state.WarehouseId,
            productId, cancellationToken);
        DemandAllowedQuantity(product.AllowsFractionalSale, quantity);
        var totalQuantity = await ReadProductQuantityAsync(
            connection, transaction, draftId, productId, null, cancellationToken) + quantity;
        await DemandInventoryAsync(
            connection, transaction, state, draftId, productId, null,
            quantity, cancellationToken);
        await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDraftLines(
                  SalesDraftLineId,SalesDraftId,ProductId,ProductCode,Description,
                  UnitCode,TaxCode,TaxRate,Quantity,BaseUnitPrice,UnitPrice,DocumentUnitCost,
                  CurrencyCode,PriceSource,PriceChannelId,
                  DiscountAmount,PromotionDiscountAmount,Position)
                SELECT
                  @LineId,@DraftId,@ProductId,@ProductCode,@Description,
                  @UnitCode,@TaxCode,@TaxRate,@Quantity,@BaseUnitPrice,@UnitPrice,@DocumentUnitCost,
                  @CurrencyCode,@PriceSource,@PriceChannelId,
                  0,0,COALESCE(MAX(Position),0)+1
                FROM dbo.SalesDraftLines WHERE SalesDraftId=@DraftId;
                """,
                [
                    P("@LineId", ids.NewId()), P("@DraftId", draftId),
                    P("@ProductId", productId), P("@ProductCode", product.Code),
                    P("@Description", product.Name), P("@UnitCode", product.UnitCode),
                    P("@TaxCode", product.TaxCode), P("@TaxRate", product.TaxRate),
                    P("@Quantity", quantity), P("@BaseUnitPrice", product.UnitPrice),
                    P("@UnitPrice", TaxExclusive(product.UnitPrice, product.TaxRate)), P("@CurrencyCode", product.CurrencyCode),
                    P("@DocumentUnitCost", product.UnitCost),
                    P("@PriceSource", "Base"),
                    P("@PriceChannelId", null)
                ],
                cancellationToken);
        await RepriceDraftAsync(
            connection, transaction, state, draftId, state.CustomerId, cancellationToken);
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> ChangeQuantityAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "ChangeQuantity";
        var hash = Hash($"{operation}|{draftId:D}|{lineId:D}|{Invariant(quantity)}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        var line = await ReadLineProductAsync(
            connection, transaction, draftId, lineId, cancellationToken);
        var product = await ReadProductAsync(
            connection, transaction, state.BusinessId, state.WarehouseId, line.ProductId, cancellationToken);
        DemandAllowedQuantity(product.AllowsFractionalSale, quantity);
        await DemandInventoryAsync(
            connection, transaction, state, draftId, line.ProductId, lineId,
            quantity, cancellationToken);
        var affected = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDraftLines
            SET Quantity=@Quantity
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """,
            [
                P("@Quantity", quantity),
                P("@DraftId", draftId), P("@LineId", lineId)
            ],
            cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");

        await RepriceDraftAsync(
            connection, transaction, state, draftId, state.CustomerId, cancellationToken);

        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> SetDiscountAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        decimal discount,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "SetDiscount";
        var hash = Hash($"{operation}|{draftId:D}|{lineId:D}|{Invariant(discount)}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        DemandActiveVersion(state, expectedVersion);
        var affected = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDraftLines
            SET DiscountAmount=@Discount
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId
              AND @Discount<=Quantity*UnitPrice-PromotionDiscountAmount;
            """,
            [P("@Discount", discount), P("@DraftId", draftId), P("@LineId", lineId)],
            cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "El descuento supera el valor de la línea o la línea no existe.");
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> UpdateLinesAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        IReadOnlyList<UpdateOnlineSalesDraftLineRequest> lines,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "UpdateLines";
        var payload = string.Join('|', lines
            .OrderBy(line => line.LineId)
            .Select(line => $"{line.LineId:D}:{line.Description.Trim()}:{Invariant(line.UnitPrice)}:{Invariant(line.Discount)}:{Invariant(line.DocumentUnitCost)}"));
        var hash = Hash($"{operation}|{draftId:D}|{payload}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        var currentDraft = await ReadDraftAsync(
            connection, transaction, draftId, cancellationToken);
        var activeLines = await ReadLineProductsAsync(
            connection, transaction, draftId, cancellationToken);
        if (lines.Count != activeLines.Count ||
            activeLines.Any(current => lines.All(line => line.LineId != current.LineId)))
            throw new OnlineSalesDraftValidationException(
                "Debes enviar exactamente todas las líneas de la venta activa.");
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesChangeDescription) &&
            lines.Any(line => !string.Equals(
                line.Description.Trim(),
                currentDraft.Lines.Single(current => current.LineId == line.LineId).Description,
                StringComparison.Ordinal)))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesChangeDescription}' is required.");

        foreach (var line in lines)
        {
            var current = activeLines.Single(value => value.LineId == line.LineId);
            var managesStock = await ProductManagesStockAsync(
                connection, transaction, state.BusinessId, current.ProductId, cancellationToken);
            if (!managesStock && line.DocumentUnitCost < 0)
                throw new OnlineSalesDraftValidationException("El costo de la línea no puede ser negativo.");
            var documentUnitCost = managesStock
                ? current.DocumentUnitCost
                : line.DocumentUnitCost;
            if (line.Discount > current.Quantity * line.UnitPrice)
                throw new OnlineSalesDraftValidationException(
                    "El descuento no puede superar el valor de la línea.");
            var affected = await ExecuteAsync(connection, transaction, """
                UPDATE dbo.SalesDraftLines
                SET Description=@Description,UnitPrice=@UnitPrice,DocumentUnitCost=@DocumentUnitCost,
                    DiscountAmount=@Discount,
                    PriceSource=CASE WHEN UnitPrice<>@UnitPrice THEN N'Manual' ELSE PriceSource END,
                    PriceChannelId=CASE WHEN UnitPrice<>@UnitPrice THEN NULL ELSE PriceChannelId END,
                    PromotionDiscountAmount=CASE WHEN UnitPrice<>@UnitPrice THEN 0 ELSE PromotionDiscountAmount END
                WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
                """,
                [
                    P("@Description", line.Description.Trim()),
                    P("@UnitPrice", line.UnitPrice),
                    P("@DocumentUnitCost", documentUnitCost),
                    P("@Discount", line.Discount),
                    P("@DraftId", draftId),
                    P("@LineId", line.LineId)
                ], cancellationToken);
            if (affected != 1)
                throw new OnlineSalesDraftValidationException(
                    "Una línea ya no pertenece a la venta activa.");
        }

        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> RemoveLineAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "RemoveLine";
        var hash = Hash($"{operation}|{draftId:D}|{lineId:D}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        DemandActiveVersion(state, expectedVersion);
        await ReadLineProductAsync(
            connection, transaction, draftId, lineId, cancellationToken);
        var affected = await ExecuteAsync(connection, transaction, """
            DELETE dbo.SalesDraftLines
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """,
            [P("@DraftId", draftId), P("@LineId", lineId)], cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");
        await RepriceDraftAsync(
            connection, transaction, state, draftId, state.CustomerId, cancellationToken);
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesCustomerSelection> SelectCustomerAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid? customerId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "SelectCustomer";
        var hash = Hash($"{operation}|{draftId:D}|{customerId?.ToString("D") ?? "Final"}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            var replayedCustomer = await ReadCustomerAsync(
                connection, transaction, state.BusinessId,
                replay.CustomerId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(replay, replayedCustomer);
        }
        DemandActiveVersion(state, expectedVersion);
        var customer = await ReadCustomerAsync(
            connection, transaction, state.BusinessId,
            customerId, cancellationToken);
        if (customerId is not null && customer is null)
            throw new OnlineSalesDraftValidationException(
                "El cliente no está disponible para este negocio.");

        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts SET CustomerId=@CustomerId
            WHERE SalesDraftId=@DraftId;
            """,
            [P("@CustomerId", customerId), P("@DraftId", draftId)], cancellationToken);
        await RepriceDraftAsync(
            connection, transaction, state, draftId, customerId, cancellationToken);
        var version = await AdvanceVersionAsync(
            connection, transaction, draftId, expectedVersion, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, draftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, draftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(result, customer);
    }

    public async Task<OnlineSalesDraft> ResetAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "Reset";
        var hash = Hash($"{operation}|{draftId:D}|{expectedVersion}");
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            (SqlTransaction)await connection.BeginTransactionAsync(
                IsolationLevel.Serializable, cancellationToken);
        var state = await LockDraftAsync(
            connection, transaction, user, draftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, state.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }

        DemandActiveVersion(state, expectedVersion);
        var now = time.GetUtcNow();
        await ExecuteAsync(connection, transaction, """
            UPDATE claim
            SET ReleasedAt=@Now
            FROM dbo.OrderClaims claim
            JOIN dbo.SalesDrafts draft ON draft.SourceOrderId=claim.OrderId
            WHERE draft.SalesDraftId=@DraftId AND claim.ReleasedAt IS NULL;

            UPDATE dbo.SalesDrafts
            SET Status=N'Deleted',SourceOrderId=NULL,DeletedAt=@Now,UpdatedAt=@Now,Version=Version+1
            WHERE SalesDraftId=@DraftId AND Version=@ExpectedVersion;
            """,
            [P("@Now", now), P("@DraftId", draftId), P("@ExpectedVersion", expectedVersion)],
            cancellationToken);
        var nextId = ids.NewId();
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDrafts(
              SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
              Status,Version,CreatedAt,UpdatedAt)
            VALUES(
              @NextId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,
              N'Active',1,@Now,@Now);
            """,
            [
                P("@NextId", nextId), P("@BusinessId", state.BusinessId),
                P("@WarehouseId", state.WarehouseId), P("@WorkSessionId", state.WorkSessionId), P("@UserId", user.UserId),
                P("@Now", now)
            ],
            cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, nextId,
            idempotencyKey, operation, hash, 1, cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, nextId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<OnlineSalesDraft?> ReplayAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string key,
        string operation,
        string hash,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftId,Operation,RequestHash
            FROM dbo.SalesDraftMutationReceipts WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND IdempotencyKey=@Key;
            """;
        command.Parameters.AddRange([P("@BusinessId", businessId), P("@Key", key)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var resultDraftId = reader.GetGuid(0);
        var storedOperation = reader.GetString(1);
        var storedHash = reader.GetString(2);
        await reader.DisposeAsync();
        if (!string.Equals(operation, storedOperation, StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(hash),
                Encoding.ASCII.GetBytes(storedHash)))
            throw new OnlineSalesDraftIdempotencyException(
                "La clave idempotente ya fue usada por otra mutación.");
        return await ReadDraftAsync(connection, transaction, resultDraftId, ct);
    }

    private async Task SaveReceiptAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid resultDraftId,
        string key,
        string operation,
        string hash,
        long version,
        CancellationToken ct) =>
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDraftMutationReceipts(
              SalesDraftMutationReceiptId,BusinessId,SalesDraftId,IdempotencyKey,
              Operation,RequestHash,ResultVersion,CreatedAt)
            VALUES(@Id,@BusinessId,@DraftId,@Key,@Operation,@Hash,@Version,@Now);
            """,
            [
                P("@Id", ids.NewId()), P("@BusinessId", businessId),
                P("@DraftId", resultDraftId), P("@Key", key),
                P("@Operation", operation), P("@Hash", hash),
                P("@Version", version), P("@Now", time.GetUtcNow())
            ],
            ct);

    private static async Task<DraftState> LockDraftAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.BusinessId,d.WarehouseId,d.WorkSessionId,d.Version,d.Status,
                   d.CustomerId,w.AllowNegativeStockSales,d.SourceOrderId
            FROM dbo.SalesDrafts d WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
            JOIN dbo.Warehouses w ON w.WarehouseId=d.WarehouseId
            WHERE d.SalesDraftId=@DraftId AND d.UserId=@UserId
              AND b.TenantId=@TenantId;
            """;
        command.Parameters.AddRange([
            P("@DraftId", draftId), P("@UserId", user.UserId),
            P("@TenantId", user.TenantId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "El borrador no pertenece al usuario autenticado.");
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetInt64(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5),
            reader.GetBoolean(6),reader.IsDBNull(7) ? null : reader.GetGuid(7));
    }

    private static void DemandActiveVersion(DraftState state, long expectedVersion)
    {
        if (!string.Equals(state.Status, "Active", StringComparison.Ordinal))
            throw new OnlineSalesDraftValidationException(
                "El borrador ya no está activo.");
        if (state.Version != expectedVersion)
            throw new OnlineSalesDraftConcurrencyException(
                $"El borrador cambió. Versión actual: {state.Version}.");
    }

    private async Task<long> AdvanceVersionAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        long expectedVersion,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE dbo.SalesDrafts
            SET Version=Version+1,UpdatedAt=@Now
            OUTPUT inserted.Version
            WHERE SalesDraftId=@DraftId AND Version=@ExpectedVersion AND Status=N'Active';
            """;
        command.Parameters.AddRange([
            P("@Now", now), P("@DraftId", draftId), P("@ExpectedVersion", expectedVersion)
        ]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is long version
            ? version
            : throw new OnlineSalesDraftConcurrencyException(
                "El borrador fue modificado por otra ventana.");
    }

    private static async Task<ProductSnapshot> ReadProductAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid warehouseId,
        Guid productId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            ;WITH ProductCategoryAncestors AS
            (
              SELECT category.ProductCategoryId,category.ParentProductCategoryId
              FROM dbo.Products scopedProduct
              JOIN dbo.ProductCategories category ON category.ProductCategoryId=scopedProduct.ProductCategoryId
              WHERE scopedProduct.ProductId=@ProductId AND category.BusinessId=@BusinessId
              UNION ALL
              SELECT parent.ProductCategoryId,parent.ParentProductCategoryId
              FROM dbo.ProductCategories parent
              JOIN ProductCategoryAncestors child ON child.ParentProductCategoryId=parent.ProductCategoryId
              WHERE parent.BusinessId=@BusinessId
            )
            SELECT COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),
                   p.Name,COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
                   COALESCE(t.Code,N'01'),COALESCE(t.Rate,0),
                   price.Amount,
                   price.CurrencyCode,p.AllowsFractionalSale,
                    COALESCE(NULLIF(balance.AverageUnitCost,0),price.CostBasisAmount,0),
                    CAST(CASE WHEN p.ManageStock=1 OR inventoryLink.ProductLinkId IS NOT NULL THEN 1 ELSE 0 END AS bit),
                    p.CategoryName,p.ProductCategoryId,p.ProductBrandId,
                    COALESCE((SELECT STRING_AGG(CONVERT(NVARCHAR(MAX),ancestor.ProductCategoryId),N',')
                              FROM ProductCategoryAncestors ancestor),N''),
                    latestCost.Amount,
                    COALESCE(price.TargetMarginPercent,price.EffectiveMarginPercent)
            FROM dbo.Products p
            LEFT JOIN dbo.TaxProfiles t
              ON t.TaxProfileId=p.TaxProfileId AND t.IsActive=1
            CROSS APPLY (
              SELECT TOP(1) pp.Amount,pp.CurrencyCode,pp.CostBasisAmount,
                     pp.TargetMarginPercent,pp.EffectiveMarginPercent
              FROM dbo.ProductPrices pp
              WHERE pp.BusinessId=@BusinessId AND pp.ProductId=p.ProductId
                AND pp.IsActive=1 AND pp.ValidFrom<=SYSDATETIMEOFFSET()
                AND (pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY pp.ValidFrom DESC,pp.ProductPriceId
            ) price
            LEFT JOIN dbo.InventoryBalances balance ON balance.BusinessId=@BusinessId
              AND balance.ProductId=p.ProductId AND balance.WarehouseId=@WarehouseId
            LEFT JOIN dbo.ProductLinks inventoryLink
              ON inventoryLink.BusinessId=@BusinessId
             AND inventoryLink.ChildProductId=p.ProductId
             AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1
            OUTER APPLY
            (
              SELECT COALESCE(
                (SELECT TOP (1) latest.LatestUnitCost
                 FROM dbo.SupplierProductLatestCosts latest
                 WHERE latest.BusinessId=@BusinessId AND latest.ProductId=p.ProductId
                 ORDER BY latest.ObservedAt DESC,latest.SupplierId),
                price.CostBasisAmount,NULLIF(balance.AverageUnitCost,0),0) Amount
            ) latestCost
            WHERE p.ProductId=@ProductId AND p.IsActive=1
              AND (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId));
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@WarehouseId", warehouseId), P("@ProductId", productId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException(
                "El producto no está disponible para este negocio.");
        return new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetDecimal(4),
            reader.GetDecimal(5), reader.GetString(6), reader.GetBoolean(7),
            reader.GetDecimal(8), reader.GetBoolean(9),
            reader.IsDBNull(10) ? null : reader.GetString(10),
            reader.IsDBNull(11) ? null : reader.GetGuid(11),
            reader.IsDBNull(12) ? null : reader.GetGuid(12),
            reader.GetString(13).Split(',',StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToArray(),
            reader.GetDecimal(14),reader.IsDBNull(15) ? null : reader.GetDecimal(15));
    }

    private static async Task<ResolvedSalesExecutionContext> ResolveOnlineContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext requested,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.BusinessId,w.WarehouseId,s.WorkSessionId,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            JOIN dbo.Warehouses w
              ON w.WarehouseId=@WarehouseId AND w.BusinessId=b.BusinessId
             AND w.IsActive=1 AND w.UseForSales=1
            JOIN dbo.WorkSessions s
             ON s.WorkSessionId=@WorkSessionId
             AND s.BusinessId=b.BusinessId
             AND s.TenantId=@TenantId
             AND s.UserId=@UserId
             AND s.Status=N'Open'
            WHERE b.TenantId=@TenantId
              AND b.BusinessId=@BusinessId
              AND b.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId),
            P("@BusinessId", requested.BusinessId),
            P("@WarehouseId", requested.WarehouseId),
            P("@WorkSessionId", requested.WorkSessionId),
            P("@UserId", user.UserId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "La sesión de trabajo no pertenece al usuario y negocio autenticados.");
        return new(
            reader.GetGuid(0),
            reader.GetGuid(1),
            reader.GetGuid(2),
            reader.GetBoolean(3));
    }

    private static async Task<Guid?> FindActiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid workSessionId,
        Guid userId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftId FROM dbo.SalesDrafts WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND WorkSessionId=@WorkSessionId
              AND UserId=@UserId AND Status=N'Active';
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@WorkSessionId", workSessionId), P("@UserId", userId)
        ]);
        return await command.ExecuteScalarAsync(ct) is Guid value ? value : null;
    }

    private static async Task<DraftLineMatch?> FindProductLineAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        Guid productId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftLineId,Quantity FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId AND ProductId=@ProductId;
            """;
        command.Parameters.AddRange([P("@DraftId", draftId), P("@ProductId", productId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(reader.GetGuid(0), reader.GetDecimal(1)) : null;
    }

    private static async Task<Guid> ResolveProductIdAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        string selector,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT TOP(2) p.ProductId,
              CASE
                WHEN p.ProductId=TRY_CONVERT(uniqueidentifier,@Selector) THEN 0
                WHEN EXISTS (
                  SELECT 1 FROM dbo.ProductBarcodes b
                  WHERE b.ProductId=p.ProductId AND b.BusinessId=@BusinessId
                    AND b.IsActive=1 AND b.Barcode=@Selector) THEN 1
                WHEN p.ProductCode=@Selector THEN 2
                WHEN p.Sku=@Selector THEN 3
                WHEN p.Reference=@Selector THEN 4
                WHEN EXISTS (
                  SELECT 1 FROM dbo.ProductIdentifiers i
                  WHERE i.ProductId=p.ProductId AND i.BusinessId=@BusinessId
                    AND i.IsActive=1 AND i.Value=@Selector) THEN 5
                ELSE 6
              END AS MatchRank
            FROM dbo.Products p
            WHERE (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId)) AND p.IsActive=1
              AND (
                p.ProductId=TRY_CONVERT(uniqueidentifier,@Selector) OR
                p.ProductCode=@Selector OR p.Sku=@Selector OR
                p.Reference=@Selector OR p.Name=@Selector OR
                EXISTS (
                  SELECT 1 FROM dbo.ProductBarcodes b
                  WHERE b.ProductId=p.ProductId AND b.BusinessId=@BusinessId
                    AND b.IsActive=1 AND b.Barcode=@Selector) OR
                EXISTS (
                  SELECT 1 FROM dbo.ProductIdentifiers i
                  WHERE i.ProductId=p.ProductId AND i.BusinessId=@BusinessId
                    AND i.IsActive=1 AND i.Value=@Selector))
            ORDER BY MatchRank,p.ProductId;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@Selector", selector)
        ]);
        var matches = new List<(Guid ProductId, int Rank)>(2);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            matches.Add((reader.GetGuid(0), reader.GetInt32(1)));
        if (matches.Count == 0)
            throw new OnlineSalesDraftValidationException(
                "No se encontró un producto vendible con ese identificador.");
        if (matches.Count > 1 && matches[0].Rank == matches[1].Rank)
            throw new OnlineSalesDraftValidationException(
                "El identificador coincide con más de un producto; selecciónalo en el buscador.");
        return matches[0].ProductId;
    }

    private static async Task<DraftLineProduct> ReadLineProductAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        Guid lineId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftLineId,ProductId,Quantity,BaseUnitPrice,CurrencyCode,TaxRate,DocumentUnitCost,
                   UnitPrice,PriceSource
            FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """;
        command.Parameters.AddRange([P("@DraftId", draftId), P("@LineId", lineId)]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
            reader.GetDecimal(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6),
            reader.GetDecimal(7), reader.GetString(8));
    }

    private static async Task<IReadOnlyList<DraftLineProduct>> ReadLineProductsAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftLineId,ProductId,Quantity,BaseUnitPrice,CurrencyCode,TaxRate,DocumentUnitCost,
                   UnitPrice,PriceSource
            FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId ORDER BY Position,SalesDraftLineId;
            """;
        command.Parameters.Add(P("@DraftId", draftId));
        var result = new List<DraftLineProduct>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetString(4), reader.GetDecimal(5), reader.GetDecimal(6),
                reader.GetDecimal(7), reader.GetString(8)));
        return result;
    }

    private static async Task<decimal> ReadProductQuantityAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        Guid productId,
        Guid? excludedLineId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(Quantity),0)
            FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId AND ProductId=@ProductId
              AND (@ExcludedLineId IS NULL OR SalesDraftLineId<>@ExcludedLineId);
            """;
        command.Parameters.AddRange([
            P("@DraftId", draftId), P("@ProductId", productId),
            P("@ExcludedLineId", excludedLineId)
        ]);
        return Convert.ToDecimal(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<OnlineSalesCustomer?> ReadCustomerAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        CancellationToken ct)
    {
        if (customerId is null) return null;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.CustomerId,COALESCE(p.Identification,N''),
                   COALESCE(p.DisplayName,p.LegalName,
                            CONCAT(p.FirstName,N' ',p.LastName),N'Sin nombre'),
                   CASE WHEN s.ValidFrom<=SYSDATETIMEOFFSET()
                          AND(s.ValidUntil IS NULL OR s.ValidUntil>SYSDATETIMEOFFSET())
                        THEN s.PriceChannelId END,c.RequiresElectronicInvoice,
                   CAST(COALESCE(cp.IsCreditEnabled,0) AS bit),
                   CASE WHEN cp.CreditLimit IS NULL THEN NULL
                        ELSE CASE WHEN cp.CreditLimit-COALESCE(balance.Outstanding,0)<0 THEN 0
                                  ELSE cp.CreditLimit-COALESCE(balance.Outstanding,0) END END
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
            LEFT JOIN dbo.CustomerCreditProfiles cp ON cp.CustomerId=c.CustomerId AND cp.BusinessId=c.BusinessId
            OUTER APPLY(SELECT SUM(r.OutstandingAmount) Outstanding FROM dbo.Receivables r
                        WHERE r.CustomerId=c.CustomerId AND r.BusinessId=c.BusinessId
                          AND r.Status IN(N'Open',N'PartiallyPaid')) balance
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId
              AND c.IsActive=1 AND p.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@CustomerId", customerId), P("@BusinessId", businessId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new(
                reader.GetGuid(0), reader.GetString(1), reader.GetString(2).Trim(),
                reader.IsDBNull(3) ? null : reader.GetGuid(3),
                reader.GetBoolean(4), reader.GetBoolean(5),
                reader.IsDBNull(6) ? null : reader.GetDecimal(6))
            : null;
    }

    private static async Task DemandInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DraftState state,
        Guid draftId,
        Guid productId,
        Guid? excludedLineId,
        decimal selectedQuantity,
        CancellationToken ct)
    {
        if (state.WarehouseAllowsNegativeStock) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DECLARE @InventoryProductId UNIQUEIDENTIFIER;
            DECLARE @SelectedFactor DECIMAL(19,6);
            DECLARE @ManagesStock BIT;
            SELECT @InventoryProductId=COALESCE(link.ParentProductId,p.ProductId),
                   @SelectedFactor=COALESCE(NULLIF(link.InventoryFactor,0),1),
                   @ManagesStock=CAST(CASE WHEN p.ManageStock=1 OR link.ProductLinkId IS NOT NULL
                                           THEN 1 ELSE 0 END AS BIT)
            FROM dbo.Products p
            LEFT JOIN dbo.ProductLinks link
              ON link.BusinessId=@BusinessId AND link.ChildProductId=p.ProductId
             AND link.SharesInventory=1 AND link.IsActive=1
            WHERE p.ProductId=@ProductId
              AND (p.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
                   OR (p.TenantId IS NULL AND p.BusinessId=@BusinessId));

            DECLARE @Available DECIMAL(19,6)=COALESCE((
              SELECT balance.QuantityOnHand
              FROM dbo.InventoryBalances balance WITH (UPDLOCK,HOLDLOCK)
              WHERE balance.BusinessId=@BusinessId AND balance.WarehouseId=@WarehouseId
                AND balance.ProductId=@InventoryProductId),0);
            SELECT @InventoryProductId,@SelectedFactor,@ManagesStock,@Available;

            SELECT line.SalesDraftLineId,line.ProductId,line.Quantity,
                   COALESCE(lineLink.ParentProductId,line.ProductId),
                   COALESCE(NULLIF(lineLink.InventoryFactor,0),1),
                   CAST(CASE WHEN lineProduct.ManageStock=1 OR lineLink.ProductLinkId IS NOT NULL
                             THEN 1 ELSE 0 END AS BIT)
            FROM dbo.SalesDraftLines line WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.Products lineProduct ON lineProduct.ProductId=line.ProductId
            LEFT JOIN dbo.ProductLinks lineLink
              ON lineLink.BusinessId=@BusinessId AND lineLink.ChildProductId=line.ProductId
             AND lineLink.SharesInventory=1 AND lineLink.IsActive=1
            WHERE line.SalesDraftId=@DraftId
              AND COALESCE(lineLink.ParentProductId,line.ProductId)=@InventoryProductId
              AND (@ExcludedLineId IS NULL OR line.SalesDraftLineId<>@ExcludedLineId);
            """;
        command.Parameters.AddRange([
            P("@BusinessId", state.BusinessId), P("@WarehouseId", state.WarehouseId),
            P("@DraftId", draftId), P("@ProductId", productId),
            P("@ExcludedLineId", excludedLineId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException("El producto ya no está disponible.");
        var inventoryProductId = reader.GetGuid(0);
        var selectedFactor = reader.GetDecimal(1);
        var managesStock = reader.GetBoolean(2);
        var available = reader.GetDecimal(3);
        var demandLines = new List<InventoryDemandLine>();
        await reader.NextResultAsync(ct);
        while (await reader.ReadAsync(ct))
            demandLines.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(3),
                reader.GetDecimal(4), reader.GetDecimal(2), reader.GetBoolean(5)));
        demandLines.Add(new(
            Guid.Empty, productId, inventoryProductId, selectedFactor,
            selectedQuantity, managesStock));
        var demand = InventoryDemandResolver.Resolve(demandLines).SingleOrDefault();
        if (demand is not null && available < demand.RequiredInventoryQuantity)
            throw new OnlineSalesDraftValidationException(
                $"Inventario insuficiente. Disponible: {Invariant(InventoryDemandResolver.InProductUnits(available, selectedFactor))}.");
    }

    private static async Task<OnlineSalesDraft> ReadDraftAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        CancellationToken ct)
    {
        await using var header = connection.CreateCommand();
        header.Transaction = transaction;
        header.CommandText = """
            SELECT SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
                   CustomerId,SellerId,Status,Name,Reference,Observation,Version,UpdatedAt,
                   SourceOrderId
            FROM dbo.SalesDrafts WHERE SalesDraftId=@DraftId;
            """;
        header.Parameters.Add(P("@DraftId", draftId));
        await using var reader = await header.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException("El borrador no existe.");
        var values = new object[14];
        reader.GetValues(values);
        await reader.DisposeAsync();

        await using var details = connection.CreateCommand();
        details.Transaction = transaction;
        details.CommandText = """
            SELECT line.SalesDraftLineId,line.ProductId,line.ProductCode,line.Description,line.UnitCode,
                   line.TaxCode,line.TaxRate,line.Quantity,line.BaseUnitPrice,line.UnitPrice,line.CurrencyCode,
                   line.PriceSource,line.DiscountAmount,line.DocumentUnitCost,
                   CAST(CASE WHEN product.ManageStock=1 OR inventoryLink.ProductLinkId IS NOT NULL THEN 1 ELSE 0 END AS bit),
                   product.AllowsFractionalSale,
                   line.PromotionDiscountAmount
            FROM dbo.SalesDraftLines line
            JOIN dbo.Products product ON product.ProductId=line.ProductId
            LEFT JOIN dbo.ProductLinks inventoryLink
              ON inventoryLink.BusinessId=(SELECT BusinessId FROM dbo.SalesDrafts WHERE SalesDraftId=@DraftId)
             AND inventoryLink.ChildProductId=line.ProductId
             AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1
            WHERE line.SalesDraftId=@DraftId ORDER BY line.Position,line.SalesDraftLineId;
            """;
        details.Parameters.Add(P("@DraftId", draftId));
        var lines = new List<OnlineSalesDraftLine>();
        await using var lineReader = await details.ExecuteReaderAsync(ct);
        while (await lineReader.ReadAsync(ct))
        {
            var quantity = lineReader.GetDecimal(7);
            var price = lineReader.GetDecimal(9);
            var discount = lineReader.GetDecimal(12);
            var promotionDiscount = lineReader.GetDecimal(16);
            var net = decimal.Round(quantity * price - discount - promotionDiscount, 2, MidpointRounding.AwayFromZero);
            var tax = decimal.Round(net * lineReader.GetDecimal(6) / 100m, 2, MidpointRounding.AwayFromZero);
            lines.Add(new(
                lineReader.GetGuid(0), lineReader.GetGuid(1), lineReader.GetString(2),
                lineReader.GetString(3), lineReader.GetString(4), lineReader.GetString(5),
                lineReader.GetDecimal(6), quantity, lineReader.GetDecimal(8), price,
                lineReader.GetString(10), lineReader.GetString(11), discount,
                lineReader.GetDecimal(13), !lineReader.GetBoolean(14), lineReader.GetBoolean(15),
                net, tax, net + tax, promotionDiscount));
        }
        return new(
            (Guid)values[0], (Guid)values[1], (Guid)values[2], (Guid)values[3],
            (Guid)values[4],
            values[5] is DBNull ? null : (Guid)values[5],
            values[6] is DBNull ? null : (Guid)values[6],
            (string)values[7],
            values[8] is DBNull ? null : (string)values[8],
            values[9] is DBNull ? null : (string)values[9],
            values[10] is DBNull ? null : (string)values[10],
            (long)values[11], (DateTimeOffset)values[12],
            lines, lines.Sum(line => line.Net), lines.Sum(line => line.Tax),
            lines.Sum(line => line.Total),
            values[13] is DBNull ? null : (Guid)values[13]);
    }

    private static async Task<int> ExecuteAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        string sql,
        SqlParameter[] parameters,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static SqlParameter P(string name, object? value) => new(name, value ?? DBNull.Value);
    private static string Invariant(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static decimal TaxExclusive(decimal grossPrice, decimal taxRate) =>
        decimal.Round(grossPrice / (1m + taxRate / 100m), 6,
            MidpointRounding.AwayFromZero);

    private sealed record DraftState(
        Guid BusinessId,
        Guid WarehouseId,
        Guid WorkSessionId,
        long Version,
        string Status,
        Guid? CustomerId,
        bool WarehouseAllowsNegativeStock,
        Guid? SourceOrderId);
    private sealed record DraftLineMatch(Guid LineId, decimal Quantity);
    private sealed record DraftLineProduct(
        Guid LineId,
        Guid ProductId,
        decimal Quantity,
        decimal BaseUnitPrice,
        string CurrencyCode,
        decimal TaxRate,
        decimal DocumentUnitCost,
        decimal UnitPrice,
        string PriceSource);
    private sealed record ProductSnapshot(
        string Code,
        string Name,
        string UnitCode,
        string TaxCode,
        decimal TaxRate,
        decimal UnitPrice,
        string CurrencyCode,
        bool AllowsFractionalSale,
        decimal UnitCost,
        bool ManagesStock,
        string? CategoryName,
        Guid? ProductCategoryId,
        Guid? ProductBrandId,
        IReadOnlyCollection<Guid> ProductCategoryAncestorIds,
        decimal LatestUnitCost,
        decimal? TargetMarginPercent);

    private static async Task<bool> ProductManagesStockAsync(
        SqlConnection connection, SqlTransaction transaction, Guid businessId, Guid productId,
        CancellationToken ct)
    {
        await using var command = new SqlCommand(
            """
            SELECT CAST(CASE WHEN product.ManageStock=1 OR inventoryLink.ProductLinkId IS NOT NULL THEN 1 ELSE 0 END AS bit)
            FROM dbo.Products product
            LEFT JOIN dbo.ProductLinks inventoryLink
              ON inventoryLink.BusinessId=@BusinessId
             AND inventoryLink.ChildProductId=product.ProductId
             AND inventoryLink.SharesInventory=1 AND inventoryLink.IsActive=1
            WHERE product.TenantId=(SELECT TenantId FROM dbo.Businesses WHERE BusinessId=@BusinessId)
              AND product.ProductId=@ProductId AND product.IsActive=1;
            """,
            connection, transaction);
        command.Parameters.AddRange([P("@BusinessId", businessId), P("@ProductId", productId)]);
        return await command.ExecuteScalarAsync(ct) is bool value
            ? value
            : throw new OnlineSalesDraftValidationException("El producto ya no está disponible.");
    }

    private static void DemandAllowedQuantity(bool allowsFractionalSale, decimal quantity)
    {
        if (!allowsFractionalSale && quantity != decimal.Truncate(quantity))
            throw new OnlineSalesDraftValidationException(
                "Este producto solo se vende en unidades completas.");
    }
    private sealed record ResolvedSalesExecutionContext(
        Guid BusinessId,
        Guid WarehouseId,
        Guid WorkSessionId,
        bool WarehouseAllowsNegativeStockSales);
}
