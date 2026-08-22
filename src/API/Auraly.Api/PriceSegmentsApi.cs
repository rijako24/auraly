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
        group.MapPut("/PriceChannel/{id:guid}/settings", SaveChannelSettingsAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        HttpContext context, SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.read")) return Results.Forbid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceSegmentsList", connection);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<PriceSegmentSummary>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetBoolean(4), reader.GetFieldValue<DateTimeOffset>(5),
                reader.GetInt32(6), reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetDecimal(8)));
        return Results.Ok(items);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext context, SavePriceSegmentRequest request,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        var kind = NormalizeKind(request.Kind);
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Results.Problem("El nombre es obligatorio.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var id = Guid.NewGuid();
        var code = $"{(kind == "PriceList" ? "LST" : "CNL")}-{id:N}".ToUpperInvariant()[..12];
        var requestedItems = request.Items ?? [];
        if (kind == "PriceList" && requestedItems.Any(item => item.ProductId == Guid.Empty || item.Amount <= 0 || item.MinimumQuantity <= 0))
            return Results.Problem("Todos los productos necesitan precio y cantidad mínima válidos.", statusCode: 400);
        if (kind == "PriceChannel" && request.PriceVariationPercent is < -100 or > 1000)
            return Results.Problem("La variación debe estar entre -100 % y 1.000 %.", statusCode: 400);
        await using var command = Procedure("dbo.PriceSegmentCreate", connection, transaction);
        command.Parameters.AddWithValue("@Kind", kind);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name);
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            if (kind == "PriceList")
            {
                foreach (var item in requestedItems)
                {
                    await using var itemCommand = Procedure("dbo.PriceSegmentItemSave", connection, transaction);
                    itemCommand.Parameters.AddWithValue("@Kind", kind);
                    itemCommand.Parameters.AddWithValue("@Id", id);
                    itemCommand.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
                    itemCommand.Parameters.AddWithValue("@ProductId", item.ProductId);
                    itemCommand.Parameters.AddWithValue("@MinimumQuantity", item.MinimumQuantity);
                    itemCommand.Parameters.AddWithValue("@Amount", item.Amount);
                    itemCommand.Parameters.AddWithValue("@ValidFrom", item.ValidFrom ?? DateTimeOffset.UtcNow);
                    itemCommand.Parameters.AddWithValue("@ValidUntil", (object?)item.ValidUntil ?? DBNull.Value);
                    itemCommand.Parameters.AddWithValue("@Excluded", false);
                    await itemCommand.ExecuteNonQueryAsync(ct);
                }
            }
            else
            {
                await using var channelCommand = Procedure("dbo.PriceChannelSettingsSave", connection, transaction);
                channelCommand.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
                channelCommand.Parameters.AddWithValue("@PriceChannelId", id);
                channelCommand.Parameters.AddWithValue("@RuleKind", PriceChannelRuleKind.PercentageVariation.ToString());
                channelCommand.Parameters.AddWithValue("@NumericValue", request.PriceVariationPercent ?? 0);
                channelCommand.Parameters.AddWithValue("@ValidFrom", DateTimeOffset.UtcNow);
                await channelCommand.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            return Results.Problem("Ya existe una condición igual para ese producto.", statusCode: 409);
        }
        return Results.Ok(new PriceSegmentSummary(id, kind, code, name, true,
            DateTimeOffset.UtcNow, requestedItems.Select(item => item.ProductId).Distinct().Count(), 0,
            kind == "PriceChannel" ? request.PriceVariationPercent ?? 0 : null));
    }

    private static async Task<IResult> ItemsAsync(
        HttpContext context, string kind, Guid id, SqlServerConnectionFactory connections,
        CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.read")) return Results.Forbid();
        kind = NormalizeKind(kind);
        if (kind != "PriceList")
            return Results.Problem("Los productos se configuran únicamente en listas de precios.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceSegmentItemsList", connection);
        command.Parameters.AddWithValue("@Kind", kind);
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
        if (kind != "PriceList")
            return Results.Problem("Los productos se configuran únicamente en listas de precios.", statusCode: 400);
        if (request.Amount <= 0 || request.MinimumQuantity <= 0)
            return Results.Problem("Precio y cantidad mínima deben ser mayores que cero.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = Procedure("dbo.PriceSegmentItemSave", connection, transaction);
        command.Parameters.AddWithValue("@Kind", kind);
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

    private static async Task<IResult> SaveChannelSettingsAsync(
        HttpContext context, Guid id, SavePriceChannelSettingsRequest request,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        if (request.PriceVariationPercent is < -100 or > 1000)
            return Results.Problem("La variación debe estar entre -100 % y 1.000 %.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = Procedure("dbo.PriceChannelSettingsSave", connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@PriceChannelId", id);
        command.Parameters.AddWithValue("@RuleKind", PriceChannelRuleKind.PercentageVariation.ToString());
        command.Parameters.AddWithValue("@NumericValue", request.PriceVariationPercent);
        command.Parameters.AddWithValue("@ValidFrom", DateTimeOffset.UtcNow);
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (SqlException exception) when (exception.Number == 51004)
        {
            await transaction.RollbackAsync(ct);
            return Results.NotFound();
        }
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteItemAsync(
        HttpContext context, string kind, Guid id, Guid productId, decimal? minimumQuantity,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        kind = NormalizeKind(kind);
        if (kind != "PriceList")
            return Results.Problem("Los productos se configuran únicamente en listas de precios.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceSegmentItemDelete", connection);
        command.Parameters.AddWithValue("@Kind", kind);
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

    private static SqlCommand Procedure(string name, SqlConnection connection, SqlTransaction? transaction = null) =>
        new(name, connection, transaction) { CommandType = System.Data.CommandType.StoredProcedure };
}

public sealed record PriceSegmentSummary(Guid Id, string Kind, string Code, string Name, bool IsActive, DateTimeOffset CreatedAt, int ProductCount, int CustomerCount, decimal? PriceVariationPercent);
public sealed record PriceSegmentItem(Guid ProductId, string ProductCode, string ProductName, decimal Amount, string CurrencyCode, decimal MinimumQuantity, DateTimeOffset ValidFrom, DateTimeOffset? ValidUntil, bool Excluded);
public sealed record SavePriceSegmentRequest(string Kind, string Name,
    decimal? PriceVariationPercent, IReadOnlyList<CreatePriceSegmentItemRequest>? Items);
public sealed record CreatePriceSegmentItemRequest(Guid ProductId, decimal Amount,
    decimal MinimumQuantity, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil);
public sealed record SavePriceSegmentItemRequest(decimal Amount, decimal MinimumQuantity, DateTimeOffset? ValidFrom, DateTimeOffset? ValidUntil, bool Excluded);
public sealed record SavePriceChannelSettingsRequest(decimal PriceVariationPercent);
public enum PriceChannelRuleKind { PercentageVariation }
