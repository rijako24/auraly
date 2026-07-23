using System.Text.Json;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Infrastructure.Commerce;

internal sealed class XionSettings
{
    public string BaseUrl { get; init; } = string.Empty;
    public int RequestTimeoutSeconds { get; init; }
    public string Currency { get; init; } = string.Empty;
    public int SucursalId { get; init; }
    public int VendedorId { get; init; }
    public int EquipoId { get; init; }
    public int BodegaId { get; init; }
    public int EmpresaId { get; init; }
    public int CentroDeCostoId { get; init; }
    public int UsuarioId { get; init; }
    public int RutaId { get; init; }
    public bool ValidateStockOnCreate { get; init; }
    public int OrderHistoryDays { get; init; }
    public string CatalogBrowseSearchValue { get; init; } = string.Empty;
    public XionEndpointSettings Endpoints { get; init; } = new();

    public static XionSettings From(IntegrationConnection connection)
    {
        var root = Parse(connection.SettingsJson);
        var endpoints = Read(root, "endpoints");
        var vendedorId = GetRequiredPositiveInt(root, "vendedorId");
        return new XionSettings
        {
            BaseUrl = GetRequiredString(root, "baseUrl"),
            RequestTimeoutSeconds = Math.Clamp(GetRequiredPositiveInt(root, "requestTimeoutSeconds"), 1, 600),
            Currency = GetRequiredString(root, "currency"),
            SucursalId = GetRequiredPositiveInt(root, "sucursalId"),
            VendedorId = vendedorId,
            EquipoId = GetRequiredPositiveInt(root, "equipoId"),
            BodegaId = GetRequiredPositiveInt(root, "bodegaId"),
            EmpresaId = GetRequiredPositiveInt(root, "empresaId"),
            CentroDeCostoId = GetRequiredPositiveInt(root, "centroDeCostoId"),
            UsuarioId = GetRequiredPositiveInt(root, "usuarioId"),
            RutaId = GetRequiredNonNegativeInt(root, "rutaId"),
            ValidateStockOnCreate = GetRequiredBool(root, "validateStockOnCreate"),
            OrderHistoryDays = Math.Clamp(GetRequiredPositiveInt(root, "orderHistoryDays"), 1, 3650),
            CatalogBrowseSearchValue = GetRequiredString(root, "catalogBrowseSearchValue"),
            Endpoints = new XionEndpointSettings
            {
                CustomerSync = GetRequiredString(endpoints, "customerSync"),
                ProductSearch = GetRequiredString(endpoints, "productSearch"),
                ProductSearchWithoutCustomer = GetRequiredString(endpoints, "productSearchWithoutCustomer"),
                ProductDetail = GetRequiredString(endpoints, "productDetail"),
                ProductDetailWithoutCustomer = GetRequiredString(endpoints, "productDetailWithoutCustomer"),
                ProductSync = GetRequiredString(endpoints, "productSync"),
                NextOrderNumber = GetRequiredString(endpoints, "nextOrderNumber"),
                CreateOrder = GetRequiredString(endpoints, "createOrder"),
                OrderHistory = GetRequiredString(endpoints, "orderHistory"),
                VerifyOrder = GetRequiredString(endpoints, "verifyOrder")
            }
        };
    }

    private static Dictionary<string, JsonElement> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonElement> Read(Dictionary<string, JsonElement> values, string key) =>
        values.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.Object
            ? value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    private static string GetString(Dictionary<string, JsonElement> values, string key, string fallback) =>
        values.TryGetValue(key, out var value)
            ? value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : value.ToString()
            : fallback;

    private static int GetInt(Dictionary<string, JsonElement> values, string key, int fallback) =>
        values.TryGetValue(key, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static bool GetBool(Dictionary<string, JsonElement> values, string key, bool fallback) =>
        values.TryGetValue(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : fallback;

    private static string GetRequiredString(Dictionary<string, JsonElement> values, string key)
    {
        var value = GetString(values, key, string.Empty).Trim();
        return value.Length > 0 ? value : throw new InvalidOperationException($"Xion setting '{key}' is required.");
    }

    private static int GetRequiredNonNegativeInt(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || !value.TryGetInt32(out var parsed) || parsed < 0)
            throw new InvalidOperationException($"Xion setting '{key}' must be zero or greater.");
        return parsed;
    }

    private static bool GetRequiredBool(Dictionary<string, JsonElement> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"Xion setting '{key}' is required.");
        return value.GetBoolean();
    }
    private static int GetRequiredPositiveInt(Dictionary<string, JsonElement> values, string key)
    {
        var value = GetInt(values, key, 0);
        return value > 0 ? value : throw new InvalidOperationException($"Xion setting '{key}' must be greater than zero.");
    }
}

internal sealed class XionEndpointSettings
{
    public string CustomerSync { get; init; } = string.Empty;
    public string ProductSearch { get; init; } = string.Empty;
    public string ProductSearchWithoutCustomer { get; init; } = string.Empty;
    public string ProductDetail { get; init; } = string.Empty;
    public string ProductDetailWithoutCustomer { get; init; } = string.Empty;
    public string ProductSync { get; init; } = string.Empty;
    public string NextOrderNumber { get; init; } = string.Empty;
    public string CreateOrder { get; init; } = string.Empty;
    public string OrderHistory { get; init; } = string.Empty;
    public string VerifyOrder { get; init; } = string.Empty;
}
