using System.Text.Json;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Infrastructure.Commerce;

internal sealed class MantisSettings
{
    public string BaseUrl { get; init; } = "http://93.189.95.109:8080/MantisFiccCasalinsPruWeb/rest/";
    public string AuthorizationToken { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; } = 30;
    public string Currency { get; init; } = "COP";
    public MantisGenericCustomerSettings GenericCustomer { get; init; } = new();
    public MantisCatalogSettings Catalog { get; init; } = new();
    public MantisCustomerSettings Customer { get; init; } = new();
    public MantisOrderSettings Order { get; init; } = new();

    public static MantisSettings From(IntegrationConnection connection)
    {
        var settings = Parse(connection.SettingsJson);
        var secrets = Parse(connection.SecretsJson);
        var catalog = Read(settings, "catalog");
        var order = Read(settings, "order");
        var customer = Read(settings, "customer");
        var genericCustomer = Read(settings, "genericCustomer");
        var configuredWarehouse = GetString(
            settings,
            "warehouse",
            GetString(catalog, "warehouse", GetString(order, "warehouse", string.Empty)));

        return new MantisSettings
        {
            BaseUrl = GetString(settings, "baseUrl", "http://93.189.95.109:8080/MantisFiccCasalinsPruWeb/rest/"),
            AuthorizationToken = GetString(secrets, "authorizationToken", GetString(settings, "authorizationToken")),
            RequestTimeoutSeconds = GetInt(settings, "requestTimeoutSeconds", 30),
            Currency = GetString(settings, "currency", "COP"),
            GenericCustomer = new MantisGenericCustomerSettings
            {
                LlaveNit = GetString(genericCustomer, "llaveNit", string.Empty),
                LlaveCliente = GetString(genericCustomer, "llaveCliente", string.Empty)
            },
            Customer = new MantisCustomerSettings
            {
                SearchEndpoint = GetString(customer, "searchEndpoint", "pwsConsultarClientesCasalins"),
                CountryCode = GetString(customer, "countryCode", "57"),
                NationalPhoneLength = Math.Clamp(GetInt(customer, "nationalPhoneLength", 10), 7, 15),
                LookupPageSize = 1
            },
            Catalog = new MantisCatalogSettings
            {
                SearchEndpoint = GetString(catalog, "searchEndpoint", "pwsConsultarArticuloCasalins"),
                DefaultPageSize = GetInt(catalog, "defaultPageSize", 5),
                MaxPageSize = GetInt(catalog, "maxPageSize", 20),
                Warehouse = configuredWarehouse
            },
            Order = new MantisOrderSettings
            {
                CreateEndpoint = GetString(order, "createEndpoint", "pwsCrearPedidoCasalins"),
                QueryEndpoint = GetString(order, "queryEndpoint", "pwsConsultarPedidoCasalins"),
                Warehouse = configuredWarehouse
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

        return value.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }


    private static string GetString(Dictionary<string, JsonElement> values, string key, string fallback = "") =>
        values.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString()
            : fallback;

    private static int GetInt(Dictionary<string, JsonElement> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && value.TryGetInt32(out var number) ? number : fallback;
}

internal sealed class MantisCatalogSettings
{
    public string SearchEndpoint { get; init; } = "pwsConsultarArticuloCasalins";
    public int DefaultPageSize { get; init; } = 5;
    public int MaxPageSize { get; init; } = 20;
    public string Warehouse { get; init; } = string.Empty;
}
internal sealed class MantisGenericCustomerSettings
{
    public string LlaveNit { get; init; } = string.Empty;
    public string LlaveCliente { get; init; } = string.Empty;
}


internal sealed class MantisCustomerSettings
{
    public string SearchEndpoint { get; init; } = "pwsConsultarClientesCasalins";
    public string CountryCode { get; init; } = "57";
    public int NationalPhoneLength { get; init; } = 10;
    public int LookupPageSize { get; init; } = 1;
}

internal sealed class MantisOrderSettings
{
    public string CreateEndpoint { get; init; } = "pwsCrearPedidoCasalins";
    public string QueryEndpoint { get; init; } = "pwsConsultarPedidoCasalins";
    public string Warehouse { get; init; } = string.Empty;
}