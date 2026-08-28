using Auraly.Contracts.Pricing;
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
        group.MapGet("/{id:guid}/items", ItemsAsync);
        group.MapPut("/{id:guid}/items/{productId:guid}", SaveItemAsync);
        group.MapDelete("/{id:guid}/items/{productId:guid}", DeleteItemAsync);
        group.MapPut("/{id:guid}/settings", SaveChannelSettingsAsync);
        group.MapGet("/{id:guid}/exclusions", ExclusionsAsync);
        group.MapPost("/{id:guid}/exclusions", SaveExclusionAsync);
        group.MapDelete("/{id:guid}/exclusions/{exclusionId:guid}", DeleteExclusionAsync);
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
                reader.GetBoolean(3), reader.GetFieldValue<DateTimeOffset>(4),
                reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDecimal(8)));
        return Results.Ok(items);
    }

    private static async Task<IResult> SaveAsync(
        HttpContext context, SavePriceSegmentRequest request,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Results.Problem("El nombre es obligatorio.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        var id = Guid.NewGuid();
        var code = $"CNL-{id:N}".ToUpperInvariant()[..12];
        var requestedItems = request.Items ?? [];
        if (!TryNormalizeChannelStrategy(request.ChannelStrategy, out var strategy) ||
            !TryValidateChannelValue(strategy, request.ChannelValue, out var channelValue))
            return InvalidChannelSettings();
        if (strategy == PriceChannelStrategies.TieredProductPrice &&
            requestedItems.Any(item => item.ProductId == Guid.Empty || item.Amount <= 0 || item.MinimumQuantity <= 0))
            return Results.Problem("Cada producto necesita un precio y una cantidad mínima válidos.", statusCode: 400);
        await using var command = Procedure("dbo.PriceSegmentCreate", connection, transaction);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@Code", code);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Strategy", (object?)strategy ?? DBNull.Value);
        command.Parameters.AddWithValue("@Value", (object?)channelValue ?? DBNull.Value);
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            if (strategy == PriceChannelStrategies.TieredProductPrice)
            {
                foreach (var item in requestedItems)
                {
                    await using var itemCommand = Procedure("dbo.PriceSegmentItemSave", connection, transaction);
                    itemCommand.Parameters.AddWithValue("@Id", id);
                    itemCommand.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
                    itemCommand.Parameters.AddWithValue("@ProductId", item.ProductId);
                    itemCommand.Parameters.AddWithValue("@MinimumQuantity", item.MinimumQuantity);
                    itemCommand.Parameters.AddWithValue("@Amount", item.Amount);
                    await itemCommand.ExecuteNonQueryAsync(ct);
                }
            }
            await transaction.CommitAsync(ct);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            await transaction.RollbackAsync(ct);
            return Results.Problem("Ya existe una condición igual para ese producto.", statusCode: 409);
        }
        return Results.Ok(new PriceSegmentSummary(id, code, name, true,
            DateTimeOffset.UtcNow, requestedItems.Select(item => item.ProductId).Distinct().Count(), 0,
            strategy!, channelValue));
    }

    private static async Task<IResult> ItemsAsync(
        HttpContext context, Guid id, SqlServerConnectionFactory connections,
        CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.read")) return Results.Forbid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceSegmentItemsList", connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var items = new List<PriceSegmentItem>();
        while (await reader.ReadAsync(ct))
            items.Add(new(reader.GetGuid(0), reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.GetString(2), reader.GetDecimal(3), reader.GetString(4), reader.GetDecimal(5)));
        return Results.Ok(items);
    }

    private static async Task<IResult> SaveItemAsync(
        HttpContext context, Guid id, Guid productId,
        SavePriceSegmentItemRequest request, SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        if (request.Amount <= 0 || request.MinimumQuantity <= 0)
            return Results.Problem("Precio y cantidad mínima deben ser mayores que cero.", statusCode: 400);
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = Procedure("dbo.PriceSegmentItemSave", connection, transaction);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@MinimumQuantity", request.MinimumQuantity);
        command.Parameters.AddWithValue("@Amount", request.Amount);
        try { await command.ExecuteNonQueryAsync(ct); await transaction.CommitAsync(ct); }
        catch (SqlException exception) when (exception.Number == 51004)
        { await transaction.RollbackAsync(ct); return Results.NotFound(); }
        return Results.NoContent();
    }

    private static async Task<IResult> ExclusionsAsync(
        HttpContext context, Guid id, SqlServerConnectionFactory connections,
        CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.read")) return Results.Forbid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceChannelExclusionsList", connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            var items = new List<PriceChannelExclusion>();
            while (await reader.ReadAsync(ct))
                items.Add(new(
                    reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2),
                    reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            return Results.Ok(items);
        }
        catch (SqlException exception) when (exception.Number == 51004)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> SaveExclusionAsync(
        HttpContext context, Guid id, SavePriceChannelExclusionRequest request,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        var scopeType = request.ScopeType?.Trim() switch
        {
            "Category" => "Category",
            "Brand" => "Brand",
            "Product" => "Product",
            _ => null
        };
        if (scopeType is null || request.ScopeId == Guid.Empty)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["scope"] = ["Selecciona un alcance válido para la exclusión."]
            });
        var exclusionId = Guid.NewGuid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceChannelExclusionSave", connection);
        command.Parameters.AddWithValue("@ExclusionId", exclusionId);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@ScopeType", scopeType);
        command.Parameters.AddWithValue("@ScopeId", request.ScopeId);
        try
        {
            await command.ExecuteNonQueryAsync(ct);
            return Results.Created($"/api/commerce/v1/pricing/segments/{id:D}/exclusions/{exclusionId:D}",
                new { exclusionId });
        }
        catch (SqlException exception) when (exception.Number == 51004)
        {
            return Results.NotFound();
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            return Results.Conflict(new { message = "Ese alcance ya está excluido del canal." });
        }
    }

    private static async Task<IResult> DeleteExclusionAsync(
        HttpContext context, Guid id, Guid exclusionId,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceChannelExclusionDelete", connection);
        command.Parameters.AddWithValue("@ExclusionId", exclusionId);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        await command.ExecuteNonQueryAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SaveChannelSettingsAsync(
        HttpContext context, Guid id, SavePriceChannelSettingsRequest request,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["El nombre es obligatorio y admite máximo 120 caracteres."] });
        if (!TryNormalizeChannelStrategy(request.ChannelStrategy, out var strategy) ||
            !TryValidateChannelValue(strategy, request.ChannelValue, out var value))
            return InvalidChannelSettings();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await using var command = Procedure("dbo.PriceChannelSettingsUpdate", connection, transaction);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@Name", name);
        command.Parameters.AddWithValue("@Strategy", strategy);
        command.Parameters.AddWithValue("@Value", (object?)value ?? DBNull.Value);
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
        HttpContext context, Guid id, Guid productId, decimal? minimumQuantity,
        SqlServerConnectionFactory connections, CancellationToken ct)
    {
        var identity = context.User.ToPricingIdentity();
        if (!identity.Permissions.Contains("pricing.segments.manage")) return Results.Forbid();
        await using var connection = connections.Create();
        await connection.OpenAsync(ct);
        await using var command = Procedure("dbo.PriceSegmentItemDelete", connection);
        command.Parameters.AddWithValue("@Id", id);
        command.Parameters.AddWithValue("@ProductId", productId);
        command.Parameters.AddWithValue("@BusinessId", identity.BusinessId);
        command.Parameters.AddWithValue("@MinimumQuantity", minimumQuantity ?? 1m);
        await command.ExecuteNonQueryAsync(ct);
        return Results.NoContent();
    }

    private static bool TryNormalizeChannelStrategy(string? strategy, out string normalized)
    {
        normalized = strategy?.Trim() ?? string.Empty;
        return PriceChannelStrategies.IsSupported(normalized);
    }

    private static bool TryValidateChannelValue(
        string strategy,
        decimal? value,
        out decimal? normalized)
    {
        switch (strategy)
        {
            case PriceChannelStrategies.PercentageOverBasePrice when value is >= -100 and <= 1000:
            case PriceChannelStrategies.PercentageOverAverageCost when value is >= 0 and <= 1000:
            case PriceChannelStrategies.FixedMarginOverAverageCost when value is >= 0 and < 100:
            case PriceChannelStrategies.ProductMarginAdjustment when value is >= -99.999999m and <= 99.999999m:
                normalized = value;
                return true;
            case PriceChannelStrategies.TieredProductPrice:
            case PriceChannelStrategies.SellAtAverageCost:
                normalized = null;
                return true;
            default:
                normalized = null;
                return false;
        }
    }

    private static IResult InvalidChannelSettings() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["channelStrategy"] = ["Selecciona un modo y un valor válidos para el canal."]
        });

    private static SqlCommand Procedure(string name, SqlConnection connection, SqlTransaction? transaction = null) =>
        new(name, connection, transaction) { CommandType = System.Data.CommandType.StoredProcedure };
}

public sealed record PriceSegmentSummary(Guid Id, string Code, string Name, bool IsActive, DateTimeOffset CreatedAt, int ProductCount, int CustomerCount, string Strategy, decimal? Value);
public sealed record PriceSegmentItem(Guid ProductId, string ProductCode, string ProductName, decimal Amount, string CurrencyCode, decimal MinimumQuantity);
public sealed record SavePriceSegmentRequest(string Name,
    string ChannelStrategy, decimal? ChannelValue, IReadOnlyList<CreatePriceSegmentItemRequest>? Items);
public sealed record CreatePriceSegmentItemRequest(Guid ProductId, decimal Amount, decimal MinimumQuantity);
public sealed record SavePriceSegmentItemRequest(decimal Amount, decimal MinimumQuantity);
public sealed record SavePriceChannelSettingsRequest(string Name, string ChannelStrategy, decimal? ChannelValue);
public sealed record PriceChannelExclusion(Guid ExclusionId, string ScopeType, Guid ScopeId,
    string ScopeName, int? CategoryDepth, string? ProductCode);
public sealed record SavePriceChannelExclusionRequest(string ScopeType, Guid ScopeId);
