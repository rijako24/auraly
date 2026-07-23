using System.Text.Json;
using MimosBabySpa.Domain.Entities;

namespace MimosBabySpa.Infrastructure.Commerce;

internal sealed class XionSettings
{
    public string BaseUrl { get; init; } = "http://api.andinasantander.com:9091/";
    public int RequestTimeoutSeconds { get; init; } = 120;
    public string Currency { get; init; } = "COP";
    public int SucursalId { get; init; }
    public int VendedorId { get; init; }
    public int EquipoId { get; init; }
    public int BodegaId { get; init; }
    public int EmpresaId { get; init; }
    public int CentroDeCostoId { get; init; }
    public int UsuarioId { get; init; }
    public int RutaId { get; init; }
    public bool ValidateStockOnCreate { get; init; } = true;
    public int OrderHistoryDays { get; init; } = 365;
    public XionEndpointSettings Endpoints { get; init; } = new();

    public static XionSettings From(IntegrationConnection connection)
    {
        var root = Parse(connection.SettingsJson);
        var endpoints = Read(root, "endpoints");
        var vendedorId = GetRequiredPositiveInt(root, "vendedorId");
        return new XionSettings
        {
            BaseUrl = GetString(root, "baseUrl", "http://api.andinasantander.com:9091/"),
            RequestTimeoutSeconds = Math.Clamp(GetInt(root, "requestTimeoutSeconds", 120), 1, 600),
            Currency = GetString(root, "currency", "COP"),
            SucursalId = GetRequiredPositiveInt(root, "sucursalId"),
            VendedorId = vendedorId,
            EquipoId = GetRequiredPositiveInt(root, "equipoId"),
            BodegaId = GetRequiredPositiveInt(root, "bodegaId"),
            EmpresaId = GetRequiredPositiveInt(root, "empresaId"),
            CentroDeCostoId = GetRequiredPositiveInt(root, "centroDeCostoId"),
            UsuarioId = GetInt(root, "usuarioId", vendedorId),
            RutaId = Math.Max(GetInt(root, "rutaId", 0), 0),
            ValidateStockOnCreate = GetBool(root, "validateStockOnCreate", true),
            OrderHistoryDays = Math.Clamp(GetInt(root, "orderHistoryDays", 365), 1, 3650),
            Endpoints = new XionEndpointSettings
            {
                CustomerSync = GetString(endpoints, "customerSync", "WebApi/Vendedores/Sync/Clientes/{vendedorId}/{sucursalId}"),
                ProductSearch = GetString(endpoints, "productSearch", "WebApi/Vendedores/Consulta/ProductosABuscar/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}/{clienteId}"),
                ProductSearchWithoutCustomer = GetString(endpoints, "productSearchWithoutCustomer", "WebApi/Vendedores/Consulta/ProductosABuscarSinCliente/{sucursalId}/{vendedorId}/{criterio}/{busqueda}/{bodegaId}/{equipoId}"),
                ProductDetail = GetString(endpoints, "productDetail", "WebApi/Vendedores/Consulta/InfoProducto/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}/{clienteId}"),
                ProductDetailWithoutCustomer = GetString(endpoints, "productDetailWithoutCustomer", "WebApi/Vendedores/Consulta/InfoProductoSinCliente/{productoId}/{sucursalId}/{vendedorId}/{bodegaId}/{equipoId}"),
                ProductSync = GetString(endpoints, "productSync", "WebApi/Vendedores/Sync/Productos/{vendedorId}/{sucursalId}"),
                NextOrderNumber = GetString(endpoints, "nextOrderNumber", "WebApi/Vendedores/Consulta/Pedido/SiguienteConsecutivo/{equipoId}"),
                CreateOrder = GetString(endpoints, "createOrder", "WebApi/Vendedores/Nuevo/Pedido/{validarExistencia}"),
                OrderHistory = GetString(endpoints, "orderHistory", "WebApi/Vendedores/Consulta/Pedidos/{vendedorId}/{fechaInicial}/{fechaFin}/{clienteId}/{rutaId}/{criterio}"),
                VerifyOrder = GetString(endpoints, "verifyOrder", "WebApi/Vendedores/Consulta/VerificarPedido/{pedidoId}")
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
