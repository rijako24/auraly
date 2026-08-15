using Auraly.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;

namespace Auraly.Api;

public static class PriceSegmentsApi
{
    public static IEndpointRouteBuilder MapPriceSegmentsApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce/v1/pricing/segments")
            .RequireAuthorization("pricing.user");

        group.MapGet("/", ListAsync);
        group.MapPost("/", SaveAsync);
        group.MapGet("/{kind}/{id:guid}/items", ItemsAsync);
        group.MapPut("/{kind}/{id:guid}/items/{productId:guid}", SaveItemAsync);
        group.MapDelete("/{kind}/{id:guid}/items/{productId:guid}", DeleteItemAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context, SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.read")) return Results.Forbid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        const string sql = """
            SELECT l.PriceListId,N'PriceList',l.Code,l.Name,l.IsActive,l.CreatedAt,
              (SELECT COUNT(DISTINCT i.ProductId) FROM dbo.PriceListItems i WHERE i.PriceListId=l.PriceListId AND i.IsActive=1),
              (SELECT COUNT(*) FROM dbo.CustomerPricingSettings s JOIN dbo.Customers c ON c.CustomerId=s.CustomerId WHERE c.BusinessId=l.BusinessId AND s.PriceListId=l.PriceListId)
            FROM dbo.PriceLists l WHERE l.BusinessId=@BusinessId
            UNION ALL
            SELECT c.PriceChannelId,N'PriceChannel',c.Code,c.Name,c.IsActive,c.CreatedAt,
              (SELECT COUNT(*) FROM dbo.ResolvedPriceChannelItems i WHERE i.PriceChannelId=c.PriceChannelId AND i.IsActive=1),
              (SELECT COUNT(*) FROM dbo.CustomerPricingSettings s JOIN dbo.Customers customer ON customer.CustomerId=s.CustomerId WHERE customer.BusinessId=c.BusinessId AND s.PriceChannelId=c.PriceChannelId)
            FROM dbo.PriceChannels c WHERE c.BusinessId=@BusinessId
            ORDER BY 2,4;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<PriceSegmentSummary>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetBoolean(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetInt32(6), reader.GetInt32(7)));
        return Results.Ok(items);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext context, SavePriceSegmentRequest request,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        var kind = NormalizeKind(request.Kind);
        var code = request.Code?.Trim().ToUpperInvariant();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 32 || string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Results.Problem("Código y nombre son obligatorios.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var id = Guid.NewGuid();
        var table = kind == "PriceList" ? "PriceLists" : "PriceChannels";
        var key = kind == "PriceList" ? "PriceListId" : "PriceChannelId";
        await using var command = new SqlCommand($"""
            INSERT dbo.{table}({key},BusinessId,Code,Name,IsActive,CreatedAt)
            VALUES(@Id,@BusinessId,@Code,@Name,1,SYSUTCDATETIME());
            """, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name);
        try { await command.ExecuteNonQueryAsync(ct); }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        { return Results.Problem("Ya existe un segmento con ese código.", statusCode: 409); }
        return Results.Ok(new { id, kind, code, name });
    }

    private static async Task<IResult> ItemsAsync(
        HttpContext context, string kind, Guid id, SqlServerConnectionFactory connections,
        CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.read")) return Results.Forbid();
        kind = NormalizeKind(kind);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var sql = kind == "PriceList" ? """
            SELECT p.ProductId,p.ProductCode,p.Name,i.Amount,i.CurrencyCode,i.MinimumQuantity,
                   i.ValidFrom,i.ValidUntil,CAST(0 AS bit)
            FROM dbo.PriceListItems i
            JOIN dbo.PriceLists l ON l.PriceListId=i.PriceListId
            JOIN dbo.Products p ON p.ProductId=i.ProductId
            WHERE i.PriceListId=@Id AND l.BusinessId=@BusinessId AND i.IsActive=1
            ORDER BY p.Name,i.MinimumQuantity;
            """ : """
            SELECT p.ProductId,p.ProductCode,p.Name,i.Amount,i.CurrencyCode,CAST(1 AS decimal(19,6)),
                   i.ValidFrom,i.ValidUntil,CASE WHEN e.ProductId IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END
            FROM dbo.ResolvedPriceChannelItems i
            JOIN dbo.PriceChannels c ON c.PriceChannelId=i.PriceChannelId
            JOIN dbo.Products p ON p.ProductId=i.ProductId
            LEFT JOIN dbo.PriceChannelExclusions e ON e.PriceChannelId=i.PriceChannelId AND e.ProductId=i.ProductId
            WHERE i.PriceChannelId=@Id AND c.BusinessId=@BusinessId AND i.IsActive=1
            ORDER BY p.Name;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<PriceSegmentItem>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.GetString(2), reader.GetDecimal(3), reader.GetString(4), reader.GetDecimal(5),
                reader.GetFieldValue<DateTimeOffset>(6), reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetBoolean(8)));
        return Results.Ok(items);
    }

    private static async Task<IResult> SaveItemAsync(
        HttpContext context, string kind, Guid id, Guid productId,
        SavePriceSegmentItemRequest request, SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        kind = NormalizeKind(kind);
        if (request.Amount <= 0 || request.MinimumQuantity <= 0)
            return Results.Problem("Precio y cantidad mínima deben ser mayores que cero.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var sql = kind == "PriceList" ? """
            IF NOT EXISTS(SELECT 1 FROM dbo.PriceLists WHERE PriceListId=@Id AND BusinessId=@BusinessId) THROW 51004,'Segment not found',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1) THROW 51004,'Product not found',1;
            UPDATE dbo.PriceListItems SET IsActive=0 WHERE PriceListId=@Id AND ProductId=@ProductId AND MinimumQuantity=@MinimumQuantity AND IsActive=1;
            INSERT dbo.PriceListItems(PriceListItemId,PriceListId,ProductId,MinimumQuantity,Amount,CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt)
            VALUES(NEWID(),@Id,@ProductId,@MinimumQuantity,@Amount,N'COP',@ValidFrom,@ValidUntil,1,SYSUTCDATETIME());
            """ : """
            IF NOT EXISTS(SELECT 1 FROM dbo.PriceChannels WHERE PriceChannelId=@Id AND BusinessId=@BusinessId) THROW 51004,'Segment not found',1;
            IF NOT EXISTS(SELECT 1 FROM dbo.Products WHERE ProductId=@ProductId AND BusinessId=@BusinessId AND IsActive=1) THROW 51004,'Product not found',1;
            UPDATE dbo.ResolvedPriceChannelItems SET IsActive=0 WHERE PriceChannelId=@Id AND ProductId=@ProductId AND IsActive=1;
            INSERT dbo.ResolvedPriceChannelItems(ResolvedPriceChannelItemId,PriceChannelId,ProductId,Amount,CurrencyCode,ValidFrom,ValidUntil,IsActive,CreatedAt)
            VALUES(NEWID(),@Id,@ProductId,@Amount,N'COP',@ValidFrom,@ValidUntil,1,SYSUTCDATETIME());
            DELETE dbo.PriceChannelExclusions WHERE PriceChannelId=@Id AND ProductId=@ProductId;
            IF @Excluded=1 INSERT dbo.PriceChannelExclusions(PriceChannelId,ProductId,CreatedAt) VALUES(@Id,@ProductId,SYSUTCDATETIME());
            """;
        await using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@MinimumQuantity", request.MinimumQuantity);
        command.Parameters.AddWithValue("@Amount", request.Amount);
        command.Parameters.AddWithValue("@ValidFrom", request.ValidFrom ?? DateTimeOffset.UtcNow);
        command.Parameters.AddWithValue("@ValidUntil", (object?)request.ValidUntil ?? DBNull.Value);
        command.Parameters.AddWithValue("@Excluded", request.Excluded);
        try { await command.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct); }
        catch (SqlException exception) when (exception.Number == 51004)
        { await transaction.RollbackAsync(ct); return Results.NotFound(); }
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteItemAsync(
        HttpContext context, string kind, Guid id, Guid productId, decimal? minimumQuantity,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        kind = NormalizeKind(kind);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        var sql = kind == "PriceList" ? """
            UPDATE item
            SET IsActive=0
            FROM dbo.PriceListItems item
            JOIN dbo.PriceLists listValue ON listValue.PriceListId=item.PriceListId
            WHERE item.PriceListId=@Id AND item.ProductId=@ProductId
              AND item.MinimumQuantity=@MinimumQuantity
              AND listValue.BusinessId=@BusinessId AND item.IsActive=1;
            """ : """
            UPDATE item
            SET IsActive=0
            FROM dbo.ResolvedPriceChannelItems item
            JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=item.PriceChannelId
            WHERE item.PriceChannelId=@Id AND item.ProductId=@ProductId
              AND channelValue.BusinessId=@BusinessId AND item.IsActive=1;
            DELETE exclusion
            FROM dbo.PriceChannelExclusions exclusion
            JOIN dbo.PriceChannels channelValue ON channelValue.PriceChannelId=exclusion.PriceChannelId
            WHERE exclusion.PriceChannelId=@Id AND exclusion.ProductId=@ProductId
              AND channelValue.BusinessId=@BusinessId;
            """;
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@MinimumQuantity", minimumQuantity ?? 1m);
        await command.ExecuteNonQueryAsync(ct);
        return Results.NoContent();
    }

    private static string NormalizeKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "pricelist" or "list" or "listas" => "PriceList",
        "pricechannel" or "channel" or "canales" => "PriceChannel",
        _ => throw new BadHttpRequestException("El tipo debe ser PriceList o PriceChannel.")
    };
}

public sealed record PriceSegmentSummary(Guid Id, string Kind, string Code, string Name, bool IsActive, DateTimeOffset CreatedAt, int ProductCount, int CustomerCount);
public sealed record PriceSegmentItem(Guid ProductId, string ProductCode, string ProductName, decimal Amount, string CurrencyCode, decimal MinimumQuantity, DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, bool Excluded);
public sealed record SavePriceSegmentRequest(string Kind, string Code, string Name);
public sealed record SavePriceSegmentItemRequest(decimal Amount, decimal MinimumQuantity, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, bool Excluded);
