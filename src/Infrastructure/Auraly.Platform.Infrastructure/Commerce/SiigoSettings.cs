using System.Text.Json;
using Auraly.Platform.Domain.Entities;

namespace Auraly.Platform.Infrastructure.Commerce;

internal sealed class SiigoSettings
{
    public string BaseUrl { get; init; } = "https://api.siigo.com/";
    public string PartnerId { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 30;
    public string Username { get; init; } = string.Empty;
    public string AccessKey { get; init; } = string.Empty;
    public SiigoCatalogSettings Catalog { get; init; } = new();
    public SiigoOrderSettings Order { get; init; } = new();

    public static SiigoSettings From(IntegrationConnection connection)
    {
        var settings = Parse(connection.SettingsJson);
        var secrets = Parse(connection.SecretsJson);
        var order = Read(settings, "order");
        var catalog = Read(settings, "catalog");
        var defaultCustomer = Read(order, "defaultCustomer");

        return new SiigoSettings
        {
            BaseUrl = GetString(settings, "baseUrl", "https://api.siigo.com/"),
            PartnerId = GetString(settings, "partnerId"),
            RequestTimeoutSeconds = GetInt(settings, "requestTimeoutSeconds", 30),
            Username = GetString(secrets, "username"),
            AccessKey = GetString(secrets, "accessKey"),
            Catalog = new SiigoCatalogSettings
            {
                CacheProducts = GetBool(catalog, "cacheProducts", true),
                DefaultPageSize = GetInt(catalog, "defaultPageSize", 25),
                PriceListPosition = GetInt(catalog, "priceListPosition", 1)
            },
            Order = new SiigoOrderSettings
            {
                DocumentId = GetInt(order, "documentId", 0),
                PaymentTypeId = GetInt(order, "paymentTypeId", 0),
                SellerId = GetNullableInt(order, "sellerId"),
                CostCenterId = GetNullableInt(order, "costCenterId"),
                StampSend = GetBool(order, "stampSend", false),
                MailSend = GetBool(order, "mailSend", false),
                DefaultCurrencyCode = GetString(order, "defaultCurrencyCode", "COP"),
                DefaultTaxIds = GetIntArray(order, "defaultTaxIds"),
                DefaultCustomer = new SiigoDefaultCustomer
                {
                    PersonType = GetString(defaultCustomer, "personType", "Person"),
                    IdType = GetString(defaultCustomer, "idType", "13"),
                    Identification = GetString(defaultCustomer, "identification", "222222222222"),
                    BranchOffice = GetInt(defaultCustomer, "branchOffice", 0),
                    City = Read(defaultCustomer, "city")
                }
            }
        };
    }

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonElement> Read(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        return value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private static string GetString(Dictionary<string, JsonElement> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString()
            : fallback;

    private static int GetInt(Dictionary<string, JsonElement> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && value.TryGetInt32(out var number) ? number : fallback;

    private static int? GetNullableInt(Dictionary<string, JsonElement> values, string key) =>
        values.TryGetValue(key, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static bool GetBool(Dictionary<string, JsonElement> values, string key, bool fallback) =>
        values.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.True || (value.ValueKind != JsonValueKind.False && bool.TryParse(value.ToString(), out var parsed) && parsed)
            : fallback;

    private static IReadOnlyList<int> GetIntArray(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        return value.EnumerateArray()
            .Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
            .Select(e => e.GetInt32())
            .ToList();
    }
}

internal sealed class SiigoCatalogSettings
{
    public bool CacheProducts { get; init; } = true;
    public int DefaultPageSize { get; init; } = 25;
    public int PriceListPosition { get; init; } = 1;
}

internal sealed class SiigoOrderSettings
{
    public int DocumentId { get; init; }
    public int PaymentTypeId { get; init; }
    public int? SellerId { get; init; }
    public int? CostCenterId { get; init; }
    public bool StampSend { get; init; }
    public bool MailSend { get; init; }
    public string DefaultCurrencyCode { get; init; } = "COP";
    public IReadOnlyList<int> DefaultTaxIds { get; init; } = [];
    public SiigoDefaultCustomer DefaultCustomer { get; init; } = new();
}

internal sealed class SiigoDefaultCustomer
{
    public string PersonType { get; init; } = "Person";
    public string IdType { get; init; } = "13";
    public string Identification { get; init; } = "222222222222";
    public int BranchOffice { get; init; }
    public Dictionary<string, JsonElement> City { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
