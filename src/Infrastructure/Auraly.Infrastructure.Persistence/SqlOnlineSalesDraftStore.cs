using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed class SqlOnlineSalesDraftStore(
    SqlServerConnectionFactory connections,
    IAuralyIdGenerator ids,
    TimeProvider time) : IOnlineSalesDraftStore
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
                  SalesDraftId,BusinessId,LocationId,WarehouseId,RegisterId,UserId,
                  Status,Version,CreatedAt,UpdatedAt)
                VALUES(
                  @DraftId,@BusinessId,@LocationId,@WarehouseId,@RegisterId,@UserId,
                  N'Active',1,@Now,@Now);
                """,
                [
                    P("@DraftId", draftId.Value),
                    P("@BusinessId", context.BusinessId),
                    P("@LocationId", context.LocationId),
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
        var existingLineId = await FindProductLineAsync(
            connection, transaction, draftId, productId, cancellationToken);
        if (existingLineId is null)
        {
            await ExecuteAsync(connection, transaction, """
                INSERT dbo.SalesDraftLines(
                  SalesDraftLineId,SalesDraftId,ProductId,ProductCode,Description,
                  UnitCode,TaxCode,TaxRate,Quantity,BaseUnitPrice,UnitPrice,
                  CurrencyCode,PriceSource,DiscountAmount,Position)
                SELECT
                  @LineId,@DraftId,@ProductId,@ProductCode,@Description,
                  @UnitCode,@TaxCode,@TaxRate,@Quantity,@UnitPrice,@UnitPrice,
                  @CurrencyCode,N'Base',0,COALESCE(MAX(Position),0)+1
                FROM dbo.SalesDraftLines WHERE SalesDraftId=@DraftId;
                """,
                [
                    P("@LineId", ids.NewId()), P("@DraftId", draftId),
                    P("@ProductId", productId), P("@ProductCode", product.Code),
                    P("@Description", product.Name), P("@UnitCode", product.UnitCode),
                    P("@TaxCode", product.TaxCode), P("@TaxRate", product.TaxRate),
                    P("@Quantity", quantity), P("@UnitPrice", product.UnitPrice),
                    P("@CurrencyCode", product.CurrencyCode)
                ],
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE dbo.SalesDraftLines
                SET Quantity=Quantity+@Quantity
                WHERE SalesDraftLineId=@LineId AND SalesDraftId=@DraftId;
                """,
                [
                    P("@Quantity", quantity),
                    P("@LineId", existingLineId.Value),
                    P("@DraftId", draftId)
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
        var affected = await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDraftLines SET Quantity=@Quantity
            WHERE SalesDraftId=@DraftId AND SalesDraftLineId=@LineId;
            """,
            [P("@Quantity", quantity), P("@DraftId", draftId), P("@LineId", lineId)],
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
              SalesDraftId,BusinessId,LocationId,WarehouseId,RegisterId,UserId,
              Status,Version,CreatedAt,UpdatedAt)
            VALUES(
              @NextId,@BusinessId,@LocationId,@WarehouseId,@RegisterId,@UserId,
              N'Active',1,@Now,@Now);
            """,
            [
                P("@NextId", nextId), P("@BusinessId", state.BusinessId),
                P("@LocationId", state.LocationId), P("@WarehouseId", state.WarehouseId),
                P("@RegisterId", state.RegisterId), P("@UserId", user.UserId),
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
            SELECT d.BusinessId,d.LocationId,d.WarehouseId,d.RegisterId,d.Version,d.Status
            FROM dbo.SalesDrafts d WITH (UPDLOCK,HOLDLOCK)
            JOIN dbo.Businesses b ON b.BusinessId=d.BusinessId
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
            reader.GetGuid(3), reader.GetInt64(4), reader.GetString(5));
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
            SELECT b.BusinessId,b.Name,l.LocationId,l.Code,l.Name,
                   r.RegisterId,r.Code,r.Name,w.WarehouseId,w.Code,w.Name,
                   w.AllowNegativeStockSales
            FROM dbo.Businesses b
            JOIN dbo.BusinessLocations l
              ON l.BusinessId=b.BusinessId AND l.LocationId=@LocationId AND l.IsActive=1
            JOIN dbo.CashRegisters r
              ON r.BusinessId=b.BusinessId AND r.LocationId=l.LocationId
             AND r.RegisterId=@RegisterId AND r.IsActive=1
            JOIN dbo.Warehouses w
              ON w.WarehouseId=r.WarehouseId AND w.BusinessId=r.BusinessId
             AND w.LocationId=r.LocationId AND w.IsActive=1
            WHERE b.TenantId=@TenantId AND b.BusinessId=@BusinessId AND b.IsActive=1
              AND NOT EXISTS(
                SELECT 1 FROM dbo.PosDevices d
                WHERE d.RegisterId=r.RegisterId AND d.IsActive=1);
            """;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId), P("@BusinessId", requested.BusinessId),
            P("@LocationId", requested.LocationId), P("@RegisterId", requested.RegisterId)
        ]);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftForbiddenException(
                "La caja online no pertenece al contexto autenticado o está enrolada como POS Edge.");
        return new(
            reader.GetGuid(0), reader.GetString(1),
            reader.GetGuid(2), reader.GetString(3), reader.GetString(4),
            reader.GetGuid(5), reader.GetString(6), reader.GetString(7),
            reader.GetGuid(8), reader.GetString(9), reader.GetString(10),
            reader.GetBoolean(11));
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

    private static async Task<Guid?> FindProductLineAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        Guid productId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftLineId FROM dbo.SalesDraftLines WITH (UPDLOCK,HOLDLOCK)
            WHERE SalesDraftId=@DraftId AND ProductId=@ProductId;
            """;
        command.Parameters.AddRange([P("@DraftId", draftId), P("@ProductId", productId)]);
        return await command.ExecuteScalarAsync(ct) is Guid value ? value : null;
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
            SELECT SalesDraftId,BusinessId,LocationId,WarehouseId,RegisterId,UserId,
                   CustomerId,SellerId,Status,Version,UpdatedAt
            FROM dbo.SalesDrafts WHERE SalesDraftId=@DraftId;
            """;
        header.Parameters.Add(P("@DraftId", draftId));
        await using var reader = await header.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftValidationException("El borrador no existe.");
        var values = new object[11];
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
            (Guid)values[4], (Guid)values[5],
            values[6] is DBNull ? null : (Guid)values[6],
            values[7] is DBNull ? null : (Guid)values[7],
            (string)values[8], (long)values[9], (DateTimeOffset)values[10],
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

    private static SqlParameter P(string name, object value) => new(name, value);
    private static string Invariant(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed record DraftState(
        Guid BusinessId,
        Guid LocationId,
        Guid WarehouseId,
        Guid RegisterId,
        long Version,
        string Status);
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
        Guid LocationId,
        string LocationCode,
        string LocationName,
        Guid RegisterId,
        string RegisterCode,
        string RegisterName,
        Guid WarehouseId,
        string WarehouseCode,
        string WarehouseName,
        bool WarehouseAllowsNegativeStockSales);
}
