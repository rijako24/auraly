using System.Text.Json;
using System.Text.Json.Serialization;

namespace MimosBabySpa.Application.LLM.Extraction;

/// <summary>
/// JsonConverter que permite valores no-string en propiedades tipo string.
/// El LLM a veces devuelve boolean o number en campos que esperan string
/// (ej. value: true en extracted_fields). Convierte cualquier tipo a string
/// para evitar JsonException en deserialización.
/// </summary>
public sealed class FlexibleStringJsonConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String  => reader.GetString() ?? string.Empty,
            JsonTokenType.True    => "true",
            JsonTokenType.False   => "false",
            JsonTokenType.Number  => reader.TryGetInt64(out var i) ? i.ToString() : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.Null    => string.Empty,
            _                     => reader.GetString() ?? string.Empty
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);

    /// <summary>
    /// Convierte un JsonElement a string tolerando cualquier ValueKind.
    /// Usado por TryParsePartialResponse cuando el JSON viene manualmente.
    /// </summary>
    public static string FromJsonElement(JsonElement el)
    {
        return el.ValueKind switch
        {
            JsonValueKind.String  => el.GetString() ?? string.Empty,
            JsonValueKind.True    => "true",
            JsonValueKind.False   => "false",
            JsonValueKind.Number  => el.TryGetInt64(out var i) ? i.ToString() : el.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.Null    => string.Empty,
            _                     => el.GetRawText()
        };
    }
}
