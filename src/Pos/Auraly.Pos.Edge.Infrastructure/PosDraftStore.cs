using System.Data;
using System.Globalization;
using Auraly.BuildingBlocks.Domain.Identifiers;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public static class PosDraftStatus
{
    public const string Active = "Active";
    public const string Temporary = "Temporary";
    public const string Consumed = "Consumed";
    public const string Deleted = "Deleted";
}

public sealed record PosDraftScope(
    BusinessId BusinessId,
    WarehouseId WarehouseId,
    RegisterId RegisterId,
    UserId UserId);

public sealed record PosDraftLineInput(
    ProductId ProductId,
    string ProductCode,
    string Description,
    string UnitCode,
    string TaxCode,
    decimal TaxRate,
    decimal Quantity,
    decimal BaseUnitPrice,
    decimal UnitPrice,
    string CurrencyCode,
    string PriceSource,
    Guid? PriceListId = null,
    Guid? PriceChannelId = null,
    decimal Discount = 0,
    string? Note = null);

public sealed record PosDraftLine(
    Guid LineId,
    ProductId ProductId,
    string ProductCode,
    string Description,
    string UnitCode,
    string TaxCode,
    decimal TaxRate,
    decimal Quantity,
    decimal BaseUnitPrice,
    decimal UnitPrice,
    string CurrencyCode,
    string PriceSource,
    Guid? PriceListId,
    Guid? PriceChannelId,
    decimal Discount,
    string? Note,
    int Position)
{
    public decimal Gross => Round(Quantity * UnitPrice);
    public decimal Net => Round(Gross - Discount);
    public decimal Tax => Round(Net * TaxRate / 100m);
    public decimal Total => Net + Tax;

    private static decimal Round(decimal value) =>
        decimal.Round(value, 2, MidpointRounding.ToEven);
}

public sealed record PosDraft(
    DraftId DraftId,
    PosDraftScope Scope,
    Guid? CustomerId,
    Guid? SellerId,
    string Status,
    string? Name,
    string? Reference,
    string? Observation,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<PosDraftLine> Lines)
{
    public decimal UntaxedAmount => Lines.Sum(line => line.Net);
    public decimal TaxAmount => Lines.Sum(line => line.Tax);
    public decimal PayableAmount => UntaxedAmount + TaxAmount;
}

public sealed record PosTemporaryFilter(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? CustomerId = null,
    RegisterId? RegisterId = null,
    string? Search = null,
    int Take = 50);

public sealed record PosDraftLinePriceUpdate(
    Guid LineId,
    decimal BaseUnitPrice,
    decimal UnitPrice,
    string CurrencyCode,
    string PriceSource,
    Guid? PriceListId,
    Guid? PriceChannelId);

public sealed class PosDraftStore
{
    private readonly string _connectionString;
    private readonly IAuralyIdGenerator _idGenerator;
    private readonly TimeProvider _timeProvider;

    public PosDraftStore(
        string connectionString,
        IAuralyIdGenerator idGenerator,
        TimeProvider timeProvider)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A SQLite connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
        _idGenerator = idGenerator;
        _timeProvider = timeProvider;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        command.CommandText = "PRAGMA table_info('PosDrafts');";
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
                columns.Add(reader.GetString(1));
        }
        if (!columns.Contains("IssuedAt"))
        {
            command.CommandText = "ALTER TABLE PosDrafts ADD COLUMN IssuedAt TEXT NULL;";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<PosDraft> GetOrCreateActiveAsync(
        PosDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var existing = await FindActiveIdAsync(connection, transaction, scope, cancellationToken);
        var draftId = existing ?? new DraftId(_idGenerator.NewId());
        if (existing is null)
            await InsertActiveAsync(connection, transaction, draftId, scope, null, null, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(draftId, cancellationToken);
    }

    public async Task<PosDraft> AddOrIncrementLineAsync(
        PosDraftScope scope,
        PosDraftLineInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateLine(input);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var draftId = await FindActiveIdAsync(connection, transaction, scope, cancellationToken);
        if (draftId is null)
        {
            draftId = new DraftId(_idGenerator.NewId());
            await InsertActiveAsync(connection, transaction, draftId.Value, scope, null, null, cancellationToken);
        }

        else
        {
            await RequireActiveAsync(connection, transaction, draftId.Value, cancellationToken);
        }

        var mergeLineId = await FindMergeableLineAsync(
            connection, transaction, draftId.Value, input, cancellationToken);
        if (mergeLineId is null)
        {
            var position = await NextPositionAsync(connection, transaction, draftId.Value, cancellationToken);
            await InsertLineAsync(
                connection,
                transaction,
                draftId.Value,
                _idGenerator.NewId(),
                input,
                position,
                cancellationToken);
        }
        else
        {
            await ExecuteAsync(connection, transaction, """
                UPDATE PosDraftLines
                SET Quantity=CAST(Quantity AS NUMERIC)+CAST(@Quantity AS NUMERIC)
                WHERE LineId=@LineId;
                """,
                [P("@Quantity", input.Quantity), P("@LineId", mergeLineId.Value)],
                cancellationToken);
        }
        await TouchAsync(connection, transaction, draftId.Value, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(draftId.Value, cancellationToken);
    }

    public async Task<PosDraft> SetQuantityAsync(
        DraftId draftId,
        Guid lineId,
        decimal quantity,
        CancellationToken cancellationToken = default)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        await MutateLineAsync(
            draftId,
            lineId,
            "UPDATE PosDraftLines SET Quantity=@Value WHERE DraftId=@DraftId AND LineId=@LineId;",
            P("@Value", quantity),
            cancellationToken);
        return await GetRequiredAsync(draftId, cancellationToken);
    }

    public async Task<PosDraft> SetDiscountAsync(
        DraftId draftId,
        Guid lineId,
        decimal discount,
        CancellationToken cancellationToken = default)
    {
        if (discount < 0) throw new ArgumentOutOfRangeException(nameof(discount));
        var current = await GetRequiredAsync(draftId, cancellationToken);
        var line = current.Lines.SingleOrDefault(value => value.LineId == lineId)
            ?? throw new KeyNotFoundException("The draft line does not exist.");
        if (discount > line.Gross)
            throw new ArgumentOutOfRangeException(nameof(discount), "Discount cannot exceed gross value.");
        await MutateLineAsync(
            draftId,
            lineId,
            "UPDATE PosDraftLines SET Discount=@Value WHERE DraftId=@DraftId AND LineId=@LineId;",
            P("@Value", discount),
            cancellationToken);
        return await GetRequiredAsync(draftId, cancellationToken);
    }

    public async Task<PosDraft> RemoveLineAsync(
        DraftId draftId,
        Guid lineId,
        CancellationToken cancellationToken = default)
    {
        await MutateLineAsync(
            draftId,
            lineId,
            "DELETE FROM PosDraftLines WHERE DraftId=@DraftId AND LineId=@LineId;",
            null,
            cancellationToken);
        return await GetRequiredAsync(draftId, cancellationToken);
    }

    public async Task CancelAsync(
        DraftId draftId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await RequireActiveAsync(connection, transaction, draftId, cancellationToken);
        var now = Now();
        await ExecuteAsync(connection, transaction, """
            DELETE FROM PosDraftLines WHERE DraftId=@DraftId;
            UPDATE PosDrafts
            SET Status='Deleted',UpdatedAt=@Now
            WHERE DraftId=@DraftId AND Status='Active' AND IssuedAt IS NULL;
            INSERT INTO PosDraftAudit(AuditId,DraftId,Action,OccurredAt)
            SELECT @AuditId,@DraftId,'Deleted',@Now
            WHERE changes()>0;
            """,
            [
                P("@DraftId", draftId.Value),
                P("@Now", now),
                P("@AuditId", _idGenerator.NewId())
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PosDraft> AssignPartiesAsync(
        DraftId draftId,
        Guid? customerId,
        Guid? sellerId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await RequireActiveAsync(connection, transaction, draftId, cancellationToken);
        await ExecuteAsync(connection, transaction, """
            UPDATE PosDrafts
            SET CustomerId=@CustomerId,SellerId=@SellerId,UpdatedAt=@Now
            WHERE DraftId=@DraftId;
            """,
            [
                P("@CustomerId", customerId), P("@SellerId", sellerId),
                P("@Now", Now()), P("@DraftId", draftId.Value)
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(draftId, cancellationToken);
    }

    public async Task<PosDraft> AssignCustomerAndPricesAsync(
        DraftId draftId,
        Guid? customerId,
        IReadOnlyCollection<PosDraftLinePriceUpdate> prices,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await RequireActiveAsync(connection, transaction, draftId, cancellationToken);
        var header = await ReadHeaderAsync(connection, transaction, draftId, cancellationToken)
            ?? throw new KeyNotFoundException("The draft does not exist.");
        var current = header with
            { Lines = await ReadLinesAsync(connection, transaction, draftId, cancellationToken) };
        if (prices.Count != current.Lines.Count ||
            prices.Select(value => value.LineId).Distinct().Count() != prices.Count ||
            current.Lines.Any(line => prices.All(value => value.LineId != line.LineId)))
            throw new InvalidOperationException("Every active line must receive exactly one price.");

        foreach (var price in prices)
        {
            if (price.BaseUnitPrice < 0 || price.UnitPrice < 0)
                throw new ArgumentOutOfRangeException(nameof(prices), "Prices cannot be negative.");
            var line = current.Lines.Single(value => value.LineId == price.LineId);
            if (line.Discount > line.Quantity * price.UnitPrice)
                throw new InvalidOperationException(
                    "The existing discount exceeds the selected customer's price.");
            await ExecuteAsync(connection, transaction, """
                UPDATE PosDraftLines
                SET BaseUnitPrice=@BaseUnitPrice,UnitPrice=@UnitPrice,
                    CurrencyCode=@CurrencyCode,PriceSource=@PriceSource,
                    PriceListId=@PriceListId,PriceChannelId=@PriceChannelId
                WHERE DraftId=@DraftId AND LineId=@LineId;
                """,
                [
                    P("@BaseUnitPrice", price.BaseUnitPrice),
                    P("@UnitPrice", price.UnitPrice),
                    P("@CurrencyCode", price.CurrencyCode.Trim().ToUpperInvariant()),
                    P("@PriceSource", price.PriceSource),
                    P("@PriceListId", price.PriceListId),
                    P("@PriceChannelId", price.PriceChannelId),
                    P("@DraftId", draftId.Value),
                    P("@LineId", price.LineId)
                ],
                cancellationToken);
        }
        await ExecuteAsync(connection, transaction, """
            UPDATE PosDrafts
            SET CustomerId=@CustomerId,UpdatedAt=@Now
            WHERE DraftId=@DraftId;
            """,
            [
                P("@CustomerId", customerId),
                P("@Now", Now()),
                P("@DraftId", draftId.Value)
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(draftId, cancellationToken);
    }

    public async Task<PosDraft> SaveTemporaryAsync(
        DraftId draftId,
        string name,
        string? reference,
        string? observation,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A temporary sale name is required.", nameof(name));
        var draft = await GetRequiredAsync(draftId, cancellationToken);
        if (draft.Lines.Count == 0)
            throw new InvalidOperationException("An empty sale cannot be saved as temporary.");

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await RequireActiveAsync(connection, transaction, draftId, cancellationToken);
        var now = Now();
        await ExecuteAsync(connection, transaction, """
            UPDATE PosDrafts
            SET Status='Temporary',Name=@Name,Reference=@Reference,Observation=@Observation,
                SavedAt=@Now,UpdatedAt=@Now
            WHERE DraftId=@DraftId;
            INSERT INTO PosDraftAudit(AuditId,DraftId,Action,OccurredAt)
            VALUES(@AuditId,@DraftId,'SavedTemporary',@Now);
            """,
            [
                P("@Name", name.Trim()), P("@Reference", Normalize(reference)),
                P("@Observation", Normalize(observation)), P("@Now", now),
                P("@DraftId", draftId.Value), P("@AuditId", _idGenerator.NewId())
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(draftId, cancellationToken);
    }

    public async Task<IReadOnlyList<PosDraft>> ListTemporariesAsync(
        BusinessId businessId,
        PosTemporaryFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (filter.Take is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(filter));
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DraftId FROM PosDrafts
            WHERE BusinessId=@BusinessId AND Status='Temporary'
              AND (@From IS NULL OR SavedAt>=@From)
              AND (@To IS NULL OR SavedAt<=@To)
              AND (@CustomerId IS NULL OR CustomerId=@CustomerId)
              AND (@RegisterId IS NULL OR RegisterId=@RegisterId)
              AND (@Search IS NULL OR Name LIKE @Search OR Reference LIKE @Search OR Observation LIKE @Search)
            ORDER BY SavedAt DESC,DraftId
            LIMIT @Take;
            """;
        command.Parameters.AddRange(
        [
            P("@BusinessId", businessId.Value), P("@From", filter.From), P("@To", filter.To),
            P("@CustomerId", filter.CustomerId), P("@RegisterId", filter.RegisterId?.Value),
            P("@Search", string.IsNullOrWhiteSpace(filter.Search) ? null : $"%{filter.Search.Trim()}%"),
            P("@Take", filter.Take)
        ]);
        var ids = new List<DraftId>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                ids.Add(new DraftId(Guid.Parse(reader.GetString(0))));
        var values = new List<PosDraft>(ids.Count);
        foreach (var id in ids)
            values.Add(await GetRequiredAsync(id, cancellationToken));
        return values;
    }

    public async Task DeleteTemporaryAsync(
        DraftId draftId,
        BusinessId businessId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var source = await ReadHeaderAsync(connection, transaction, draftId, cancellationToken)
            ?? throw new KeyNotFoundException("The paused sale does not exist.");
        if (source.Scope.BusinessId != businessId)
            throw new UnauthorizedAccessException("The paused sale belongs to another business.");
        if (source.Status != PosDraftStatus.Temporary)
            throw new InvalidOperationException(
                "The sale was already recovered or is no longer paused.");

        var now = Now();
        await ExecuteAsync(connection, transaction, """
            DELETE FROM PosDraftLines WHERE DraftId=@DraftId;
            UPDATE PosDrafts
            SET Status='Deleted',UpdatedAt=@Now
            WHERE DraftId=@DraftId AND Status='Temporary' AND IssuedAt IS NULL;
            INSERT INTO PosDraftAudit(AuditId,DraftId,Action,OccurredAt)
            SELECT @AuditId,@DraftId,'DeletedTemporary',@Now
            WHERE changes()>0;
            """,
            [
                P("@DraftId", draftId.Value),
                P("@Now", now),
                P("@AuditId", _idGenerator.NewId())
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PosDraft> RecoverTemporaryAsync(
        DraftId temporaryId,
        PosDraftScope scope,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var activeId = await FindActiveIdAsync(connection, transaction, scope, cancellationToken);
        if (activeId is not null &&
            await HasLinesAsync(connection, transaction, activeId.Value, cancellationToken))
            throw new InvalidOperationException(
                "The active sale must be completed or saved before recovering a temporary sale.");

        var source = await ReadHeaderAsync(connection, transaction, temporaryId, cancellationToken)
            ?? throw new KeyNotFoundException("The temporary sale does not exist.");
        if (source.Status != PosDraftStatus.Temporary)
            throw new InvalidOperationException(
                "The temporary sale was already consumed or is no longer available.");
        if (source.Scope.BusinessId != scope.BusinessId)
            throw new UnauthorizedAccessException("The temporary sale belongs to another business.");

        var sourceLines = await ReadLinesAsync(connection, transaction, temporaryId, cancellationToken);
        var targetId = activeId ?? new DraftId(_idGenerator.NewId());
        if (activeId is null)
            await InsertActiveAsync(
                connection,
                transaction,
                targetId,
                scope,
                source.CustomerId,
                source.SellerId,
                cancellationToken);
        else
            await ExecuteAsync(connection, transaction, """
                UPDATE PosDrafts
                SET CustomerId=@CustomerId,SellerId=@SellerId,UpdatedAt=@Now
                WHERE DraftId=@DraftId;
                """,
                [
                    P("@CustomerId", source.CustomerId), P("@SellerId", source.SellerId),
                    P("@Now", Now()), P("@DraftId", targetId.Value)
                ],
                cancellationToken);

        foreach (var line in sourceLines)
            await InsertLineAsync(
                connection,
                transaction,
                targetId,
                _idGenerator.NewId(),
                ToInput(line),
                line.Position,
                cancellationToken);

        var now = Now();
        await ExecuteAsync(connection, transaction, """
            UPDATE PosDrafts
            SET Status='Consumed',ConsumedAt=@Now,UpdatedAt=@Now
            WHERE DraftId=@SourceId AND Status='Temporary';
            INSERT INTO PosDraftAudit(AuditId,DraftId,Action,RelatedDraftId,OccurredAt)
            VALUES(@AuditId,@SourceId,'Recovered',@TargetId,@Now);
            """,
            [
                P("@TargetId", targetId.Value), P("@SourceId", temporaryId.Value),
                P("@Now", now), P("@AuditId", _idGenerator.NewId())
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetRequiredAsync(targetId, cancellationToken);
    }

    public async Task<PosDraft?> GetAsync(
        DraftId draftId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var header = await ReadHeaderAsync(connection, null, draftId, cancellationToken);
        if (header is null) return null;
        var lines = await ReadLinesAsync(connection, null, draftId, cancellationToken);
        return header with { Lines = lines };
    }

    private async Task<PosDraft> GetRequiredAsync(DraftId draftId, CancellationToken ct) =>
        await GetAsync(draftId, ct) ?? throw new KeyNotFoundException("The draft does not exist.");

    private async Task InsertActiveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftId draftId,
        PosDraftScope scope,
        Guid? customerId,
        Guid? sellerId,
        CancellationToken ct)
    {
        var now = Now();
        await ExecuteAsync(connection, transaction, """
            INSERT INTO PosDrafts(
              DraftId,BusinessId,WarehouseId,RegisterId,UserId,CustomerId,SellerId,
              Status,CreatedAt,UpdatedAt)
            VALUES(
              @DraftId,@BusinessId,@WarehouseId,@RegisterId,@UserId,@CustomerId,@SellerId,
              'Active',@Now,@Now);
            """,
            [
                P("@DraftId", draftId.Value), P("@BusinessId", scope.BusinessId.Value),
                P("@WarehouseId", scope.WarehouseId.Value), P("@RegisterId", scope.RegisterId.Value),
                P("@UserId", scope.UserId.Value), P("@CustomerId", customerId),
                P("@SellerId", sellerId), P("@Now", now)
            ],
            ct);
    }

    private static async Task InsertLineAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftId draftId,
        Guid lineId,
        PosDraftLineInput input,
        int position,
        CancellationToken ct) =>
        await ExecuteAsync(connection, transaction, """
            INSERT INTO PosDraftLines(
              LineId,DraftId,ProductId,ProductCode,Description,UnitCode,TaxCode,TaxRate,
              Quantity,BaseUnitPrice,UnitPrice,CurrencyCode,PriceSource,PriceListId,
              PriceChannelId,Discount,Note,Position)
            VALUES(
              @LineId,@DraftId,@ProductId,@ProductCode,@Description,@UnitCode,@TaxCode,@TaxRate,
              @Quantity,@BaseUnitPrice,@UnitPrice,@CurrencyCode,@PriceSource,@PriceListId,
              @PriceChannelId,@Discount,@Note,@Position);
            """,
            LineParameters(lineId, draftId, input, position),
            ct);

    private async Task MutateLineAsync(
        DraftId draftId,
        Guid lineId,
        string sql,
        SqliteParameter? value,
        CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await RequireActiveAsync(connection, transaction, draftId, ct);
        var parameters = new List<SqliteParameter>
        {
            P("@DraftId", draftId.Value),
            P("@LineId", lineId)
        };
        if (value is not null) parameters.Add(value);
        var affected = await ExecuteAsync(connection, transaction, sql, [.. parameters], ct);
        if (affected == 0) throw new KeyNotFoundException("The draft line does not exist.");
        await TouchAsync(connection, transaction, draftId, ct);
        await transaction.CommitAsync(ct);
    }

    private static void ValidateLine(PosDraftLineInput input)
    {
        if (input.ProductId.Value == Guid.Empty)
            throw new ArgumentException("A product ID is required.", nameof(input));
        if (string.IsNullOrWhiteSpace(input.ProductCode) ||
            string.IsNullOrWhiteSpace(input.Description))
            throw new ArgumentException("Product code and description are required.", nameof(input));
        if (input.Quantity <= 0) throw new ArgumentOutOfRangeException(nameof(input));
        if (input.UnitPrice < 0 || input.BaseUnitPrice < 0 ||
            input.Discount < 0 || input.TaxRate < 0)
            throw new ArgumentOutOfRangeException(nameof(input));
        if (input.Discount > input.Quantity * input.UnitPrice)
            throw new ArgumentOutOfRangeException(
                nameof(input),
                "Discount cannot exceed gross value.");
        if (input.PriceListId is not null && input.PriceChannelId is not null)
            throw new ArgumentException(
                "A line cannot originate from both a price list and a price channel.",
                nameof(input));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }

    private static async Task<DraftId?> FindActiveIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PosDraftScope scope,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT DraftId FROM PosDrafts
            WHERE BusinessId=@BusinessId AND RegisterId=@RegisterId
              AND UserId=@UserId AND Status='Active'
            LIMIT 1;
            """;
        command.Parameters.AddRange(
        [
            P("@BusinessId", scope.BusinessId.Value), P("@RegisterId", scope.RegisterId.Value),
            P("@UserId", scope.UserId.Value)
        ]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : new DraftId(Guid.Parse((string)value));
    }

    private static async Task<Guid?> FindMergeableLineAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftId draftId,
        PosDraftLineInput input,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT LineId FROM PosDraftLines
            WHERE DraftId=@DraftId AND ProductId=@ProductId AND UnitCode=@UnitCode
              AND UnitPrice=@UnitPrice AND PriceSource=@PriceSource
              AND ifnull(PriceListId,'')=ifnull(@PriceListId,'')
              AND ifnull(PriceChannelId,'')=ifnull(@PriceChannelId,'')
              AND TaxCode=@TaxCode AND TaxRate=@TaxRate AND Discount=@Discount
              AND ifnull(Note,'')=ifnull(@Note,'')
            LIMIT 1;
            """;
        command.Parameters.AddRange(
        [
            P("@DraftId", draftId.Value), P("@ProductId", input.ProductId.Value),
            P("@UnitCode", input.UnitCode), P("@UnitPrice", input.UnitPrice),
            P("@PriceSource", input.PriceSource), P("@PriceListId", input.PriceListId),
            P("@PriceChannelId", input.PriceChannelId), P("@TaxCode", input.TaxCode),
            P("@TaxRate", input.TaxRate), P("@Discount", input.Discount),
            P("@Note", Normalize(input.Note))
        ]);
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? null : Guid.Parse((string)value);
    }

    private static async Task<int> NextPositionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftId draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT ifnull(max(Position),0)+1 FROM PosDraftLines WHERE DraftId=@DraftId;";
        command.Parameters.Add(P("@DraftId", draftId.Value));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    private static async Task<bool> HasLinesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftId draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT EXISTS(SELECT 1 FROM PosDraftLines WHERE DraftId=@DraftId);";
        command.Parameters.Add(P("@DraftId", draftId.Value));
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    private static async Task RequireActiveAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftId draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Status,IssuedAt FROM PosDrafts WHERE DraftId=@DraftId;";
        command.Parameters.Add(P("@DraftId", draftId.Value));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new KeyNotFoundException("The draft does not exist.");
        var status = reader.GetString(0);
        if (status != PosDraftStatus.Active)
            throw new InvalidOperationException("Only the active sale can be modified.");
        if (!reader.IsDBNull(1))
            throw new InvalidOperationException(
                "The sale was already issued and is locked until its receipt is printed.");
    }

    private static async Task<PosDraft?> ReadHeaderAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DraftId draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT BusinessId,WarehouseId,RegisterId,UserId,CustomerId,SellerId,Status,
                   Name,Reference,Observation,CreatedAt,UpdatedAt
            FROM PosDrafts WHERE DraftId=@DraftId;
            """;
        command.Parameters.Add(P("@DraftId", draftId.Value));
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new PosDraft(
            draftId,
            new PosDraftScope(
                new BusinessId(Guid.Parse(reader.GetString(0))),
                new WarehouseId(Guid.Parse(reader.GetString(1))),
                new RegisterId(Guid.Parse(reader.GetString(2))),
                new UserId(Guid.Parse(reader.GetString(3)))),
            NullableGuid(reader, 4),
            NullableGuid(reader, 5),
            reader.GetString(6),
            NullableString(reader, 7),
            NullableString(reader, 8),
            NullableString(reader, 9),
            DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
            DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture),
            []);
    }

    private static async Task<IReadOnlyList<PosDraftLine>> ReadLinesAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        DraftId draftId,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT LineId,ProductId,ProductCode,Description,UnitCode,TaxCode,TaxRate,Quantity,
                   BaseUnitPrice,UnitPrice,CurrencyCode,PriceSource,PriceListId,PriceChannelId,
                   Discount,Note,Position
            FROM PosDraftLines WHERE DraftId=@DraftId ORDER BY Position,LineId;
            """;
        command.Parameters.Add(P("@DraftId", draftId.Value));
        var lines = new List<PosDraftLine>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            lines.Add(new PosDraftLine(
                Guid.Parse(reader.GetString(0)),
                new ProductId(Guid.Parse(reader.GetString(1))),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                Decimal(reader, 6),
                Decimal(reader, 7),
                Decimal(reader, 8),
                Decimal(reader, 9),
                reader.GetString(10),
                reader.GetString(11),
                NullableGuid(reader, 12),
                NullableGuid(reader, 13),
                Decimal(reader, 14),
                NullableString(reader, 15),
                reader.GetInt32(16)));
        return lines;
    }

    private static async Task TouchAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DraftId draftId,
        CancellationToken ct) =>
        await ExecuteAsync(
            connection,
            transaction,
            "UPDATE PosDrafts SET UpdatedAt=@Now WHERE DraftId=@DraftId;",
            [P("@Now", DateTimeOffset.UtcNow), P("@DraftId", draftId.Value)],
            ct);

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        SqliteParameter[] parameters,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    private static SqliteParameter[] LineParameters(
        Guid lineId,
        DraftId draftId,
        PosDraftLineInput input,
        int position) =>
    [
        P("@LineId", lineId), P("@DraftId", draftId.Value), P("@ProductId", input.ProductId.Value),
        P("@ProductCode", input.ProductCode.Trim()), P("@Description", input.Description.Trim()),
        P("@UnitCode", input.UnitCode.Trim()), P("@TaxCode", input.TaxCode.Trim()),
        P("@TaxRate", input.TaxRate), P("@Quantity", input.Quantity),
        P("@BaseUnitPrice", input.BaseUnitPrice), P("@UnitPrice", input.UnitPrice),
        P("@CurrencyCode", input.CurrencyCode.Trim().ToUpperInvariant()),
        P("@PriceSource", input.PriceSource), P("@PriceListId", input.PriceListId),
        P("@PriceChannelId", input.PriceChannelId), P("@Discount", input.Discount),
        P("@Note", Normalize(input.Note)), P("@Position", position)
    ];

    private static PosDraftLineInput ToInput(PosDraftLine line) =>
        new(
            line.ProductId,
            line.ProductCode,
            line.Description,
            line.UnitCode,
            line.TaxCode,
            line.TaxRate,
            line.Quantity,
            line.BaseUnitPrice,
            line.UnitPrice,
            line.CurrencyCode,
            line.PriceSource,
            line.PriceListId,
            line.PriceChannelId,
            line.Discount,
            line.Note);

    private DateTimeOffset Now() => _timeProvider.GetUtcNow();

    private static SqliteParameter P(string name, object? value) =>
        new(name, value switch
        {
            Guid id => id.ToString("D"),
            DateTimeOffset instant => instant.ToString("O", CultureInfo.InvariantCulture),
            decimal number => number.ToString(CultureInfo.InvariantCulture),
            _ => value ?? DBNull.Value
        });

    private static decimal Decimal(SqliteDataReader reader, int ordinal) =>
        decimal.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);

    private static Guid? NullableGuid(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));

    private static string? NullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private const string Schema = """
        PRAGMA foreign_keys=ON;
        CREATE TABLE IF NOT EXISTS PosDrafts(
          DraftId TEXT PRIMARY KEY,
          BusinessId TEXT NOT NULL,
          WarehouseId TEXT NOT NULL,
          RegisterId TEXT NOT NULL,
          UserId TEXT NOT NULL,
          CustomerId TEXT NULL,
          SellerId TEXT NULL,
          Status TEXT NOT NULL CHECK(Status IN ('Active','Temporary','Consumed','Deleted')),
          Name TEXT NULL,
          Reference TEXT NULL,
          Observation TEXT NULL,
          CreatedAt TEXT NOT NULL,
          UpdatedAt TEXT NOT NULL,
          SavedAt TEXT NULL,
          ConsumedAt TEXT NULL,
          DeletedAt TEXT NULL,
          IssuedAt TEXT NULL);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_PosDrafts_ActiveScope
          ON PosDrafts(BusinessId,RegisterId,UserId) WHERE Status='Active';
        CREATE INDEX IF NOT EXISTS IX_PosDrafts_Temporaries
          ON PosDrafts(BusinessId,Status,SavedAt DESC);
        CREATE TABLE IF NOT EXISTS PosDraftLines(
          LineId TEXT PRIMARY KEY,
          DraftId TEXT NOT NULL,
          ProductId TEXT NOT NULL,
          ProductCode TEXT NOT NULL,
          Description TEXT NOT NULL,
          UnitCode TEXT NOT NULL,
          TaxCode TEXT NOT NULL,
          TaxRate TEXT NOT NULL,
          Quantity TEXT NOT NULL,
          BaseUnitPrice TEXT NOT NULL,
          UnitPrice TEXT NOT NULL,
          CurrencyCode TEXT NOT NULL,
          PriceSource TEXT NOT NULL,
          PriceListId TEXT NULL,
          PriceChannelId TEXT NULL,
          Discount TEXT NOT NULL,
          Note TEXT NULL,
          Position INTEGER NOT NULL,
          CHECK(PriceListId IS NULL OR PriceChannelId IS NULL),
          FOREIGN KEY(DraftId) REFERENCES PosDrafts(DraftId) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_PosDraftLines_Draft
          ON PosDraftLines(DraftId,Position);
        CREATE TABLE IF NOT EXISTS PosDraftAudit(
          AuditId TEXT PRIMARY KEY,
          DraftId TEXT NOT NULL,
          Action TEXT NOT NULL,
          ActorUserId TEXT NULL,
          RelatedDraftId TEXT NULL,
          OccurredAt TEXT NOT NULL,
          FOREIGN KEY(DraftId) REFERENCES PosDrafts(DraftId));
        """;
}
