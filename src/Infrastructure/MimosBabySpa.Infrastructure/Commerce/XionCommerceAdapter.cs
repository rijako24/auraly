using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Infrastructure.Commerce;

public sealed class XionCommerceAdapter :
    ICommerceAdapter,
    IAuthoritativeCommercePricingAdapter,
    ICommerceCustomerLookup,
    ICommerceCustomerIdentitySource,
    ICommerceProductIdentitySource,
    ICommerceOrderHistorySource
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IUnitOfWork _unitOfWork;

    public XionCommerceAdapter(HttpClient httpClient, IUnitOfWork unitOfWork)
    {
        _httpClient = httpClient;
        _unitOfWork = unitOfWork;
    }

    public CommerceProvider Provider => CommerceProvider.Xion;

    public async Task<CommerceCustomerReference?> FindCustomerAsync(
        CommerceAdapterContext context,
        CancellationToken ct = default)
    {
        var connection = RequireConnection(context);
        var settings = XionSettings.From(connection);
        var phone = XionMapper.NormalizePhone(context.CustomerPhone);
        if (phone is null)
            return null;

        var local = await _unitOfWork.ExternalCommerceCustomers.FindActiveByPhoneAsync(
            context.BusinessId, connection.IntegrationConnectionId, phone, ct);
        if (local.Count == 1)
            return ToCustomerReference(local[0]);
        if (local.Count > 1)
            return null;

        var matches = (await GetCustomersAsync(settings, ct))
            .Where(customer => XionMapper.NormalizePhone(customer.Telefono) == phone)
            .Where(customer => customer.ClienteId is > 0)
            .GroupBy(customer => customer.ClienteId)
            .Select(group => group.First())
            .ToList();
        if (matches.Count != 1)
            return null;

        var match = matches[0];
        var customerId = match.ClienteId!.Value.ToString(CultureInfo.InvariantCulture);
        var accountId = Clean(match.NoIdentificacion) ?? customerId;
        var existing = await _unitOfWork.ExternalCommerceCustomers.GetByExternalKeysAsync(
            context.BusinessId, connection.IntegrationConnectionId, accountId, customerId, ct);
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            existing = new ExternalCommerceCustomer
            {
                ExternalCommerceCustomerId = Guid.NewGuid(),
                BusinessId = context.BusinessId,
                IntegrationConnectionId = connection.IntegrationConnectionId,
                ExternalAccountId = accountId,
                ExternalCustomerId = customerId,
                Name = Clean(match.NombreCompleto),
                PhoneNormalized = phone,
                Phone = Clean(match.Telefono),
                IsActive = true,
                LastSyncedAt = now,
                CreatedAt = now
            };
            await _unitOfWork.ExternalCommerceCustomers.CreateAsync(existing, ct);
        }
        else
        {
            existing.Name = Clean(match.NombreCompleto);
            existing.PhoneNormalized = phone;
            existing.Phone = Clean(match.Telefono);
            existing.IsActive = true;
            existing.LastSyncedAt = now;
            existing.UpdatedAt = now;
            await _unitOfWork.ExternalCommerceCustomers.UpdateAsync(existing, ct);
        }
        await _unitOfWork.SaveChangesAsync(ct);
        return ToCustomerReference(existing);
    }

    public async Task<ProductSearchResult> SearchProductsAsync(
        ProductSearchRequest request,
        CommerceAdapterContext context,
        CancellationToken ct = default)
    {
        var settings = XionSettings.From(RequireConnection(context));
        var query = Clean(request.Query);
        if (query is null)
            return new ProductSearchResult([], "xion", false, ProductSearchAppliedFilters.From(request));

        var customerId = GetCustomerId(context.Customer);
        var template = customerId.HasValue
            ? settings.Endpoints.ProductSearch
            : settings.Endpoints.ProductSearchWithoutCustomer;
        var summaries = await GetJsonAsync<List<XionProductSummaryDto>>(
            settings, Expand(template, settings, query, customerId), "product search", ct) ?? [];
        var limit = Math.Clamp(request.Limit, 1, 50);
        var products = new List<ProductReference>();
        foreach (var summary in summaries.Take(limit))
        {
            var product = await GetProductByIdAsync(summary.IdProducto, customerId, settings, ct);
            var mapped = product is null ? null : XionMapper.ToProductReference(product, settings.Currency);
            if (mapped is not null)
                products.Add(await AttachExistingSnapshotIdAsync(context, mapped, ct));
        }
        return new ProductSearchResult(
            products, "xion-live", summaries.Count > limit, ProductSearchAppliedFilters.From(request));
    }

    public async Task<ProductReference?> GetProductAsync(
        AddOrderItemRequest request,
        CommerceAdapterContext context,
        CancellationToken ct = default)
    {
        var settings = XionSettings.From(RequireConnection(context));
        if (TryGetProductId(request.ExternalProductId, request.Sku, out var productId))
        {
            var product = await GetProductByIdAsync(productId, GetCustomerId(context.Customer), settings, ct);
            var mapped = product is null ? null : XionMapper.ToProductReference(product, settings.Currency);
            return mapped is null ? null : await AttachExistingSnapshotIdAsync(context, mapped, ct);
        }
        var query = Clean(request.Name);
        if (query is null)
            return null;
        var result = await SearchProductsAsync(new ProductSearchRequest(query, null, 10), context, ct);
        return result.Products.FirstOrDefault(product =>
                   string.Equals(product.Name, query, StringComparison.OrdinalIgnoreCase))
               ?? result.Products.FirstOrDefault();
    }

    public async Task<ProductIdentityPage> GetProductIdentityPageAsync(
        CommerceAdapterContext context,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var settings = XionSettings.From(RequireConnection(context));
        var products = await GetJsonAsync<List<XionProductDto>>(
            settings,
            Expand(settings.Endpoints.ProductSync, settings, null, null),
            "product synchronization",
            ct) ?? [];
        var size = Math.Clamp(pageSize, 1, 500);
        var skip = (Math.Max(page, 1) - 1) * size;
        var mapped = products.Skip(skip).Take(size)
            .Select(product => XionMapper.ToProductReference(product, settings.Currency))
            .OfType<ProductReference>()
            .ToList();
        return new ProductIdentityPage(mapped, products.Count > skip + size);
    }

    public async Task<ExternalCustomerIdentityPage> GetCustomerIdentityPageAsync(
        CommerceAdapterContext context,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var settings = XionSettings.From(RequireConnection(context));
        var customers = await GetCustomersAsync(settings, ct);
        var size = Math.Clamp(pageSize, 1, 500);
        var skip = (Math.Max(page, 1) - 1) * size;
        var mapped = customers.Skip(skip).Take(size)
            .Where(customer => customer.ClienteId is > 0)
            .Select(customer =>
            {
                var phone = XionMapper.NormalizePhone(customer.Telefono);
                if (phone is null)
                    return null;
                var customerId = customer.ClienteId!.Value.ToString(CultureInfo.InvariantCulture);
                return new ExternalCustomerIdentityReference(
                    Clean(customer.NoIdentificacion) ?? customerId,
                    customerId,
                    Clean(customer.NombreCompleto),
                    phone,
                    Clean(customer.Telefono));
            })
            .OfType<ExternalCustomerIdentityReference>()
            .ToList();
        return new ExternalCustomerIdentityPage(mapped, customers.Count > skip + size);
    }
    public async Task<IReadOnlyList<CommerceOrderHistoryRecord>> GetOrderHistoryAsync(
        CommerceAdapterContext context,
        CommerceOrderHistoryQuery query,
        CancellationToken ct = default)
    {
        var settings = XionSettings.From(RequireConnection(context));
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var from = query.From ?? today.AddDays(-settings.OrderHistoryDays);
        var to = query.To ?? today;
        var customerId = ParsePositiveInt(query.ExternalCustomerLookupId) ?? 0;
        var path = settings.Endpoints.OrderHistory
            .Replace("{vendedorId}", settings.VendedorId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{fechaInicial}", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{fechaFin}", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{clienteId}", customerId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{rutaId}", settings.RutaId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{criterio}", "0", StringComparison.Ordinal);
        var orders = await GetJsonAsync<List<XionOrderDto>>(
            settings, path, "order history", ct) ?? [];
        return orders
            .Where(order => string.IsNullOrWhiteSpace(query.ExternalOrderId)
                || string.Equals(order.PedidoId, query.ExternalOrderId, StringComparison.OrdinalIgnoreCase))
            .Select(XionMapper.ToOrderHistory)
            .OfType<CommerceOrderHistoryRecord>()
            .ToList();
    }

    public async Task<CreateExternalOrderResult> CreateOrderAsync(
        Order order,
        IReadOnlyList<OrderItem> items,
        CommerceAdapterContext context,
        CancellationToken ct = default)
    {
        var settings = XionSettings.From(RequireConnection(context));
        var customerId = GetCustomerId(context.Customer)
            ?? throw new InvalidOperationException("Xion requires a resolved customer before creating an order.");
        if (items.Count == 0)
            throw new InvalidOperationException("Xion order has no items.");

        var consecutivePath = settings.Endpoints.NextOrderNumber.Replace(
            "{equipoId}", settings.EquipoId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var consecutive = await GetJsonAsync<int>(settings, consecutivePath, "next order number", ct);
        if (consecutive <= 0)
            throw new InvalidOperationException("Xion did not return a valid order number.");

        var externalOrderId = $"PE{settings.EquipoId:000}{consecutive:00000000}";
        var details = items.Select((item, index) =>
            BuildOrderItem(item, index + 1, externalOrderId, settings)).ToList();
        var payload = new
        {
            PedidoId = externalOrderId,
            Consecutivo = consecutive,
            IdCliente = customerId,
            RutaId = settings.RutaId,
            VendedorId = settings.VendedorId,
            MotivoId = 0,
            Procesado = true,
            NombreCliente = order.CustomerNameSnapshot ?? context.Customer?.Name ?? string.Empty,
            Observacion = string.IsNullOrWhiteSpace(order.Notes)
                ? $"Creado por Talkio. Ref {order.OrderId:N}"
                : order.Notes.Trim(),
            TotalPedido = order.Total,
            IdEquipo = settings.EquipoId,
            EmpresaId = settings.EmpresaId,
            BodegaId = settings.BodegaId,
            CentroDeCostoId = settings.CentroDeCostoId,
            TotalUtilidad = details.Sum(detail => detail.Quantity * (detail.SalePrice - detail.CostPrice)),
            UsuarioId = settings.UsuarioId,
            TotalIva = order.TaxTotal,
            NoIdentificacionCliente = context.Customer?.ExternalAccountId ?? order.CustomerDocumentSnapshot,
            FechaPedido = DateTime.Now,
            PedidoDetalle = details.Select(detail => detail.Payload).ToList(),
            ParamsEmpresa = new
            {
                EmpresaId = settings.EmpresaId,
                CentroDeCostoId = settings.CentroDeCostoId,
                BodegaId = settings.BodegaId,
                VendedorId = settings.VendedorId,
                SucursalId = settings.SucursalId,
                EquipoId = settings.EquipoId
            },
            Visitado = (object?)null,
            TipoOrden = 1
        };
        var createPath = settings.Endpoints.CreateOrder.Replace(
            "{validarExistencia}", settings.ValidateStockOnCreate ? "true" : "false", StringComparison.Ordinal);
        var responseText = await PostJsonAsync(settings, createPath, payload, "order creation", ct);
        var unavailable = JsonSerializer.Deserialize<List<XionUnavailableProductDto>>(
            responseText, JsonOptions) ?? [];
        if (unavailable.Count > 0)
        {
            var summary = string.Join(", ", unavailable.Select(product =>
                Clean(product.Descripcion) ?? product.ProductoId.ToString(CultureInfo.InvariantCulture)));
            throw new InvalidOperationException($"Xion rejected the order because stock is unavailable: {summary}.");
        }
        if (!await VerifyOrderAsync(settings, externalOrderId, ct))
            throw new InvalidOperationException("Xion accepted the request but the order could not be verified.");

        return new CreateExternalOrderResult(externalOrderId, externalOrderId, "created", responseText);
    }

    private async Task<List<XionCustomerDto>> GetCustomersAsync(
        XionSettings settings,
        CancellationToken ct) =>
        await GetJsonAsync<List<XionCustomerDto>>(
            settings,
            Expand(settings.Endpoints.CustomerSync, settings, null, null),
            "customer synchronization",
            ct) ?? [];

    private async Task<XionProductDto?> GetProductByIdAsync(
        int productId,
        int? customerId,
        XionSettings settings,
        CancellationToken ct)
    {
        var template = customerId.HasValue
            ? settings.Endpoints.ProductDetail
            : settings.Endpoints.ProductDetailWithoutCustomer;
        var path = Expand(template, settings, null, customerId)
            .Replace("{productoId}", productId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return await GetJsonAsync<XionProductDto>(settings, path, "product detail", ct);
    }

    private static XionOrderLine BuildOrderItem(
        OrderItem item,
        int index,
        string externalOrderId,
        XionSettings settings)
    {
        var product = DeserializeProduct(item.RawPayloadJson)
            ?? throw new InvalidOperationException(
                $"Xion order item '{item.ProductNameSnapshot}' has no authoritative product payload.");
        var sale = product.InformacionVenta ?? new XionSaleInfoDto();
        var cost = sale.PrecioCosto != 0 ? sale.PrecioCosto : product.PrecioCosto;
        var payload = new
        {
            PedidoDetalleId = index,
            PedidoId = externalOrderId,
            IdProducto = product.IdProducto,
            CodigoAlterno = string.Empty,
            Descripcion = product.DescripcionLarga ?? item.ProductNameSnapshot,
            DescripcionCorta = product.DescripcionCorta,
            ImpoConsumo = sale.ImpoConsumo != 0 ? sale.ImpoConsumo : product.ImpoConsumo,
            PrecioCosto = cost,
            PrecioCostoPromedio = sale.PrecioCostoPromedio != 0 ? sale.PrecioCostoPromedio : product.PrecioCostoPromedio,
            Precio = item.UnitPrice,
            PrecioPublicoReal = sale.PrecioPublicoReal != 0 ? sale.PrecioPublicoReal : product.PrecioPublicoReal,
            Cantidad = item.Quantity,
            product.Dc1, product.Dc2, product.Dc3, product.Dc4, product.Dc5,
            product.Df1, product.Df2, product.Df3, product.Df4, product.Df5,
            Total = item.LineTotal,
            sale.CanalId, sale.ListaId, sale.EventoId,
            VendedorId = settings.VendedorId,
            IvaVentaId = sale.IvaVentaId != 0 ? sale.IvaVentaId : product.IvaVentaId,
            IvaCompraId = sale.IvaCompraId != 0 ? sale.IvaCompraId : product.IvaCompraId,
            ValorIvaVenta = sale.IvaVentaValor,
            ValorIvaCompra = sale.IvaCompraValor,
            PrefijoIvaVenta = sale.IvaVenta,
            PrefijoIvaCompra = sale.IvaCompra,
            PrecioVenta = item.UnitPrice,
            sale.MargenProducto, sale.MargenVenta, sale.MargenLiquidacion,
            sale.AplicaPuntos, sale.CantidadDpc, sale.DescuentoProducto,
            product.EsCombo,
            UsuarioId = settings.UsuarioId,
            EquipoId = settings.EquipoId,
            product.Embalaje,
            TipoLiquidacionId = 0,
            product.VenderXPeso, product.VenderXFraccion, product.NoManejaInventario,
            product.TieneLote, product.TieneSerial, product.EsServicio, product.EsProduccion,
            product.EsConcesion, product.EsObsequio, product.PerteneceAsociacion,
            product.ProductoWeb, product.EsBolsa, product.EsAlterno, product.EsAncheta, product.Interno,
            ProductosCombo = Array.Empty<object>()
        };
        return new XionOrderLine(payload, item.Quantity, item.UnitPrice, cost);
    }

    private async Task<bool> VerifyOrderAsync(
        XionSettings settings,
        string externalOrderId,
        CancellationToken ct)
    {
        var path = settings.Endpoints.VerifyOrder.Replace(
            "{pedidoId}", Uri.EscapeDataString(externalOrderId), StringComparison.Ordinal);
        return await GetJsonAsync<bool>(settings, path, "order verification", ct);
    }

    private async Task<T?> GetJsonAsync<T>(
        XionSettings settings,
        string path,
        string operation,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var request = CreateRequest(HttpMethod.Get, settings, path);
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Xion {operation} failed ({(int)response.StatusCode}).", null, response.StatusCode);
        return JsonSerializer.Deserialize<T>(text, JsonOptions);
    }

    private async Task<string> PostJsonAsync(
        XionSettings settings,
        string path,
        object payload,
        string operation,
        CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var request = CreateRequest(HttpMethod.Post, settings, path);
        request.Content = JsonContent.Create(payload, options: JsonOptions);
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"Xion {operation} failed ({(int)response.StatusCode}).", null, response.StatusCode);
        return text;
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, XionSettings settings, string path)
    {
        var request = new HttpRequestMessage(
            method,
            new Uri(new Uri(EnsureTrailingSlash(settings.BaseUrl), UriKind.Absolute), path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<ProductReference> AttachExistingSnapshotIdAsync(
        CommerceAdapterContext context,
        ProductReference reference,
        CancellationToken ct)
    {
        if (context.Connection is null || string.IsNullOrWhiteSpace(reference.ExternalProductId))
            return reference;
        var existing = await _unitOfWork.Products.GetByExternalIdAsync(
            context.BusinessId, context.Connection.IntegrationConnectionId, reference.ExternalProductId, ct);
        return existing is null ? reference : reference with { ProductId = existing.ProductId };
    }

    private static CommerceCustomerReference ToCustomerReference(ExternalCommerceCustomer customer) =>
        new(CommerceProvider.Xion, customer.ExternalAccountId, customer.ExternalCustomerId,
            customer.Name, customer.Phone ?? customer.PhoneNormalized);

    private static int? GetCustomerId(CommerceCustomerReference? customer) =>
        customer is { Provider: CommerceProvider.Xion }
            ? ParsePositiveInt(customer.ExternalCustomerId)
            : null;

    private static int? ParsePositiveInt(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;

    private static bool TryGetProductId(string? externalProductId, string? sku, out int productId) =>
        int.TryParse(externalProductId, NumberStyles.Integer, CultureInfo.InvariantCulture, out productId)
        || int.TryParse(sku, NumberStyles.Integer, CultureInfo.InvariantCulture, out productId);

    private static XionProductDto? DeserializeProduct(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return JsonSerializer.Deserialize<XionProductDto>(json, JsonOptions); }
        catch (JsonException) { return null; }
    }

    private static string Expand(string template, XionSettings settings, string? query, int? customerId) =>
        template
            .Replace("{sucursalId}", settings.SucursalId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{vendedorId}", settings.VendedorId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{criterio}", "0", StringComparison.Ordinal)
            .Replace("{busqueda}", Uri.EscapeDataString(query ?? string.Empty), StringComparison.Ordinal)
            .Replace("{bodegaId}", settings.BodegaId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{equipoId}", settings.EquipoId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{clienteId}", (customerId ?? 0).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    private static IntegrationConnection RequireConnection(CommerceAdapterContext context) =>
        context.Connection ?? throw new InvalidOperationException("Xion requires a commerce connection.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";

    private sealed record XionOrderLine(object Payload, decimal Quantity, decimal SalePrice, decimal CostPrice);
}
