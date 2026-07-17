using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimosBabySpa.Application.Commerce;

namespace MimosBabySpa.Infrastructure.Commerce;

internal sealed class MantisProductSearchResponse
{
    public string? ErrorKey { get; init; }
    public List<MantisProductDto> SDTConArtCasalins { get; init; } = [];
    public MantisPaginationDto? SDTPaginadoCasalins { get; init; }
}

internal sealed class MantisPaginationDto
{
    public string? NextPage { get; init; }
    public int Page { get; init; }
    public int PagaSize { get; init; }
    public int TotalItems { get; init; }
    public int TotalPages { get; init; }
}

internal sealed class MantisProductDto
{
    public string? CategoriaProducto { get; init; }
    public string? ClaseProducto { get; init; }
    public string? CodigoProducto { get; init; }
    public string? DispProducto { get; init; }
    public string? ExiProducto { get; init; }
    public string? FamiliaProducto { get; init; }
    public string? MonedaProducto { get; init; }
    public string? NombreProducto { get; init; }
    public string? PrecioProducto { get; init; }
    public string? PresProducto { get; init; }
    public string? SubCategoriaProducto { get; init; }
    public string? TipoProducto { get; init; }
    public string? FechaCreacion { get; init; }
    public string? FechaModificacion { get; init; }
}

internal static class MantisProductMapper
{
    public static ProductReference? ToProductReference(MantisProductDto product, string defaultCurrency)
    {
        var code = Clean(product.CodigoProducto);
        var name = Clean(product.NombreProducto) ?? code;
        if (string.IsNullOrWhiteSpace(name))
            return null;

        return new ProductReference(
            null,
            code,
            code,
            name,
            Clean(product.PresProducto),
            Clean(product.CategoriaProducto),
            ParseDecimal(product.PrecioProducto) ?? 0m,
            Clean(product.MonedaProducto) ?? defaultCurrency,
            ParseDecimal(product.ExiProducto),
            RawPayloadJson: JsonSerializer.Serialize(product, CommerceJson.Options),
            FamilyName: Clean(product.FamiliaProducto),
            SubcategoryName: Clean(product.SubCategoriaProducto),
            ProductClassName: Clean(product.ClaseProducto))
        { IsActive = ParseBool(product.DispProducto) ?? true };
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var invariant)
            ? invariant
            : decimal.TryParse(value, NumberStyles.Number, CultureInfo.GetCultureInfo("es-CO"), out var localized)
                ? localized
                : null;
    }

    private static bool? ParseBool(string? value) =>
        bool.TryParse(value, out var parsed) ? parsed : null;
}
internal sealed class MantisCustomerSearchResponse
{
    public string? ErrorKey { get; init; }
    public List<MantisCustomerDto> SDTConsultarClientesCasalins { get; init; } = [];
    public MantisPaginationDto? SDTPaginadoCasalins { get; init; }
}

internal sealed class MantisCustomerDto
{
    public string? BarrioCliente { get; init; }
    public string? CelularCliente { get; init; }
    public string? CiudadCliente { get; init; }
    public string? DireccionCliente { get; init; }
    public string? LlaveCliente { get; init; }
    public string? LlaveNit { get; init; }
    public string? NombreCliente { get; init; }
    public string? RutaCliente { get; init; }
    public string? TelefonoClientes { get; init; }
    public string? ZonaCliente { get; init; }
}

internal sealed record MantisCustomerIdentity(
    string LlaveNit,
    string LlaveCliente,
    string? Name,
    string? CellPhone,
    string? Telephone);

internal sealed class FlexibleStringJsonConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => ReadNumber(ref reader),
            JsonTokenType.True => bool.TrueString,
            JsonTokenType.False => bool.FalseString,
            _ => throw new JsonException($"Cannot convert {reader.TokenType} to string.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    private static string ReadNumber(ref Utf8JsonReader reader)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetRawText();
    }
}
