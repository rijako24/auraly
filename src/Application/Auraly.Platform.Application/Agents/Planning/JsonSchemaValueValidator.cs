using System.Text.Json;

namespace Auraly.Platform.Application.Agents.Planning;

public static class JsonSchemaValueValidator
{
    public static bool IsValid(JsonElement value, JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return false;

        if (schema.TryGetProperty("anyOf", out var anyOf) && anyOf.ValueKind == JsonValueKind.Array)
            return anyOf.EnumerateArray().Any(candidate => IsValid(value, candidate));

        if (schema.TryGetProperty("enum", out var allowed) && allowed.ValueKind == JsonValueKind.Array
            && !allowed.EnumerateArray().Any(candidate => candidate.ValueKind == value.ValueKind && candidate.GetRawText() == value.GetRawText()))
            return false;

        if (schema.TryGetProperty("type", out var type) && !MatchesType(value, type))
            return false;

        if (value.ValueKind == JsonValueKind.Object)
            return ValidateObject(value, schema);
        if (value.ValueKind == JsonValueKind.Array)
            return ValidateArray(value, schema);

        return true;
    }

    private static bool ValidateObject(JsonElement value, JsonElement schema)
    {
        var properties = schema.TryGetProperty("properties", out var configuredProperties)
            && configuredProperties.ValueKind == JsonValueKind.Object
                ? configuredProperties
                : default;

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var requiredProperty in required.EnumerateArray())
            {
                if (requiredProperty.GetString() is { } name && !value.TryGetProperty(name, out _))
                    return false;
            }
        }

        foreach (var property in value.EnumerateObject())
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var propertySchema))
            {
                if (!IsValid(property.Value, propertySchema))
                    return false;
                continue;
            }

            if (schema.TryGetProperty("additionalProperties", out var additional)
                && additional.ValueKind == JsonValueKind.False)
                return false;
        }

        return true;
    }

    private static bool ValidateArray(JsonElement value, JsonElement schema)
    {
        if (!schema.TryGetProperty("items", out var itemSchema))
            return true;
        return value.EnumerateArray().All(item => IsValid(item, itemSchema));
    }

    private static bool MatchesType(JsonElement value, JsonElement type)
    {
        if (type.ValueKind == JsonValueKind.Array)
            return type.EnumerateArray().Any(candidate => candidate.ValueKind == JsonValueKind.String && MatchesType(value, candidate.GetString()));
        return type.ValueKind == JsonValueKind.String && MatchesType(value, type.GetString());
    }

    private static bool MatchesType(JsonElement value, string? type) => type switch
    {
        "string" => value.ValueKind == JsonValueKind.String,
        "number" => value.ValueKind == JsonValueKind.Number,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };
}
