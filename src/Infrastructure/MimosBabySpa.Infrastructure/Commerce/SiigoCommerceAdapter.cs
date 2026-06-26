using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Infrastructure.Commerce;

public sealed class SiigoCommerceAdapter : ICommerceAdapter
{
    private readonly HttpClient _httpClient;
    private readonly IUnitOfWork _unitOfWork;

    public SiigoCommerceAdapter(HttpClient httpClient, IUnitOfWork unitOfWork)
    {
        _httpClient = httpClient;
        _unitOfWork = unitOfWork;
    }

    public CommerceProvider Provider => CommerceProvider.Siigo;

    public async Task<ProductSearchResult> SearchProductsAsync(ProductSearchRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var connection = RequireConnection(ctx);
        var settings = SiigoSettings.From(connection);
        await ConfigureAsync(settings, connection, ct);

        var pageSize = Math.Clamp(Math.Max(settings.Catalog.DefaultPageSize, request.Limit), 1, 100);
        var response = await _httpClient.GetFromJsonAsync<SiigoProductListResponse>(
            $"/v1/products?page=1&page_size={pageSize}",
            CommerceJson.Options,
            ct);

        var products = response?.Results ?? [];
        var filtered = Filter(products, request)
            .Where(p => p.Active)
            .Take(Math.Clamp(request.Limit, 1, 50))
            .Select(p => Map(p, settings.Catalog.PriceListPosition))
            .ToList();

        if (settings.Catalog.CacheProducts)
        {
            var cached = new List<ProductReference>(filtered.Count);
            foreach (var product in filtered)
                cached.Add(await UpsertSnapshotAsync(ctx, product, ct));
            await _unitOfWork.SaveChangesAsync(ct);
            filtered = cached;
        }

        return new ProductSearchResult(filtered, "siigo", (response?.Pagination?.TotalResults ?? 0) > pageSize);
    }

    public async Task<ProductReference?> GetProductAsync(AddOrderItemRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ExternalProductId))
        {
            var connection = RequireConnection(ctx);
            var settings = SiigoSettings.From(connection);
            await ConfigureAsync(settings, connection, ct);
            var product = await _httpClient.GetFromJsonAsync<SiigoProduct>(
                $"/v1/products/{Uri.EscapeDataString(request.ExternalProductId)}",
                CommerceJson.Options,
                ct);
            return product is null ? null : Map(product, settings.Catalog.PriceListPosition);
        }

        if (!string.IsNullOrWhiteSpace(request.Sku) || !string.IsNullOrWhiteSpace(request.Name))
        {
            var result = await SearchProductsAsync(
                new ProductSearchRequest(request.Sku ?? request.Name, null, 10),
                ctx,
                ct);
            return result.Products.FirstOrDefault(p =>
                string.Equals(p.Sku, request.Sku, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, request.Name, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    public async Task<CreateExternalOrderResult> CreateOrderAsync(Order order, IReadOnlyList<OrderItem> items, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var connection = RequireConnection(ctx);
        var settings = SiigoSettings.From(connection);
        await ConfigureAsync(settings, connection, ct);

        var payload = BuildInvoicePayload(order, items, settings);
        var response = await _httpClient.PostAsJsonAsync("/v1/invoices", payload, CommerceJson.Options, ct);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        var id = ReadString(root, "id") ?? throw new InvalidOperationException("Siigo invoice response did not include id.");
        var number = ReadString(root, "name");
        var status = root.TryGetProperty("stamp", out var stamp) ? ReadString(stamp, "status") : null;

        return new CreateExternalOrderResult(id, number, status, responseText);
    }

    private async Task ConfigureAsync(SiigoSettings settings, IntegrationConnection connection, CancellationToken ct)
    {
        _httpClient.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
        _httpClient.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.Add("Partner-Id", settings.PartnerId);

        var token = await AuthenticateAsync(settings, ct);
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        connection.AccountIdentifier = settings.Username;
        connection.LastError = null;
        connection.UpdatedAt = DateTime.UtcNow;
    }

    private async Task<string> AuthenticateAsync(SiigoSettings settings, CancellationToken ct)
    {
        var auth = await _httpClient.PostAsJsonAsync(
            "/auth",
            new { username = settings.Username, access_key = settings.AccessKey },
            CommerceJson.Options,
            ct);
        auth.EnsureSuccessStatusCode();
        var token = await auth.Content.ReadFromJsonAsync<SiigoTokenResponse>(CommerceJson.Options, ct);
        return token?.AccessToken ?? throw new InvalidOperationException("Siigo auth response did not include access_token.");
    }

    private async Task<ProductReference> UpsertSnapshotAsync(CommerceAdapterContext ctx, ProductReference reference, CancellationToken ct)
    {
        var connectionId = ctx.Connection?.IntegrationConnectionId
            ?? throw new InvalidOperationException("Siigo product snapshot requires a connection.");
        if (string.IsNullOrWhiteSpace(reference.ExternalProductId))
            return reference;

        var existing = await _unitOfWork.Products.GetByExternalIdAsync(ctx.BusinessId, connectionId, reference.ExternalProductId, ct);
        if (existing is null)
        {
            var product = new Product
            {
                ProductId = Guid.NewGuid(),
                BusinessId = ctx.BusinessId,
                IntegrationConnectionId = connectionId,
                ExternalProductId = reference.ExternalProductId,
                Source = ProductSource.External,
                Sku = reference.Sku,
                Name = reference.Name,
                Description = reference.Description,
                CategoryName = reference.CategoryName,
                UnitPrice = reference.UnitPrice,
                Currency = reference.Currency,
                ManageStock = true,
                StockQuantity = reference.StockQuantity,
                IsActive = reference.IsActive,
                RawPayloadJson = reference.RawPayloadJson,
                LastSyncedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Products.CreateAsync(product, ct);
            return WithProductId(reference, product.ProductId);
        }

        existing.Sku = reference.Sku;
        existing.Name = reference.Name;
        existing.Description = reference.Description;
        existing.CategoryName = reference.CategoryName;
        existing.UnitPrice = reference.UnitPrice;
        existing.Currency = reference.Currency;
        existing.StockQuantity = reference.StockQuantity;
        existing.IsActive = reference.IsActive;
        existing.RawPayloadJson = reference.RawPayloadJson;
        existing.LastSyncedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Products.UpdateAsync(existing, ct);
        return WithProductId(reference, existing.ProductId);
    }

    private static ProductReference WithProductId(ProductReference reference, Guid productId) =>
        reference with { ProductId = productId };

    private static object BuildInvoicePayload(Order order, IReadOnlyList<OrderItem> items, SiigoSettings settings)
    {
        var customerName = SplitName(order.CustomerNameSnapshot);
        var dueDate = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var taxes = settings.Order.DefaultTaxIds.Select(id => new { id }).ToArray();

        return new
        {
            document = new { id = settings.Order.DocumentId },
            date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            customer = new
            {
                person_type = settings.Order.DefaultCustomer.PersonType,
                id_type = settings.Order.DefaultCustomer.IdType,
                identification = string.IsNullOrWhiteSpace(order.CustomerDocumentSnapshot)
                    ? settings.Order.DefaultCustomer.Identification
                    : order.CustomerDocumentSnapshot,
                branch_office = settings.Order.DefaultCustomer.BranchOffice,
                name = customerName,
                address = string.IsNullOrWhiteSpace(order.DeliveryAddressSnapshot)
                    ? null
                    : new
                    {
                        address = order.DeliveryAddressSnapshot,
                        city = settings.Order.DefaultCustomer.City
                    },
                phones = string.IsNullOrWhiteSpace(order.CustomerPhoneSnapshot)
                    ? Array.Empty<object>()
                    : [new { indicative = "57", number = order.CustomerPhoneSnapshot }],
                contacts = new[]
                {
                    new
                    {
                        first_name = customerName[0],
                        last_name = customerName.Length > 1 ? customerName[1] : customerName[0],
                        email = order.CustomerEmailSnapshot,
                        phone = string.IsNullOrWhiteSpace(order.CustomerPhoneSnapshot)
                            ? null
                            : new { indicative = "57", number = order.CustomerPhoneSnapshot }
                    }
                }
            },
            cost_center = settings.Order.CostCenterId,
            seller = settings.Order.SellerId,
            stamp = new { send = settings.Order.StampSend },
            mail = new { send = settings.Order.MailSend },
            observations = string.IsNullOrWhiteSpace(order.Notes)
                ? "Pedido creado desde WhatsApp por Auraly"
                : order.Notes,
            items = items.Select(i => new
            {
                code = i.Sku,
                description = i.ProductNameSnapshot,
                quantity = i.Quantity,
                price = i.UnitPrice,
                discount = 0,
                taxes
            }).ToArray(),
            payments = new[]
            {
                new
                {
                    id = settings.Order.PaymentTypeId,
                    value = order.Total,
                    due_date = dueDate
                }
            }
        };
    }

    private static IEnumerable<SiigoProduct> Filter(IEnumerable<SiigoProduct> products, ProductSearchRequest request)
    {
        var query = request.Query?.Trim();
        var category = request.Category?.Trim();
        return products.Where(p =>
            (string.IsNullOrWhiteSpace(query)
             || Contains(p.Name, query)
             || Contains(p.Code, query)
             || Contains(p.Description, query)
             || Contains(p.AccountGroup?.Name, query))
            && (string.IsNullOrWhiteSpace(category) || Contains(p.AccountGroup?.Name, category)));
    }

    private static ProductReference Map(SiigoProduct product, int priceListPosition)
    {
        var price = product.Prices?
            .SelectMany(p => p.PriceList ?? [])
            .FirstOrDefault(p => p.Position == priceListPosition)
            ?? product.Prices?.SelectMany(p => p.PriceList ?? []).FirstOrDefault();
        var currency = product.Prices?.FirstOrDefault()?.CurrencyCode ?? "COP";
        var raw = JsonSerializer.Serialize(product, CommerceJson.Options);
        return new ProductReference(
            null,
            product.Id,
            product.Code,
            product.Name ?? product.Code ?? "Producto",
            product.Description,
            product.AccountGroup?.Name,
            price?.Value ?? 0,
            currency,
            product.AvailableQuantity,
            product.Active && (!product.StockControl || (product.AvailableQuantity ?? 0) > 0),
            null,
            null,
            null,
            null,
            raw)
        { IsActive = product.Active };
    }

    private static IntegrationConnection RequireConnection(CommerceAdapterContext ctx) =>
        ctx.Connection ?? throw new InvalidOperationException("Siigo requires a commerce connection.");

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.ToString() : null;

    private static string[] SplitName(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return ["Cliente", "WhatsApp"];
        var parts = fullName.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 1 ? [parts[0], parts[0]] : [parts[0], parts[1]];
    }

    private sealed record SiigoTokenResponse([property: JsonPropertyName("access_token")] string AccessToken);

    private sealed class SiigoProductListResponse
    {
        public SiigoPagination? Pagination { get; set; }
        public List<SiigoProduct> Results { get; set; } = [];
    }

    private sealed class SiigoPagination
    {
        public int Page { get; set; }
        [JsonPropertyName("page_size")]
        public int PageSize { get; set; }
        [JsonPropertyName("total_results")]
        public int TotalResults { get; set; }
    }

    private sealed class SiigoProduct
    {
        public string? Id { get; set; }
        public string? Code { get; set; }
        public string? Name { get; set; }
        [JsonPropertyName("account_group")]
        public SiigoNamedId? AccountGroup { get; set; }
        [JsonPropertyName("stock_control")]
        public bool StockControl { get; set; }
        public bool Active { get; set; }
        public List<SiigoPriceCurrency>? Prices { get; set; }
        public string? Description { get; set; }
        [JsonPropertyName("available_quantity")]
        public decimal? AvailableQuantity { get; set; }
    }

    private sealed class SiigoNamedId
    {
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    private sealed class SiigoPriceCurrency
    {
        [JsonPropertyName("currency_code")]
        public string? CurrencyCode { get; set; }
        [JsonPropertyName("price_list")]
        public List<SiigoPriceList>? PriceList { get; set; }
    }

    private sealed class SiigoPriceList
    {
        public int Position { get; set; }
        public decimal Value { get; set; }
    }
}
