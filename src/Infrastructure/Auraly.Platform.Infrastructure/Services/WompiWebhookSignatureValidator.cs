using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Infrastructure.Services;

/// <summary>
/// Valida la firma de webhooks de Wompi Colombia.
/// Algoritmo: SHA256(concat(properties_values) + timestamp + events_secret).
/// Ver: https://docs.wompi.co/docs/colombia/eventos/
/// </summary>
public class WompiWebhookSignatureValidator : IWompiWebhookSignatureValidator
{
    public bool Validate(JsonElement root, string eventsSecret)
    {
        if (string.IsNullOrWhiteSpace(eventsSecret))
            return true;

        if (!root.TryGetProperty("signature", out var signature) ||
            !root.TryGetProperty("timestamp", out var timestampEl) ||
            !root.TryGetProperty("data", out var data))
        {
            return false;
        }

        if (!signature.TryGetProperty("properties", out var properties) ||
            !signature.TryGetProperty("checksum", out var checksumEl))
        {
            return false;
        }

        var expectedChecksum = checksumEl.GetString();
        if (string.IsNullOrWhiteSpace(expectedChecksum))
            return false;

        var timestamp = timestampEl.ValueKind switch
        {
            JsonValueKind.Number => timestampEl.GetInt64().ToString(),
            JsonValueKind.String => timestampEl.GetString() ?? "",
            _ => ""
        };

        var sb = new StringBuilder();
        foreach (var prop in properties.EnumerateArray())
        {
            var path = prop.GetString();
            if (string.IsNullOrEmpty(path))
                continue;

            var value = GetValueByPath(data, path);
            sb.Append(value);
        }
        sb.Append(timestamp);
        sb.Append(eventsSecret);

        var computedHash = ComputeSha256Hex(sb.ToString());
        return string.Equals(computedHash, expectedChecksum, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetValueByPath(JsonElement data, string path)
    {
        var parts = path.Split('.');
        JsonElement current = data;

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (string.IsNullOrEmpty(part))
                return "";

            if (!current.TryGetProperty(part, out current))
                return "";
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString() ?? "",
            JsonValueKind.Number => current.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => "",
            _ => current.GetRawText()
        };
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return string.Concat(hash.Select(b => b.ToString("x2")));
    }
}
