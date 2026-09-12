using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Application.Sales;
using Auraly.Contracts.Catalog;
using Auraly.Contracts.Sales;
using Auraly.Domain.Pricing;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Pricing;
using Auraly.Platform.Domain.Promotions;
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
        await using var command = connection.CreateCommand();
        command.CommandText = "dbo.OnlineSalesProductSearch";
        command.CommandType = CommandType.StoredProcedure;
        var search = request.Search?.Trim() ?? string.Empty;
        command.Parameters.AddRange([
            P("@TenantId", user.TenantId), P("@BusinessId", request.Context.BusinessId),
            P("@WarehouseId", request.Context.WarehouseId),
            P("@WorkSessionId", request.Context.WorkSessionId), P("@UserId", user.UserId),
            P("@CustomerId", request.CustomerId), P("@Search", search),
            P("@Contains", $"%{search}%"), P("@Prefix", $"{search}%"),
            P("@Skip", request.Skip), P("@Take", request.Take + 1)
        ]);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new OnlineSalesDraftForbiddenException(
                "La sesión de trabajo no pertenece al usuario y negocio autenticados.");

        await reader.NextResultAsync(cancellationToken);
        var candidates = new List<SearchProductCandidate>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var productId = reader.GetGuid(1);
            var ancestors = reader.GetString(19)
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(Guid.Parse)
                .ToArray();
            var product = new OnlineSalesProduct(
                productId, reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6), reader.GetDecimal(7),
                reader.GetDecimal(8), reader.GetString(9), reader.GetBoolean(10),
                reader.GetBoolean(11), reader.GetBoolean(12), "Base");
            candidates.Add(new(product, new(
                product.ProductCode, product.Name, product.BaseUnitCode, product.TaxCode,
                product.TaxRate, product.UnitPrice, product.CurrencyCode,
                product.AllowsFractionalSale, reader.GetDecimal(16),
                false, reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetGuid(14),
                reader.IsDBNull(15) ? null : reader.GetGuid(15), ancestors,
                reader.GetDecimal(17), reader.IsDBNull(18) ? null : reader.GetDecimal(18))));
        }

        await reader.NextResultAsync(cancellationToken);
        Guid? channelId = await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(0)
            ? reader.GetGuid(0)
            : null;
        await reader.NextResultAsync(cancellationToken);
        var channels = new List<PriceChannelRule>();
        while (await reader.ReadAsync(cancellationToken))
            channels.Add(new(reader.GetGuid(0), reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetDecimal(2)));
        await reader.NextResultAsync(cancellationToken);
        var tiers = new List<PriceChannelTierRule>();
        while (await reader.ReadAsync(cancellationToken))
            tiers.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetDecimal(2),
                reader.GetDecimal(3), reader.GetString(4)));
        await reader.NextResultAsync(cancellationToken);
        var exclusions = new List<PriceChannelExclusionRule>();
        while (await reader.ReadAsync(cancellationToken))
            exclusions.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.IsDBNull(2) ? null : reader.GetGuid(2),
                reader.IsDBNull(3) ? null : reader.GetGuid(3)));
        await reader.NextResultAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new OnlineSalesDraftValidationException(
                "El negocio no tiene una configuración de precios válida.");
        var allowCombination = reader.GetBoolean(0);
        await reader.NextResultAsync(cancellationToken);
        var promotions = new List<PromotionRule>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var conditions = JsonSerializer.Deserialize<PosPromotionCondition[]>(reader.GetString(6)) ?? [];
            var benefits = JsonSerializer.Deserialize<PosPromotionBenefit[]>(reader.GetString(7)) ?? [];
            promotions.Add(new(
                reader.GetGuid(0), reader.GetString(1), reader.GetInt32(2), reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetDateTime(5),
                conditions.Select(value => new PromotionConditionRule(
                    (PromotionItemType)value.ItemType, value.ProductId, value.ServiceId,
                    value.MinimumQuantity, value.MinimumSubtotal,
                    value.ProductCategoryId, value.ServiceCategoryId)).ToArray(),
                benefits.Select(value => new PromotionBenefitRule(
                    (PromotionBenefitType)value.BenefitType, (PromotionItemType)value.TargetItemType,
                    value.ProductId, value.ServiceId, value.DiscountPercentage,
                    value.DiscountAmount, value.FixedUnitPrice, value.AppliesToQuantity,
                    value.ProductCategoryId, value.ServiceCategoryId)).ToArray()));
        }

        var hasMore = candidates.Count > request.Take;
        if (hasMore) candidates.RemoveAt(candidates.Count - 1);
        var priceInputs = candidates.Select(candidate => new CommercePriceLineInput(
            candidate.Product.ProductId.ToString("D"), candidate.Snapshot.Name,
            candidate.Snapshot.UnitPrice, 1m,
            new PriceChannelProductContext(
                candidate.Product.ProductId, candidate.Snapshot.ProductCategoryId,
                candidate.Snapshot.ProductBrandId, candidate.Snapshot.ProductCategoryAncestorIds,
                candidate.Snapshot.CurrencyCode, candidate.Snapshot.UnitCost,
                candidate.Snapshot.LatestUnitCost, candidate.Snapshot.TargetMarginPercent),
            EligibleForPromotion: true)).ToArray();
        var resolved = CommercePriceResolver.Resolve(
            priceInputs,
            new CommercePricePolicy(
                channelId, channels, tiers, exclusions, allowCombination, promotions),
            independentLines: true).Lines.ToDictionary(
                line => line.Input.ProductId!.Value);
        var items = candidates.Select(candidate =>
        {
            var price = resolved[candidate.Product.ProductId];
            return candidate.Product with
            {
                UnitPrice = price.EffectiveUnitPrice,
                CurrencyCode = price.Input.CurrencyCode,
                PriceSource = price.PriceSource,
                PromotionDiscount = price.DiscountAmount
            };
        }).ToArray();
        return new(items, hasMore, hasMore ? request.Skip + items.Length : null);
    }

    private sealed record SearchProductCandidate(
        OnlineSalesProduct Product,
        ProductSnapshot Snapshot);

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
                    reader.GetBoolean(4), reader.GetBoolean(5),
                    reader.IsDBNull(6) ? null : reader.GetDecimal(6)));
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
        var drafts = await ReadDraftsAsync(
            connection, transaction, idsToRead, cancellationToken);
        var result = idsToRead.Select(id => drafts[id]).ToArray();
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
        if (state.SourceOrderId.HasValue)
            throw new OnlineSalesDraftValidationException(
                "Un pedido recuperado debe guardarse como pedido, facturarse o reiniciarse; no puede pausarse.");
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
