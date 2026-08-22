using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Auraly.Application.Sales;
using Auraly.Contracts.Sales;
using Microsoft.Data.SqlClient;

namespace Auraly.Infrastructure.Persistence;

public sealed partial class SqlOnlineSalesDraftStore
{
    public async Task<OnlineSalesProductPage> SearchProductsAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var scope = await ResolveOnlineContextAsync(
            connection, transaction, user, request.Context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT p.ProductId,
                   COALESCE(NULLIF(p.ProductCode,N''),NULLIF(p.Sku,N''),N''),
                   p.Reference,p.Name,
                   COALESCE(NULLIF(p.BaseUnitCode,N''),N'EA'),
                   COALESCE(t.Code,N'01'),COALESCE(t.Rate,0),
                   price.Amount,
                   price.CurrencyCode,
                   p.IsActive,
                   p.IsWeighable
            FROM dbo.Products p
            LEFT JOIN dbo.TaxProfiles t
              ON t.TaxProfileId=p.TaxProfileId AND t.BusinessId=p.BusinessId AND t.IsActive=1
            CROSS APPLY (
              SELECT TOP(1) pp.Amount,pp.CurrencyCode
              FROM dbo.ProductPrices pp
              WHERE pp.BusinessId=p.BusinessId AND pp.ProductId=p.ProductId
                AND pp.IsActive=1 AND pp.ValidFrom<=SYSDATETIMEOFFSET()
                AND (pp.ValidUntil IS NULL OR pp.ValidUntil>SYSDATETIMEOFFSET())
              ORDER BY pp.ValidFrom DESC,pp.ProductPriceId
            ) price
            WHERE p.BusinessId=@BusinessId AND p.IsActive=1
              AND (@Search=N'' OR p.Name LIKE @Contains
                   OR p.ProductCode LIKE @Prefix OR p.Sku LIKE @Prefix
                   OR p.Reference LIKE @Prefix
                   OR EXISTS(
                     SELECT 1 FROM dbo.ProductBarcodes b
                     WHERE b.ProductId=p.ProductId AND b.BusinessId=p.BusinessId
                       AND b.IsActive=1 AND b.Barcode LIKE @Prefix)
                   OR EXISTS(
                     SELECT 1 FROM dbo.ProductIdentifiers i
                     WHERE i.ProductId=p.ProductId AND i.BusinessId=p.BusinessId
                       AND i.IsActive=1 AND i.Value LIKE @Prefix))
            ORDER BY CASE
                       WHEN p.ProductCode=@Search OR p.Sku=@Search OR p.Reference=@Search THEN 0
                       WHEN EXISTS(
                         SELECT 1 FROM dbo.ProductBarcodes b
                         WHERE b.ProductId=p.ProductId AND b.IsActive=1
                           AND b.Barcode=@Search) THEN 0
                       ELSE 1
                     END,
                     p.Name,p.ProductId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@BusinessId", scope.BusinessId), P("@Search", search),
            P("@Contains", $"%{search}%"), P("@Prefix", $"{search}%"),
            P("@Skip", request.Skip), P("@Take", request.Take + 1)
        ]);
        var items = new List<OnlineSalesProduct>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new(
                    reader.GetGuid(0), reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.GetString(5),
                    reader.GetDecimal(6), reader.GetDecimal(7), reader.GetString(8),
                    reader.GetBoolean(9), reader.GetBoolean(10), "Public"));
        if (request.CustomerId is not null)
        {
            for (var index = 0; index < items.Count; index++)
            {
                var item = items[index];
                var resolved = await ResolvePriceAsync(connection, transaction, scope.BusinessId,
                    request.CustomerId, item.ProductId, 1m, item.UnitPrice, item.CurrencyCode,
                    cancellationToken);
                items[index] = item with { UnitPrice = resolved.Amount, CurrencyCode = resolved.CurrencyCode, PriceSource = resolved.Source };
            }
        }
        var hasMore = items.Count > request.Take;
        if (hasMore) items.RemoveAt(items.Count - 1);
        await transaction.CommitAsync(cancellationToken);
        return new(items, hasMore, hasMore ? request.Skip + items.Count : null);
    }

    public async Task<OnlineSalesCustomerPage> SearchCustomersAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var scope = await ResolveOnlineContextAsync(
            connection, transaction, user, request.Context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT c.CustomerId,COALESCE(p.Identification,N''),
                   COALESCE(p.DisplayName,p.LegalName,
                            NULLIF(LTRIM(RTRIM(CONCAT(p.FirstName,N' ',p.LastName))),N''),
                            N'Sin nombre'),
                   s.PriceListId,s.PriceChannelId,c.RequiresElectronicInvoice
            FROM dbo.Customers c
            JOIN dbo.Parties p ON p.PartyId=c.PartyId
            LEFT JOIN dbo.CustomerPricingSettings s ON s.CustomerId=c.CustomerId
            WHERE c.BusinessId=@BusinessId AND c.IsActive=1 AND p.IsActive=1
              AND (@Search=N'' OR p.Identification LIKE @Prefix
                   OR p.DisplayName LIKE @Contains OR p.LegalName LIKE @Contains
                   OR p.FirstName LIKE @Contains OR p.LastName LIKE @Contains)
            ORDER BY CASE WHEN p.Identification=@Search THEN 0 ELSE 1 END,
                     COALESCE(p.DisplayName,p.LegalName,p.FirstName),c.CustomerId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@BusinessId", scope.BusinessId), P("@Search", search),
            P("@Contains", $"%{search}%"), P("@Prefix", $"{search}%"),
            P("@Skip", request.Skip), P("@Take", request.Take + 1)
        ]);
        var items = new List<OnlineSalesCustomer>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                items.Add(new(
                    reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetGuid(3),
                    reader.IsDBNull(4) ? null : reader.GetGuid(4),
                    reader.GetBoolean(5)));
        var hasMore = items.Count > request.Take;
        if (hasMore) items.RemoveAt(items.Count - 1);
        await transaction.CommitAsync(cancellationToken);
        return new(items, hasMore, hasMore ? request.Skip + items.Count : null);
    }

    public async Task<IReadOnlyList<OnlineSalesDraft>> ListTemporariesAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken)
    {
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.ReadCommitted, cancellationToken);
        var scope = await ResolveOnlineContextAsync(
            connection, transaction, user, request.Context, cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT SalesDraftId
            FROM dbo.SalesDrafts
            WHERE BusinessId=@BusinessId AND WorkSessionId=@WorkSessionId
              AND UserId=@UserId AND Status=N'Temporary'
              AND (@Search=N'' OR Name LIKE @Contains OR Reference LIKE @Contains)
            ORDER BY SavedAt DESC,SalesDraftId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            """;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@BusinessId", scope.BusinessId), P("@WorkSessionId", scope.WorkSessionId),
            P("@UserId", user.UserId), P("@Search", search),
            P("@Contains", $"%{search}%"), P("@Skip", request.Skip),
            P("@Take", request.Take)
        ]);
        var idsToRead = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                idsToRead.Add(reader.GetGuid(0));
        var result = new List<OnlineSalesDraft>(idsToRead.Count);
        foreach (var id in idsToRead)
            result.Add(await ReadDraftAsync(connection, transaction, id, cancellationToken));
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> PauseAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        PauseOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "Pause";
        var hash = MutationHash(
            operation, draftId, request.ExpectedVersion,
            request.Name, request.Reference, request.Observation);
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
        DemandActiveVersion(state, request.ExpectedVersion);
        await DemandDraftHasLinesAsync(connection, transaction, draftId, cancellationToken);
        var now = time.GetUtcNow();
        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts
            SET Status=N'Temporary',Name=@Name,Reference=@Reference,
                Observation=@Observation,SavedAt=@Now,UpdatedAt=@Now,Version=Version+1
            WHERE SalesDraftId=@DraftId AND Version=@Version AND Status=N'Active';
            """,
            [
                P("@Name", request.Name), P("@Reference", request.Reference),
                P("@Observation", request.Observation), P("@Now", now),
                P("@DraftId", draftId), P("@Version", request.ExpectedVersion)
            ], cancellationToken);
        var nextId = ids.NewId();
        await InsertActiveAsync(connection, transaction, nextId, state, user.UserId, now, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, state.BusinessId, nextId,
            idempotencyKey, operation, hash, 1, cancellationToken);
        var result = await ReadDraftAsync(connection, transaction, nextId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> RecoverTemporaryAsync(
        OnlineSalesUserIdentity user,
        Guid temporaryDraftId,
        RecoverOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "RecoverTemporary";
        var hash = MutationHash(
            operation, temporaryDraftId, request.ExpectedTemporaryVersion,
            request.ExpectedActiveVersion);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var temporary = await LockTemporaryAsync(
            connection, transaction, user, temporaryDraftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, temporary.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        DemandTemporaryVersion(temporary, request.ExpectedTemporaryVersion);
        var activeId = await FindActiveAsync(
            connection, transaction, temporary.BusinessId,
            temporary.WorkSessionId, user.UserId, cancellationToken)
            ?? throw new OnlineSalesDraftConcurrencyException(
                "No existe una venta activa para intercambiar.");
        var active = await ReadActiveStateAsync(
            connection, transaction, activeId, cancellationToken);
        if (active.Version != request.ExpectedActiveVersion)
            throw new OnlineSalesDraftConcurrencyException(
                "La venta activa cambió en otra ventana.");
        if (active.LineCount != 0)
            throw new OnlineSalesDraftValidationException(
                "Pausa o reinicia la venta actual antes de recuperar otra.");
        var now = time.GetUtcNow();
        await ExecuteAsync(connection, transaction, """
            UPDATE dbo.SalesDrafts
            SET Status=N'Deleted',DeletedAt=@Now,UpdatedAt=@Now,Version=Version+1
            WHERE SalesDraftId=@ActiveId AND Version=@ActiveVersion AND Status=N'Active';
            UPDATE dbo.SalesDrafts
            SET Status=N'Active',UpdatedAt=@Now,Version=Version+1
            WHERE SalesDraftId=@TemporaryId AND Version=@TemporaryVersion
              AND Status=N'Temporary';
            """,
            [
                P("@Now", now), P("@ActiveId", activeId),
                P("@ActiveVersion", request.ExpectedActiveVersion),
                P("@TemporaryId", temporaryDraftId),
                P("@TemporaryVersion", request.ExpectedTemporaryVersion)
            ], cancellationToken);
        var version = request.ExpectedTemporaryVersion + 1;
        await SaveReceiptAsync(
            connection, transaction, temporary.BusinessId, temporaryDraftId,
            idempotencyKey, operation, hash, version, cancellationToken);
        var result = await ReadDraftAsync(
            connection, transaction, temporaryDraftId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    public async Task<OnlineSalesDraft> RemoveTemporaryAsync(
        OnlineSalesUserIdentity user,
        Guid temporaryDraftId,
        RemoveOnlineSalesTemporaryRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        const string operation = "RemoveTemporary";
        var hash = MutationHash(operation, temporaryDraftId, request.ExpectedVersion);
        await using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var temporary = await LockTemporaryAsync(
            connection, transaction, user, temporaryDraftId, cancellationToken);
        var replay = await ReplayAsync(
            connection, transaction, temporary.BusinessId, idempotencyKey,
            operation, hash, cancellationToken);
        if (replay is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return replay;
        }
        DemandTemporaryVersion(temporary, request.ExpectedVersion);
        var activeId = await FindActiveAsync(
            connection, transaction, temporary.BusinessId,
            temporary.WorkSessionId, user.UserId, cancellationToken)
            ?? throw new OnlineSalesDraftConcurrencyException(
                "No existe la venta activa del usuario.");
        var now = time.GetUtcNow();
        await ExecuteAsync(connection, transaction, """
            UPDATE claim
            SET ReleasedAt=@Now
            FROM dbo.OrderClaims claim
            JOIN dbo.SalesDrafts draft ON draft.SourceOrderId=claim.OrderId
            WHERE draft.SalesDraftId=@DraftId AND claim.ReleasedAt IS NULL;

            UPDATE dbo.SalesDrafts
            SET Status=N'Deleted',SourceOrderId=NULL,DeletedAt=@Now,UpdatedAt=@Now,Version=Version+1
            WHERE SalesDraftId=@DraftId AND Version=@Version AND Status=N'Temporary';
            """,
            [
                P("@Now", now), P("@DraftId", temporaryDraftId),
                P("@Version", request.ExpectedVersion)
            ], cancellationToken);
        var active = await ReadDraftAsync(connection, transaction, activeId, cancellationToken);
        await SaveReceiptAsync(
            connection, transaction, temporary.BusinessId, activeId,
            idempotencyKey, operation, hash, active.Version, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return active;
    }

    private static async Task<DraftState> LockTemporaryAsync(
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
                "La venta en espera no pertenece al usuario autenticado.");
        return new(
            reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.GetInt64(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetGuid(5), reader.GetBoolean(6),
            reader.IsDBNull(7) ? null : reader.GetGuid(7));
    }

    private static void DemandTemporaryVersion(DraftState state, long expectedVersion)
    {
        if (!string.Equals(state.Status, "Temporary", StringComparison.Ordinal))
            throw new OnlineSalesDraftValidationException(
                "La venta ya no está en espera.");
        if (state.Version != expectedVersion)
            throw new OnlineSalesDraftConcurrencyException(
                $"La venta en espera cambió. Versión actual: {state.Version}.");
    }

    private static async Task DemandDraftHasLinesAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT COUNT_BIG(*) FROM dbo.SalesDraftLines WHERE SalesDraftId=@DraftId;";
        command.Parameters.Add(P("@DraftId", draftId));
        if (Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0)
            throw new OnlineSalesDraftValidationException(
                "No se puede pausar una venta vacía.");
    }

    private static async Task<ActiveDraftState> ReadActiveStateAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT d.Version,COUNT_BIG(l.SalesDraftLineId)
            FROM dbo.SalesDrafts d WITH (UPDLOCK,HOLDLOCK)
            LEFT JOIN dbo.SalesDraftLines l ON l.SalesDraftId=d.SalesDraftId
            WHERE d.SalesDraftId=@DraftId AND d.Status=N'Active'
            GROUP BY d.Version;
            """;
        command.Parameters.Add(P("@DraftId", draftId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new OnlineSalesDraftConcurrencyException(
                "La venta activa ya no existe.");
        return new(reader.GetInt64(0), reader.GetInt64(1));
    }

    private static async Task InsertActiveAsync(
        SqlConnection connection,
        SqlTransaction transaction,
        Guid draftId,
        DraftState state,
        Guid userId,
        DateTimeOffset now,
        CancellationToken ct) =>
        await ExecuteAsync(connection, transaction, """
            INSERT dbo.SalesDrafts(
              SalesDraftId,BusinessId,WarehouseId,WorkSessionId,UserId,
              Status,Version,CreatedAt,UpdatedAt)
            VALUES(
              @DraftId,@BusinessId,@WarehouseId,@WorkSessionId,@UserId,
              N'Active',1,@Now,@Now);
            """,
            [
                P("@DraftId", draftId), P("@BusinessId", state.BusinessId),
                P("@WarehouseId", state.WarehouseId), P("@WorkSessionId", state.WorkSessionId), P("@UserId", userId), P("@Now", now)
            ], ct);

    private static string MutationHash(
        string operation,
        Guid draftId,
        params object?[] values)
    {
        var payload = string.Join(
            "|",
            new object?[] { operation, draftId.ToString("D") }
                .Concat(values)
                .Select(value => value switch
                {
                    null => string.Empty,
                    decimal number => number.ToString(CultureInfo.InvariantCulture),
                    _ => value.ToString()
                }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    }

    private sealed record ActiveDraftState(long Version, long LineCount);
}
