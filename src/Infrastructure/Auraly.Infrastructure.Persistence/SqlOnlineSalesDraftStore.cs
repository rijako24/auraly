using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time) : IOnlineSalesDraftStore, IOnlineSalesCheckoutStore,
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
            context.RegisterId, user.UserId, cancellationToken);
        if (draftId is null)
        {
            draftId = ids.NewId();
            var now = time.GetUtcNow();
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDrafts(
                  SalesDraftId,BusinessId,WarehouseId,RegisterId,UserId,
                  Status,Version,CreatedAt,UpdatedAt)
                VALUES(
                  @DraftId,@BusinessId,@WarehouseId,@RegisterId,@UserId,
                  N'Active',1,@Now,@Now);
                """,
                [
                    P("@DraftId", draftId.Value),
                    P("@BusinessId", context.BusinessId),
                    P("@WarehouseId", context.WarehouseId),
                    P("@RegisterId", context.RegisterId),
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
        Guid productId,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "AddProduct";
        var hash = Hash($"{operation}|{draftId:D}|{productId:D}|{Invariant(quantity)}");
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
        var product = await ReadProductAsync(
            connection, transaction, state.BusinessId,
            productId, cancellationToken);
        var existing = await FindProductLineAsync(
            connection, transaction, draftId, productId, cancellationToken);
        var totalQuantity = (existing?.Quantity ?? 0m) + quantity;
        await DemandInventoryAsync(
            connection, transaction, state, productId,
            totalQuantity, cancellationToken);
        var price = await ResolvePriceAsync(
            connection, transaction, state.BusinessId, state.CustomerId,
            productId, totalQuantity, product.UnitPrice,
            product.CurrencyCode, cancellationToken);
        if (existing is null)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDraftLines(
                  SalesDraftLineId,SalesDraftId,ProductId,ProductCode,Description,
                  UnitCode,TaxCode,TaxRate,Quantity,BaseUnitPrice,UnitPrice,
                  CurrencyCode,PriceSource,PriceListId,PriceChannelId,
                  DiscountAmount,Position)
                SELECT
                  @LineId,@DraftId,@ProductId,@ProductCode,@Description,
                  @UnitCode,@TaxCode,@TaxRate,@Quantity,@BaseUnitPrice,@UnitPrice,
                  @CurrencyCode,@PriceSource,@PriceListId,@PriceChannelId,
                  0,COALESCE(MAX(Position),0)+1
                FROM dbo.SalesDraftLines WHERE SalesDraftId=@DraftId;
                """,
                [
                    P("@LineId", ids.NewId()), P("@DraftId", draftId),
                    P("@ProductId", productId), P("@ProductCode", product.Code),
                    P("@Description", product.Name), P("@UnitCode", product.UnitCode),
                    P("@TaxCode", product.TaxCode), P("@TaxRate", product.TaxRate),
                    P("@Quantity", quantity), P("@BaseUnitPrice", product.UnitPrice),
                    P("@UnitPrice", price.Amount), P("@CurrencyCode", price.CurrencyCode),
                    P("@PriceSource", price.Source), P("@PriceListId", price.PriceListId),
                    P("@PriceChannelId", price.PriceChannelId)
                ],
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.SalesDraftLines
                SET Quantity=@TotalQuantity,BaseUnitPrice=@BaseUnitPrice,
                    UnitPrice=@UnitPrice,CurrencyCode=@CurrencyCode,
                    PriceSource=@PriceSource,PriceListId=@PriceListId,
                    PriceChannelId=@PriceChannelId
                WHERE SalesDraftLineId=@LineId AND SalesDraftId=@DraftId;
                """,
                [
                    P("@TotalQuantity", totalQuantity), P("@BaseUnitPrice", product.UnitPrice),
                    P("@UnitPrice", price.Amount), P("@CurrencyCode", price.CurrencyCode),
                    P("@PriceSource", price.Source), P("@PriceListId", price.PriceListId),
                    P("@PriceChannelId", price.PriceChannelId),
                    P("@LineId", existing.LineId), P("@DraftId", draftId)
                ],
                cancellationToken);
        }

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

    public async Task<OnlineSalesDraft> CaptureAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        string value,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        var productId = await ResolveProductIdAsync(
            connection, user, draftId, value, cancellationToken);
        return await AddProductAsync(
            user, draftId, productId, quantity, expectedVersion,
            idempotencyKey, cancellationToken);
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
        await DemandInventoryAsync(
            connection, transaction, state, line.ProductId,
            quantity, cancellationToken);
        var price = await ResolvePriceAsync(
            connection, transaction, state.BusinessId, state.CustomerId,
            line.ProductId, quantity, line.BaseUnitPrice,
            line.CurrencyCode, cancellationToken);
        var affected = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDraftLines
            SET Quantity=@Quantity,UnitPrice=@UnitPrice,CurrencyCode=@CurrencyCode,
                PriceSource=@PriceSource,PriceListId=@PriceListId,
                PriceChannelId=@PriceChannelId
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """,
            [
                P("@Quantity", quantity), P("@UnitPrice", price.Amount),
                P("@CurrencyCode", price.CurrencyCode), P("@PriceSource", price.Source),
                P("@PriceListId", price.PriceListId),
                P("@PriceChannelId", price.PriceChannelId),
                P("@DraftId", draftId), P("@LineId", lineId)
            ],
            cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");

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
              AND @Discount<=Quantity*UnitPrice;
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
        var affected = await ExecuteAsync(connection, transaction, """
            DELETE dbo.SalesDraftLines
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """,
            [P("@DraftId", draftId), P("@LineId", lineId)], cancellationToken);
        if (affected != 1)
            throw new OnlineSalesDraftValidationException(
                "La línea no pertenece al borrador activo.");
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
        var lines = await ReadLineProductsAsync(
            connection, transaction, draftId, cancellationToken);
        foreach (var line in lines)
        {
            var price = await ResolvePriceAsync(
                connection, transaction, state.BusinessId, customerId,
                line.ProductId, line.Quantity, line.BaseUnitPrice,
                line.CurrencyCode, cancellationToken);
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.SalesDraftLines
                SET UnitPrice=@UnitPrice,CurrencyCode=@CurrencyCode,
                    PriceSource=@PriceSource,PriceListId=@PriceListId,
                    PriceChannelId=@PriceChannelId
                WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
                """,
                [
                    P("@UnitPrice", price.Amount), P("@CurrencyCode", price.CurrencyCode),
                    P("@PriceSource", price.Source), P("@PriceListId", price.PriceListId),
                    P("@PriceChannelId", price.PriceChannelId),
                    P("@DraftId", draftId), P("@LineId", line.LineId)
                ], cancellationToken);
        }
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
            UPDATE dbo.SalesDrafts
            SET Status=N'Deleted',DeletedAt=@Now,UpdatedAt=@Now,Version=Version+1
            WHERE SalesDraftId=@DraftId AND Version=@ExpectedVersion;
            """,
            [P("@Now", now), P("@DraftId", draftId), P("@ExpectedVersion", expectedVersion)],
            cancellationToken);
        var nextId = ids.NewId();
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDrafts(
              SalesDraftId,BusinessId,WarehouseId,RegisterId,UserId,
              Status,Version,CreatedAt,UpdatedAt)
            VALUES(
              @NextId,@BusinessId,@WarehouseId,@RegisterId,@UserId,
              N'Active',1,@Now,@Now);
            """,
            [
                P("@NextId", nextId), P("@BusinessId", state.BusinessId),
                P("@WarehouseId", state.WarehouseId), P("@RegisterId", state.RegisterId), P("@UserId", user.UserId),
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
            SELECT d.BusinessId,d.WarehouseId,d.RegisterId,d.Version,d.Status,
                   d.CustomerId,w.AllowNegativeStockSales
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
            reader.GetBoolean(6));
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
        Guid productId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),
                   p.Name,COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
                   COALESCE(t.Code,N'01'),COALESCE(t.Rate,0),
                   COALESCE(price.Amount,p.UnitPrice),
                   COALESCE(price.CurrencyCode,NULLIF(p.Currency,N''),N'COP')
            FROM dbo.Products p
            LEFT JOIN dbo.TaxProfiles t
              ON t.TaxProfileId=p.TaxProfileId AND t.BusinessId=p.BusinessId AND t.IsActive=1
            OUTER APPLY (
              SELECT TOP(1) pp.Amount,pp.CurrencyCode
              FROM dbo.ProductPrices pp
              WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId
                AND pp.IsActive=1 AND pp.ValidFrom<=SYSDATETIMEOFFSET()
                AND (pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY pp.ValidFrom DESC,pp.ProductPriceId
            ) price
            WHERE p.BusinessId=@BusinessId AND p.ProductId=@ProductId AND p.IsActive=1;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@ProductId", productId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException(
                "El producto no está disponible para este negocio.");
        return new(
            reader.GetString(0), reader.GetString(1), reader.GetString(2),
            reader.GetString(3), reader.GetDecimal(4),
            reader.GetDecimal(5), reader.GetString(6));
    }

    private static async Task<ResolvedRegisterContext> ResolveOnlineContextAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext requested,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT b.BusinessId,b.Name,
                   r.RegisterId,r.Code,r.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            JOIN dbo.CashRegisters r
              ON r.BusinessId=b.BusinessId
             AND r.RegisterId=@RegisterId AND r.IsActive=1
            JOIN dbo.Warehouses w
              ON w.WarehouseId=r.WarehouseId AND w.BusinessId=r.BusinessId
             AND w.IsActive=1
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId AND b.IsActive=1
              AND NOT EXISTS(
                SELECT 1 FROM dbo.PosDevices d
                WHERE d.RegisterId=r.RegisterId AND d.IsActive=1);
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId), P("@BusinessId", requested.BusinessId),
            P("@RegisterId", requested.RegisterId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "La caja online no pertenece al contexto autenticado o está enrolada como POS Edge.");
        return new(
            reader.GetGuid(0), reader.GetString(1),
            reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
            reader.GetBoolean(8));
    }

    private static async Task<Guid?> FindActiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid registerId,
        Guid userId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftId FROM dbo.SalesDrafts WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND RegisterId=@RegisterId
              AND UserId=@UserId AND Status=N'Active';
            """;
        command.Parameters.AddRange([
            P("@BusinessId", businessId), P("@RegisterId", registerId), P("@UserId", userId)
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
        OnlineSalesUserIdentity user,
        Guid draftId,
        string value,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP(2) p.ProductId,
              CASE
                WHEN EXISTS (
                  SELECT 1 FROM dbo.ProductBarcodes b
                  WHERE b.ProductId=p.ProductId AND b.BusinessId=p.BusinessId
                    AND b.IsActive=1 AND b.Barcode=@Value) THEN 0
                WHEN p.ProductCode=@Value THEN 1
                WHEN p.Sku=@Value THEN 2
                WHEN p.Reference=@Value THEN 3
                ELSE 3
              END AS MatchRank
            FROM dbo.SalesDrafts d
            JOIN dbo.Businesses business
              ON business.BusinessId=d.BusinessId AND business.TenantId=@TenantId
            JOIN dbo.Products p ON p.BusinessId=d.BusinessId AND p.IsActive=1
            WHERE d.SalesDraftId=@DraftId AND d.UserId=@UserId AND d.Status=N'Active'
              AND (
                p.ProductCode=@Value OR p.Sku=@Value OR p.Reference=@Value OR
                EXISTS (
                  SELECT 1 FROM dbo.ProductBarcodes b
                  WHERE b.ProductId=p.ProductId AND b.BusinessId=p.BusinessId
                    AND b.IsActive=1 AND b.Barcode=@Value) OR
                EXISTS (
                  SELECT 1 FROM dbo.ProductIdentifiers i
                  WHERE i.ProductId=p.ProductId AND i.BusinessId=p.BusinessId
                    AND i.IsActive=1 AND i.Value=@Value))
            ORDER BY MatchRank,p.ProductId;
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId), P("@UserId", user.UserId),
            P("@DraftId", draftId), P("@Value", value)
        ]);
        var matches = new List<(Guid ProductId, int Rank)>(2);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            matches.Add((reader.GetGuid(0), reader.GetInt32(1)));
        if (matches.Count == 0)
            throw new OnlineSalesDraftValidationException(
                "No se encontró un producto vendible con ese código o referencia.");
        if (matches.Count > 1 && matches[0].Rank == matches[1].Rank)
            throw new OnlineSalesDraftValidationException(
                "El código o referencia identifica más de un producto.");
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
            SELECT SalesDraftLineId,ProductId,Quantity,BaseUnitPrice,CurrencyCode
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
            reader.GetDecimal(3), reader.GetString(4));
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
            SELECT SalesDraftLineId,ProductId,Quantity,BaseUnitPrice,CurrencyCode
            FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId ORDER BY Position,SalesDraftLineId;
            """;
        command.Parameters.Add(P("@DraftId", draftId));
        var result = new List<DraftLineProduct>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            result.Add(new(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetString(4)));
        return result;
    }

    private static async Task<ResolvedPrice> ResolvePriceAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid businessId,
        Guid? customerId,
        Guid productId,
        decimal quantity,
        decimal baseAmount,
        string baseCurrency,
        CancellationToken ct)
    {
        if (customerId is null)
            return new(baseAmount, baseCurrency, "Base", null, null);

        await using var assignment = connection.CreateCommand();
        assignment.Transaction = transaction;
        assignment.CommandText = """
            SELECT s.PriceListId,s.PriceChannelId
            FROM dbo.Customers c
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
            WHERE c.CustomerId=@CustomerId AND c.BusinessId=@BusinessId AND c.IsActive=1;
            """;
        assignment.Parameters.AddRange([
            P("@CustomerId", customerId), P("@BusinessId", businessId)
        ]);
        Guid? listId;
        Guid? channelId;
        await using (var reader = await assignment.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct))
                return new(baseAmount, baseCurrency, "Base", null, null);
            listId = reader.IsDBNull(0) ? null : reader.GetGuid(0);
            channelId = reader.IsDBNull(1) ? null : reader.GetGuid(1);
        }

        if (listId is not null)
        {
            await using var list = connection.CreateCommand();
            list.Transaction = transaction;
            list.CommandText = """
                SELECT TOP(1) i.Amount,i.CurrencyCode
                FROM dbo.PriceListItems i
                JOIN dbo.PriceLists l ON l.PriceListId=i.PriceListId
                WHERE i.PriceListId=@SourceId AND l.BusinessId=@BusinessId
                  AND l.IsActive=1 AND i.ProductId=@ProductId
                  AND i.IsActive=1 AND i.MinimumQuantity<=@Quantity
                  AND i.ValidFrom<=SYSDATETIMEOFFSET()
                  AND (i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET())
                ORDER BY i.MinimumQuantity DESC,i.ValidFrom DESC;
                """;
            list.Parameters.AddRange([
                P("@SourceId", listId), P("@BusinessId", businessId),
                P("@ProductId", productId), P("@Quantity", quantity)
            ]);
            await using var reader = await list.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new(
                    reader.GetDecimal(0), reader.GetString(1),
                    "PriceList", listId, null);
        }
        else if (channelId is not null)
        {
            await using var channel = connection.CreateCommand();
            channel.Transaction = transaction;
            channel.CommandText = """
                SELECT TOP(1) i.Amount,i.CurrencyCode
                FROM dbo.ResolvedPriceChannelItems i
                JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId
                WHERE i.PriceChannelId=@SourceId AND c.BusinessId=@BusinessId
                  AND c.IsActive=1 AND i.ProductId=@ProductId AND i.IsActive=1
                  AND i.ValidFrom<=SYSDATETIMEOFFSET()
                  AND (i.ValidUntil IS NULL OR i.ValidUntil>SYSDATETIMEOFFSET())
                  AND NOT EXISTS(
                    SELECT 1 FROM dbo.PriceChannelExclusions e
                    WHERE e.PriceChannelId=i.PriceChannelId AND e.ProductId=i.ProductId);
                """;
            channel.Parameters.AddRange([
                P("@SourceId", channelId), P("@BusinessId", businessId),
                P("@ProductId", productId)
            ]);
            await using var reader = await channel.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                return new(
                    reader.GetDecimal(0), reader.GetString(1),
                    "PriceChannel", null, channelId);
        }
        return new(baseAmount, baseCurrency, "Base", listId, channelId);
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
                   s.PriceListId,s.PriceChannelId
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
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
                reader.IsDBNull(4) ? null : reader.GetGuid(4))
            : null;
    }

    private static async Task DemandInventoryAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        DraftState state,
        Guid productId,
        decimal requestedQuantity,
        CancellationToken ct)
    {
        if (state.WarehouseAllowsNegativeStock) return;
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT COALESCE(SUM(QuantityChange),0)
            FROM dbo.InventoryMovements WITH (UPDLOCK,HOLDLOCK)
            WHERE BusinessId=@BusinessId AND WarehouseId=@WarehouseId
              AND ProductId=@ProductId;
            """;
        command.Parameters.AddRange([
            P("@BusinessId", state.BusinessId), P("@WarehouseId", state.WarehouseId),
            P("@ProductId", productId)
        ]);
        var available = Convert.ToDecimal(
            await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        if (available < requestedQuantity)
            throw new OnlineSalesDraftValidationException(
                $"Inventario insuficiente. Disponible: {Invariant(available)}.");
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
            SELECT SalesDraftId,BusinessId,WarehouseId,RegisterId,UserId,
                   CustomerId,SellerId,Status,Name,Reference,Observation,Version,UpdatedAt
            FROM dbo.SalesDrafts WHERE SalesDraftId=@DraftId;
            """;
        header.Parameters.Add(P("@DraftId", draftId));
        await using var reader = await header.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException("El borrador no existe.");
        var values = new object[13];
        reader.GetValues(values);
        await reader.DisposeAsync();

        await using var details = connection.CreateCommand();
        details.Transaction = transaction;
        details.CommandText = """
            SELECT SalesDraftLineId,ProductId,ProductCode,Description,UnitCode,
                   TaxCode,TaxRate,Quantity,BaseUnitPrice,UnitPrice,CurrencyCode,
                   PriceSource,DiscountAmount
            FROM dbo.SalesDraftLines
            WHERE SalesDraftId=@DraftId ORDER BY Position,SalesDraftLineId;
            """;
        details.Parameters.Add(P("@DraftId", draftId));
        var lines = new List<OnlineSalesDraftLine>();
        await using var lineReader = await details.ExecuteReaderAsync(ct);
        while (await lineReader.ReadAsync(ct))
        {
            var quantity = lineReader.GetDecimal(7);
            var price = lineReader.GetDecimal(9);
            var discount = lineReader.GetDecimal(12);
            var net = decimal.Round(quantity * price - discount, 2, MidpointRounding.AwayFromZero);
            var tax = decimal.Round(net * lineReader.GetDecimal(6) / 100m, 2, MidpointRounding.AwayFromZero);
            lines.Add(new(
                lineReader.GetGuid(0), lineReader.GetGuid(1), lineReader.GetString(2),
                lineReader.GetString(3), lineReader.GetString(4), lineReader.GetString(5),
                lineReader.GetDecimal(6), quantity, lineReader.GetDecimal(8), price,
                lineReader.GetString(10), lineReader.GetString(11), discount,
                net, tax, net + tax));
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
            lines.Sum(line => line.Total));
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

    private sealed record DraftState(
        Guid BusinessId,
        Guid WarehouseId,
        Guid RegisterId,
        long Version,
        string Status,
        Guid? CustomerId,
        bool WarehouseAllowsNegativeStock);
    private sealed record DraftLineMatch(Guid LineId, decimal Quantity);
    private sealed record DraftLineProduct(
        Guid LineId,
        Guid ProductId,
        decimal Quantity,
        decimal BaseUnitPrice,
        string CurrencyCode);
    private sealed record ResolvedPrice(
        decimal Amount, string CurrencyCode, string Source,
        Guid? PriceListId, Guid? PriceChannelId);
    private sealed record ProductSnapshot(
        string Code,
        string Name,
        string UnitCode,
        string TaxCode,
        decimal TaxRate,
        decimal UnitPrice,
        string CurrencyCode);
    private sealed record ResolvedRegisterContext(
        Guid BusinessId,
        string BusinessName,
        Guid RegisterId,
        string RegisterCode,
        string RegisterName,
        Guid WarehouseId,
        string WarehouseCode,
        string WarehouseName,
        bool WarehouseAllowsNegativeStockSales);
}
