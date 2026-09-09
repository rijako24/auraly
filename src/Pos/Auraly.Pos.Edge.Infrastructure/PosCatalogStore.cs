using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Contracts.Catalog;
using Auraly.Domain.Catalog;
using Microsoft.Data.Sqlite;

namespace Auraly.Pos.Edge.Infrastructure;

public sealed record PosCatalogStatus(
    string Status,
    Guid? SessionId,
    long HighWaterMark,
    long Cursor,
    string? NextPageCursor,
    DateTimeOffset UpdatedAt,
    int TotalProducts,
    int ProcessedProducts);

public sealed record CapturedCatalogProduct(
    PosCatalogItem Product,
    decimal Quantity,
    string MatchKind);

public sealed partial class PosCatalogStore(string connectionString, TimeProvider? timeProvider = null)
{
    private TimeProvider Clock { get; } = timeProvider ?? TimeProvider.System;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = Schema;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureProgressStateAsync(connection, cancellationToken);
        await EnsureProductFlagsAsync(connection, cancellationToken);
        await InitializePricingAsync(connection, cancellationToken);
    }

    public async Task<PosCatalogStatus> StatusAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Status,SessionId,HighWaterMark,Cursor,NextPageCursor,UpdatedAt,
                   TotalProducts,
                   CASE WHEN Status='Bootstrapping'
                        THEN (SELECT COUNT(*) FROM PosCatalogStagingProducts)
                        ELSE TotalProducts END
            FROM PosCatalogState WHERE StateId=1;
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("POS catalog storage has not been initialized.");
        return new PosCatalogStatus(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            reader.GetInt32(6),
            reader.GetInt32(7));
    }

    public async Task BeginBootstrapAsync(
        CatalogSyncSessionResponse session,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction, """
            DELETE FROM PosCatalogStagingBarcodes;
            DELETE FROM PosCatalogStagingIdentifiers;
            DELETE FROM PosCatalogStagingProducts;
            UPDATE PosCatalogState
            SET Status='Bootstrapping',SessionId=@SessionId,HighWaterMark=@HighWaterMark,
                NextPageCursor=NULL,TotalProducts=@TotalProducts,UpdatedAt=@Now
            WHERE StateId=1;
            """,
            [
                P("@SessionId", session.SessionId.ToString("D")),
                P("@HighWaterMark", session.HighWaterMark),
                P("@TotalProducts", session.TotalProducts),
                P("@Now", Clock.GetUtcNow().ToString("O"))
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task InvalidateBootstrapAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PosCatalogState SET Status='Invalid',SessionId=NULL,NextPageCursor=NULL,UpdatedAt=@Now WHERE StateId=1 AND Status='Bootstrapping';";
        command.Parameters.Add(P("@Now", Clock.GetUtcNow().ToString("O")));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> IsPreparationPausedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT PreparationPaused FROM PosCatalogState WHERE StateId=1;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task SetPreparationPausedAsync(bool paused, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE PosCatalogState SET PreparationPaused=@Paused WHERE StateId=1;";
        command.Parameters.Add(P("@Paused", paused ? 1 : 0));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ApplyBootstrapPageAsync(
        CatalogBootstrapPage page,
        CancellationToken cancellationToken = default)
    {
        var expectedHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(page.Items))))
            .ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(expectedHash),
                Encoding.ASCII.GetBytes(page.IntegrityHash.ToLowerInvariant())))
            throw new InvalidDataException("The catalog bootstrap page failed its integrity validation.");

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await StatusAsync(connection, transaction, cancellationToken);
        if (status.Status != "Bootstrapping" || status.SessionId != page.SessionId ||
            status.HighWaterMark != page.HighWaterMark)
            throw new InvalidOperationException("The bootstrap page does not belong to the active session.");

        await UpsertBatchAsync(connection, transaction, page.Items, staging: true, cancellationToken);

        await ExecuteAsync(connection, transaction, """
            UPDATE PosCatalogState SET NextPageCursor=@Next,UpdatedAt=@Now WHERE StateId=1;
            """,
            [P("@Next", page.NextCursor), P("@Now", Clock.GetUtcNow().ToString("O"))],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PromoteBootstrapAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await StatusAsync(connection, transaction, cancellationToken);
        if (status.Status != "Bootstrapping")
            throw new InvalidOperationException("There is no bootstrap ready to promote.");

        await ExecuteAsync(connection, transaction, """
            DELETE FROM PosCatalogBarcodes;
            DELETE FROM PosCatalogIdentifiers;
            DELETE FROM PosCatalogProducts;
            INSERT INTO PosCatalogProducts
              SELECT * FROM PosCatalogStagingProducts;
            INSERT INTO PosCatalogBarcodes
              SELECT * FROM PosCatalogStagingBarcodes;
            INSERT INTO PosCatalogIdentifiers
              SELECT * FROM PosCatalogStagingIdentifiers;
            DELETE FROM PosCatalogStagingBarcodes;
            DELETE FROM PosCatalogStagingIdentifiers;
            DELETE FROM PosCatalogStagingProducts;
            UPDATE PosCatalogState
            SET Status='Ready',Cursor=HighWaterMark,SessionId=NULL,NextPageCursor=NULL,UpdatedAt=@Now
            WHERE StateId=1;
            """,
            [P("@Now", Clock.GetUtcNow().ToString("O"))],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ApplyChangesAsync(
        CatalogDeltaPage page,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var status = await StatusAsync(connection, transaction, cancellationToken);
        if (status.Status != "Ready")
            throw new InvalidOperationException("Incremental changes require a ready local catalog.");
        if (page.ToCursor <= status.Cursor)
        {
            await transaction.RollbackAsync(cancellationToken);
            return;
        }
        if (page.FromCursor != status.Cursor)
            throw new InvalidOperationException("The incremental page is outside the durable local cursor.");

        var expectedVersion = status.Cursor;
        var products = new List<PosCatalogItem>(page.Changes.Count);
        foreach (var change in page.Changes)
        {
            if (change.Version <= expectedVersion)
                throw new InvalidOperationException("Catalog changes must be strictly ordered.");
            products.Add(change.Product);
            expectedVersion = change.Version;
        }
        if (expectedVersion != page.ToCursor)
            throw new InvalidOperationException("The catalog response cursor does not match its changes.");
        await UpsertBatchAsync(connection, transaction, products, staging: false, cancellationToken);

        await ExecuteAsync(connection, transaction,
            "UPDATE PosCatalogState SET Cursor=@Cursor,UpdatedAt=@Now WHERE StateId=1;",
            [P("@Cursor", page.ToCursor), P("@Now", Clock.GetUtcNow().ToString("O"))],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CapturedCatalogProduct?> CaptureAsync(
        string value,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim();
        var exact = await FindSingleAsync(normalized, cancellationToken);
        if (exact is not null) return new CapturedCatalogProduct(exact, 1m, "Exact");

        var candidates = await ScaleCandidatesAsync(normalized, cancellationToken);
        foreach (var candidate in candidates)
        {
            ScaleBarcodeValue parsed;
            try
            {
                parsed = ScaleBarcodeParser.Parse(normalized, candidate.Rule);
            }
            catch (FormatException)
            {
                continue;
            }
            if (!string.Equals(parsed.ProductCode, candidate.Product.Scale?.ScaleCode, StringComparison.Ordinal))
                continue;
            var quantity = candidate.Rule.EmbeddedValueType.Equals("Weight", StringComparison.OrdinalIgnoreCase)
                ? parsed.Value
                : parsed.Value / candidate.Product.UnitPrice;
            return new CapturedCatalogProduct(candidate.Product, quantity, "Scale");
        }
        return null;
    }

    public async Task<IReadOnlyCollection<PosCatalogItem>> SearchAsync(
        string term,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.*
            FROM PosCatalogProducts p
            WHERE p.IsActive=1
              AND (
                @Exact='' OR
                p.ProductCode LIKE @Prefix OR
                p.Reference LIKE @Prefix OR
                p.Name LIKE @Name OR
                EXISTS(
                  SELECT 1 FROM PosCatalogBarcodes b
                  WHERE b.ProductId=p.ProductId AND b.Value LIKE @Prefix) OR
                EXISTS(
                  SELECT 1 FROM PosCatalogIdentifiers i
                  WHERE i.ProductId=p.ProductId AND i.Value LIKE @Prefix))
            ORDER BY CASE WHEN @Exact<>'' AND (
                       p.ProductCode=@Exact OR p.Reference=@Exact OR
                       EXISTS(SELECT 1 FROM PosCatalogBarcodes b WHERE b.ProductId=p.ProductId AND b.Value=@Exact) OR
                       EXISTS(SELECT 1 FROM PosCatalogIdentifiers i WHERE i.ProductId=p.ProductId AND i.Value=@Exact))
                     THEN 0 ELSE 1 END,
                     p.Name,p.ProductId
            LIMIT @Take OFFSET @Skip;
            """;
        var normalized = term.Trim();
        command.Parameters.AddRange([
            P("@Exact", normalized), P("@Prefix", $"{normalized}%"),
            P("@Name", $"%{normalized}%"), P("@Take", take), P("@Skip", skip)]);
        var products = new List<PosCatalogItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            products.Add(ReadProduct(reader));
        return products;
    }

    public async Task<PosCatalogItem?> GetByProductIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty) return null;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT * FROM PosCatalogProducts WHERE ProductId=@ProductId AND IsActive=1;";
        command.Parameters.Add(P("@ProductId", productId.ToString("D")));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadProduct(reader) : null;
    }

    private async Task<PosCatalogItem?> FindSingleAsync(string value, CancellationToken ct)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DISTINCT p.* FROM PosCatalogProducts p
            LEFT JOIN PosCatalogBarcodes b ON b.ProductId=p.ProductId
            LEFT JOIN PosCatalogIdentifiers i ON i.ProductId=p.ProductId
            WHERE p.IsActive=1 AND (p.ProductCode=@Value OR p.Reference=@Value OR b.Value=@Value OR i.Value=@Value)
            LIMIT 2;
            """;
        command.Parameters.Add(P("@Value", value));
        var products = new List<PosCatalogItem>(2);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) products.Add(ReadProduct(reader));
        return products.Count == 1 ? products[0] : null;
    }

    private async Task<IReadOnlyCollection<(PosCatalogItem Product, ScaleBarcodeRule Rule)>> ScaleCandidatesAsync(
        string barcode,
        CancellationToken ct)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM PosCatalogProducts WHERE IsActive=1 AND ScaleJson IS NOT NULL AND @Code LIKE ScalePrefix || '%';";
        command.Parameters.Add(P("@Code", barcode));
        var values = new List<(PosCatalogItem, ScaleBarcodeRule)>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var item = ReadProduct(reader);
            var scale = item.Scale!;
            values.Add((item, new ScaleBarcodeRule(
                scale.BarcodePrefix,
                scale.BarcodePrefix.Length,
                scale.ScaleCode.Length,
                scale.ValueStart,
                scale.ValueLength,
                scale.DecimalPlaces,
                scale.EmbeddedValueType)));
        }
        return values;
    }

    private static async Task UpsertBatchAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        IReadOnlyCollection<PosCatalogItem> items,
        bool staging,
        CancellationToken ct)
    {
        var products = staging ? "PosCatalogStagingProducts" : "PosCatalogProducts";
        var barcodes = staging ? "PosCatalogStagingBarcodes" : "PosCatalogBarcodes";
        var identifiers = staging ? "PosCatalogStagingIdentifiers" : "PosCatalogIdentifiers";
        var effectiveItems = items
            .GroupBy(item => item.ProductId)
            .Select(group => group.Last())
            .ToArray();

        foreach (var chunk in effectiveItems.Chunk(400))
        {
            var parameters = chunk
                .Select((item, index) => P($"@ProductId{index}", item.ProductId.ToString("D")))
                .ToArray();
            var productIds = string.Join(',', parameters.Select(parameter => parameter.ParameterName));
            await ExecuteAsync(connection, transaction, $"""
                DELETE FROM {barcodes} WHERE ProductId IN ({productIds});
                DELETE FROM {identifiers} WHERE ProductId IN ({productIds});
                """, parameters, ct);
        }

        await using var productCommand = connection.CreateCommand();
        productCommand.Transaction = (SqliteTransaction)transaction;
        productCommand.CommandText = $"""
            INSERT INTO {products}
              (ProductId,ProductCode,Reference,Name,BaseUnitCode,TaxCode,TaxRate,UnitPrice,UnitCost,ManagesStock,CurrencyCode,IsActive,IsWeighable,AllowsFractionalSale,ScaleJson,ScalePrefix,CategoryName,
               ProductCategoryId,ProductBrandId,ProductCategoryAncestorIds,AverageUnitCost,LatestUnitCost,TargetMarginPercent)
            VALUES
              (@ProductId,@ProductCode,@Reference,@Name,@BaseUnitCode,@TaxCode,@TaxRate,@UnitPrice,@UnitCost,@ManagesStock,@CurrencyCode,@IsActive,@IsWeighable,@AllowsFractionalSale,@ScaleJson,@ScalePrefix,@CategoryName,
               @ProductCategoryId,@ProductBrandId,@ProductCategoryAncestorIds,@AverageUnitCost,@LatestUnitCost,@TargetMarginPercent)
            ON CONFLICT(ProductId) DO UPDATE SET
              ProductCode=excluded.ProductCode,Reference=excluded.Reference,Name=excluded.Name,
              BaseUnitCode=excluded.BaseUnitCode,TaxCode=excluded.TaxCode,TaxRate=excluded.TaxRate,
              UnitPrice=excluded.UnitPrice,UnitCost=excluded.UnitCost,ManagesStock=excluded.ManagesStock,CurrencyCode=excluded.CurrencyCode,IsActive=excluded.IsActive,
              IsWeighable=excluded.IsWeighable,AllowsFractionalSale=excluded.AllowsFractionalSale,
              ScaleJson=excluded.ScaleJson,ScalePrefix=excluded.ScalePrefix,CategoryName=excluded.CategoryName,
              ProductCategoryId=excluded.ProductCategoryId,ProductBrandId=excluded.ProductBrandId,
              ProductCategoryAncestorIds=excluded.ProductCategoryAncestorIds,
              AverageUnitCost=excluded.AverageUnitCost,LatestUnitCost=excluded.LatestUnitCost,
              TargetMarginPercent=excluded.TargetMarginPercent;
            """;
        foreach (var name in new[]
                 {
                     "@ProductId", "@ProductCode", "@Reference", "@Name", "@BaseUnitCode", "@TaxCode",
                     "@TaxRate", "@UnitPrice", "@UnitCost", "@ManagesStock", "@CurrencyCode", "@IsActive",
                     "@IsWeighable", "@AllowsFractionalSale", "@ScaleJson", "@ScalePrefix", "@CategoryName",
                     "@ProductCategoryId", "@ProductBrandId", "@ProductCategoryAncestorIds", "@AverageUnitCost",
                     "@LatestUnitCost", "@TargetMarginPercent"
                 })
            productCommand.Parameters.Add(P(name, null));

        await using var barcodeCommand = connection.CreateCommand();
        barcodeCommand.Transaction = (SqliteTransaction)transaction;
        barcodeCommand.CommandText =
            $"INSERT INTO {barcodes}(ProductId,Value) VALUES(@ProductId,@Value);";
        barcodeCommand.Parameters.Add(P("@ProductId", null));
        barcodeCommand.Parameters.Add(P("@Value", null));

        await using var identifierCommand = connection.CreateCommand();
        identifierCommand.Transaction = (SqliteTransaction)transaction;
        identifierCommand.CommandText =
            $"INSERT INTO {identifiers}(ProductId,Type,Value) VALUES(@ProductId,@Type,@Value);";
        identifierCommand.Parameters.Add(P("@ProductId", null));
        identifierCommand.Parameters.Add(P("@Type", null));
        identifierCommand.Parameters.Add(P("@Value", null));

        await productCommand.PrepareAsync(ct);
        await barcodeCommand.PrepareAsync(ct);
        await identifierCommand.PrepareAsync(ct);

        foreach (var item in effectiveItems)
        {
            var productId = item.ProductId.ToString("D");
            var values = new object?[]
            {
                productId, item.ProductCode, item.Reference, item.Name, item.BaseUnitCode, item.TaxCode,
                item.TaxRate, item.UnitPrice, item.UnitCost, item.ManagesStock ? 1 : 0, item.CurrencyCode,
                item.IsActive ? 1 : 0, item.IsWeighable ? 1 : 0, item.AllowsFractionalSale ? 1 : 0,
                item.Scale is null ? null : JsonSerializer.Serialize(item.Scale), item.Scale?.BarcodePrefix,
                item.CategoryName, item.ProductCategoryId?.ToString("D"), item.ProductBrandId?.ToString("D"),
                JsonSerializer.Serialize(item.ProductCategoryAncestorIds ?? []), item.AverageUnitCost,
                item.LatestUnitCost, item.TargetMarginPercent
            };
            for (var index = 0; index < values.Length; index++)
                productCommand.Parameters[index].Value = values[index] ?? DBNull.Value;
            await productCommand.ExecuteNonQueryAsync(ct);

            barcodeCommand.Parameters[0].Value = productId;
            foreach (var barcode in item.Barcodes.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                barcodeCommand.Parameters[1].Value = barcode;
                await barcodeCommand.ExecuteNonQueryAsync(ct);
            }

            identifierCommand.Parameters[0].Value = productId;
            foreach (var identifier in item.Identifiers)
            {
                identifierCommand.Parameters[1].Value = identifier.Type;
                identifierCommand.Parameters[2].Value = identifier.Value;
                await identifierCommand.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static PosCatalogItem ReadProduct(SqliteDataReader reader)
    {
        var scaleOrdinal = reader.GetOrdinal("ScaleJson");
        var scale = reader.IsDBNull(scaleOrdinal)
            ? null
            : JsonSerializer.Deserialize<ScaleConfigurationInput>(reader.GetString(scaleOrdinal));
        return new PosCatalogItem(
            Guid.Parse(reader.GetString(reader.GetOrdinal("ProductId"))),
            reader.GetString(reader.GetOrdinal("ProductCode")),
            reader.IsDBNull(reader.GetOrdinal("Reference")) ? null : reader.GetString(reader.GetOrdinal("Reference")),
            reader.GetString(reader.GetOrdinal("Name")),
            reader.GetString(reader.GetOrdinal("BaseUnitCode")),
            reader.GetString(reader.GetOrdinal("TaxCode")),
            Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("TaxRate")), CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("UnitPrice")), CultureInfo.InvariantCulture),
            reader.GetString(reader.GetOrdinal("CurrencyCode")),
            reader.GetInt64(reader.GetOrdinal("IsActive")) == 1,
            reader.GetInt64(reader.GetOrdinal("IsWeighable")) == 1,
            reader.GetInt64(reader.GetOrdinal("AllowsFractionalSale")) == 1,
            scale,
            [],
            [],
            Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("UnitCost")), CultureInfo.InvariantCulture),
            reader.GetInt64(reader.GetOrdinal("ManagesStock")) == 1,
            reader.IsDBNull(reader.GetOrdinal("CategoryName")) ? null : reader.GetString(reader.GetOrdinal("CategoryName")),
            reader.IsDBNull(reader.GetOrdinal("ProductCategoryId")) ? null : Guid.Parse(reader.GetString(reader.GetOrdinal("ProductCategoryId"))),
            reader.IsDBNull(reader.GetOrdinal("ProductBrandId")) ? null : Guid.Parse(reader.GetString(reader.GetOrdinal("ProductBrandId"))),
            reader.IsDBNull(reader.GetOrdinal("ProductCategoryAncestorIds")) ? [] :
                JsonSerializer.Deserialize<Guid[]>(reader.GetString(reader.GetOrdinal("ProductCategoryAncestorIds"))) ?? [],
            Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("AverageUnitCost")), CultureInfo.InvariantCulture),
            Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("LatestUnitCost")), CultureInfo.InvariantCulture),
            reader.IsDBNull(reader.GetOrdinal("TargetMarginPercent")) ? null :
                Convert.ToDecimal(reader.GetValue(reader.GetOrdinal("TargetMarginPercent")), CultureInfo.InvariantCulture));
    }

    private static async Task<PosCatalogStatus> StatusAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            SELECT Status,SessionId,HighWaterMark,Cursor,NextPageCursor,UpdatedAt,
                   TotalProducts,
                   CASE WHEN Status='Bootstrapping'
                        THEN (SELECT COUNT(*) FROM PosCatalogStagingProducts)
                        ELSE TotalProducts END
            FROM PosCatalogState WHERE StateId=1;
            """;
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new InvalidOperationException("POS catalog state is missing.");
        return new PosCatalogStatus(
            reader.GetString(0),
            reader.IsDBNull(1) ? null : Guid.Parse(reader.GetString(1)),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
            reader.GetInt32(6),
            reader.GetInt32(7));
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string sql,
        SqliteParameter[] parameters,
        CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static SqliteParameter P(string name, object? value) => new(name, value ?? DBNull.Value);

    private static async Task EnsureProductFlagsAsync(SqliteConnection connection, CancellationToken ct)
    {
        foreach (var table in new[] { "PosCatalogProducts", "PosCatalogStagingProducts" })
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var info = connection.CreateCommand())
            {
                info.CommandText = $"PRAGMA table_info('{table}');";
                await using var reader = await info.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct)) columns.Add(reader.GetString(1));
            }
            foreach (var column in new[] { "IsWeighable", "AllowsFractionalSale" })
            {
                if (columns.Contains(column)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} INTEGER NOT NULL DEFAULT 0;";
                await alter.ExecuteNonQueryAsync(ct);
            }
            if (!columns.Contains("UnitCost"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN UnitCost TEXT NOT NULL DEFAULT '0';";
                await alter.ExecuteNonQueryAsync(ct);
            }
            if (!columns.Contains("ManagesStock"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN ManagesStock INTEGER NOT NULL DEFAULT 1;";
                await alter.ExecuteNonQueryAsync(ct);
            }
            if (!columns.Contains("CategoryName"))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN CategoryName TEXT NULL;";
                await alter.ExecuteNonQueryAsync(ct);
            }
            foreach (var column in new[] { "ProductCategoryId", "ProductBrandId", "ProductCategoryAncestorIds", "TargetMarginPercent" })
            {
                if (columns.Contains(column)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NULL;";
                await alter.ExecuteNonQueryAsync(ct);
            }
            foreach (var column in new[] { "AverageUnitCost", "LatestUnitCost" })
            {
                if (columns.Contains(column)) continue;
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} TEXT NOT NULL DEFAULT '0';";
                await alter.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static async Task EnsureProgressStateAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info('PosCatalogState');";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) columns.Add(reader.GetString(1));
        }
        if (!columns.Contains("TotalProducts"))
        {
            await using var upgrade = connection.CreateCommand();
            upgrade.CommandText = "ALTER TABLE PosCatalogState ADD COLUMN TotalProducts INTEGER NOT NULL DEFAULT 0;";
            await upgrade.ExecuteNonQueryAsync(cancellationToken);
        }
        if (!columns.Contains("PreparationPaused"))
        {
            await using var upgrade = connection.CreateCommand();
            upgrade.CommandText = "ALTER TABLE PosCatalogState ADD COLUMN PreparationPaused INTEGER NOT NULL DEFAULT 0;";
            await upgrade.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private const string ProductColumns = """
        ProductId TEXT PRIMARY KEY,
        ProductCode TEXT NOT NULL,
        Reference TEXT NULL,
        Name TEXT NOT NULL,
        BaseUnitCode TEXT NOT NULL,
        TaxCode TEXT NOT NULL,
        TaxRate TEXT NOT NULL,
        UnitPrice TEXT NOT NULL,
        UnitCost TEXT NOT NULL DEFAULT '0',
        ManagesStock INTEGER NOT NULL DEFAULT 1,
        CurrencyCode TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        IsWeighable INTEGER NOT NULL DEFAULT 0,
        AllowsFractionalSale INTEGER NOT NULL DEFAULT 0,
        ScaleJson TEXT NULL,
        ScalePrefix TEXT NULL,
        CategoryName TEXT NULL,
        ProductCategoryId TEXT NULL,
        ProductBrandId TEXT NULL,
        ProductCategoryAncestorIds TEXT NULL,
        AverageUnitCost TEXT NOT NULL DEFAULT '0',
        LatestUnitCost TEXT NOT NULL DEFAULT '0',
        TargetMarginPercent TEXT NULL
        """;

    private static readonly string Schema = $"""
        PRAGMA foreign_keys=ON;
        CREATE TABLE IF NOT EXISTS PosCatalogState(
          StateId INTEGER PRIMARY KEY CHECK(StateId=1),
          Status TEXT NOT NULL,
          SessionId TEXT NULL,
          HighWaterMark INTEGER NOT NULL,
          Cursor INTEGER NOT NULL,
          NextPageCursor TEXT NULL,
          TotalProducts INTEGER NOT NULL DEFAULT 0,
          PreparationPaused INTEGER NOT NULL DEFAULT 0,
          UpdatedAt TEXT NOT NULL);
        INSERT OR IGNORE INTO PosCatalogState(StateId,Status,HighWaterMark,Cursor,UpdatedAt)
          VALUES(1,'Empty',0,0,'1970-01-01T00:00:00+00:00');
        CREATE TABLE IF NOT EXISTS PosCatalogProducts({ProductColumns});
        CREATE TABLE IF NOT EXISTS PosCatalogStagingProducts({ProductColumns});
        CREATE TABLE IF NOT EXISTS PosCatalogBarcodes(
          ProductId TEXT NOT NULL,Value TEXT NOT NULL,
          PRIMARY KEY(ProductId,Value),
          FOREIGN KEY(ProductId) REFERENCES PosCatalogProducts(ProductId) ON DELETE CASCADE);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_PosCatalogBarcodes_Value ON PosCatalogBarcodes(Value);
        CREATE TABLE IF NOT EXISTS PosCatalogIdentifiers(
          ProductId TEXT NOT NULL,Type TEXT NOT NULL,Value TEXT NOT NULL,
          PRIMARY KEY(ProductId,Type,Value),
          FOREIGN KEY(ProductId) REFERENCES PosCatalogProducts(ProductId) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_PosCatalogIdentifiers_Value ON PosCatalogIdentifiers(Value);
        CREATE TABLE IF NOT EXISTS PosReferenceOptions(
          CatalogCode TEXT NOT NULL,
          OptionId TEXT NOT NULL,
          Code TEXT NOT NULL,
          Label TEXT NOT NULL,
          Description TEXT NULL,
          SortOrder INTEGER NOT NULL,
          PRIMARY KEY(CatalogCode,OptionId));
        CREATE UNIQUE INDEX IF NOT EXISTS UX_PosReferenceOptions_Code
          ON PosReferenceOptions(CatalogCode,Code);
        CREATE TABLE IF NOT EXISTS PosBankAccounts(
          BankAccountId TEXT PRIMARY KEY,
          DisplayName TEXT NOT NULL,
          BankName TEXT NOT NULL,
          AccountNumber TEXT NOT NULL,
          AccountTypeName TEXT NOT NULL,
          IsPrimary INTEGER NOT NULL,
          RowVersion TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS PosSettlementConfiguration(
          ConfigurationId INTEGER PRIMARY KEY CHECK(ConfigurationId=1),
          IsAccountingEnabled INTEGER NOT NULL CHECK(IsAccountingEnabled IN (0,1)));
        INSERT OR IGNORE INTO PosSettlementConfiguration(ConfigurationId,IsAccountingEnabled)
          VALUES(1,0);
        CREATE TABLE IF NOT EXISTS PosCatalogStagingBarcodes(
          ProductId TEXT NOT NULL,Value TEXT NOT NULL,
          PRIMARY KEY(ProductId,Value),
          FOREIGN KEY(ProductId) REFERENCES PosCatalogStagingProducts(ProductId) ON DELETE CASCADE);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_PosCatalogStagingBarcodes_Value ON PosCatalogStagingBarcodes(Value);
        CREATE TABLE IF NOT EXISTS PosCatalogStagingIdentifiers(
          ProductId TEXT NOT NULL,Type TEXT NOT NULL,Value TEXT NOT NULL,
          PRIMARY KEY(ProductId,Type,Value),
          FOREIGN KEY(ProductId) REFERENCES PosCatalogStagingProducts(ProductId) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS IX_PosCatalogProducts_Search ON PosCatalogProducts(ProductCode,Reference,Name);
        """;
}
