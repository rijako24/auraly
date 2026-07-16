using System.Globalization;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Infrastructure.Commerce;

public sealed class MantisCommerceAdapter : ICommerceAdapter, ICommerceCustomerLookup
{
    private static readonly JsonSerializerOptions MantisJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ConcurrentDictionary<string, Task<MantisCustomerIdentity?>> _customerLookups = new(StringComparer.Ordinal);

    public MantisCommerceAdapter(HttpClient httpClient, IUnitOfWork unitOfWork)
    {
        _httpClient = httpClient;
        _unitOfWork = unitOfWork;
    }

    public CommerceProvider Provider => CommerceProvider.Mantis;
    public async Task<CommerceCustomerReference?> FindCustomerAsync(
        CommerceAdapterContext context,
        CancellationToken ct = default)
    {
        var connection = RequireConnection(context);
        var settings = MantisSettings.From(connection);
        var customer = await ResolveCustomerIdentityAsync(
            settings,
            connection.IntegrationConnectionId,
            context.CustomerPhone,
            ct);
        return customer is null
            ? null
            : new CommerceCustomerReference(
                CommerceProvider.Mantis,
                customer.LlaveNit,
                customer.LlaveCliente,
                customer.Name,
                customer.CellPhone ?? customer.Telephone ?? context.CustomerPhone ?? string.Empty);
    }

    private static MantisCustomerIdentity? ToMantisIdentity(CommerceCustomerReference? customer) =>
        customer is { Provider: CommerceProvider.Mantis }
            && !string.IsNullOrWhiteSpace(customer.ExternalAccountId)
            && !string.IsNullOrWhiteSpace(customer.ExternalCustomerId)
                ? new MantisCustomerIdentity(
                    customer.ExternalAccountId,
                    customer.ExternalCustomerId,
                    customer.Name,
                    customer.Phone,
                    null)
                : null;


    public async Task<ProductSearchResult> SearchProductsAsync(ProductSearchRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var connection = RequireConnection(ctx);
        var settings = MantisSettings.From(connection);
        var customer = ToMantisIdentity(ctx.Customer)
            ?? await ResolveCustomerIdentityAsync(settings, connection.IntegrationConnectionId, ctx.CustomerPhone, ct);
        var pageSize = Math.Clamp(request.Limit > 0 ? request.Limit : settings.Catalog.DefaultPageSize, 1, settings.Catalog.MaxPageSize);
        var (products, hasMore) = await FetchProductsAsync(settings, request, request, pageSize, customer, ct);

        if (settings.Catalog.CacheProducts && products.Count > 0)
        {
            var cached = new List<ProductReference>(products.Count);
            if (customer is null)
            {
                foreach (var product in products)
                    cached.Add(await UpsertSnapshotAsync(ctx, product, ct));
                await _unitOfWork.SaveChangesAsync(ct);
            }
            else
            {
                foreach (var product in products)
                    cached.Add(await AttachExistingSnapshotIdAsync(ctx, product, ct));
            }

            products = cached;
        }

        return new ProductSearchResult(products, "mantis", hasMore, ProductSearchAppliedFilters.From(request));
    }
    private async Task<(List<ProductReference> Products, bool HasMore)> FetchProductsAsync(
        MantisSettings settings,
        ProductSearchRequest searchRequest,
        ProductSearchRequest matchRequest,
        int pageSize,
        MantisCustomerIdentity? customer,
        CancellationToken ct)
    {
        var mantisResponse = await FetchProductPageAsync(
            settings, searchRequest, pageSize, customer, ct);
        IReadOnlyList<MantisProductDto> productDtos = mantisResponse.SDTConArtCasalins;
        var reportedTotal = mantisResponse.SDTPaginadoCasalins?.TotalItems ?? 0;
        var firstItemPage = (Math.Max(searchRequest.Page, 1) - 1) * pageSize + 1;
        var expectedPageItems = Math.Min(
            pageSize,
            Math.Max(reportedTotal - firstItemPage + 1, 0));
        if (reportedTotal > 0 && productDtos.Count < expectedPageItems)
        {
            var recovered = new List<MantisProductDto>();
            var lastItemPage = Math.Min(
                reportedTotal,
                firstItemPage + Math.Clamp(matchRequest.Limit, 1, 50) - 1);
            for (var itemPage = firstItemPage; itemPage <= lastItemPage; itemPage++)
            {
                var singlePage = await FetchProductPageAsync(
                    settings, searchRequest with { Page = itemPage }, 1, customer, ct);
                recovered.AddRange(singlePage.SDTConArtCasalins);
            }
            productDtos = recovered;
        }

        var products = productDtos
            .Select(product => MantisProductMapper.ToProductReference(product, settings.Currency))
            .OfType<ProductReference>()
            .Where(product => ProductMatches(product, matchRequest))
            .Take(Math.Clamp(matchRequest.Limit, 1, 50))
            .ToList();

        return (products, HasMore(mantisResponse.SDTPaginadoCasalins, products.Count, pageSize));
    }

    private async Task<MantisProductSearchResponse> FetchProductPageAsync(
        MantisSettings settings,
        ProductSearchRequest request,
        int pageSize,
        MantisCustomerIdentity? customer,
        CancellationToken ct)
    {
        var payload = BuildProductSearchPayload(request, pageSize, customer, settings.Catalog.Warehouse);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var httpRequest = CreateRequest(
            settings,
            settings.Catalog.SearchEndpoint,
            payload);
        var response = await _httpClient.SendAsync(httpRequest, timeout.Token);
        var responseText = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Mantis product search failed ({(int)response.StatusCode}): {responseText}");
        var mantisResponse = JsonSerializer.Deserialize<MantisProductSearchResponse>(responseText, MantisJsonOptions)
            ?? new MantisProductSearchResponse();
        if (!string.IsNullOrWhiteSpace(mantisResponse.ErrorKey))
            throw new InvalidOperationException($"Mantis product search failed: {mantisResponse.ErrorKey}");
        return mantisResponse;
    }
    public async Task<ProductReference?> GetProductAsync(AddOrderItemRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(request.ExternalProductId) || !string.IsNullOrWhiteSpace(request.Sku))
        {
            var result = await SearchProductsAsync(
                new ProductSearchRequest(request.ExternalProductId ?? request.Sku, null, 10),
                ctx,
                ct);

            return result.Products.FirstOrDefault(p =>
                EqualsIgnoreCase(p.ExternalProductId, request.ExternalProductId) ||
                EqualsIgnoreCase(p.Sku, request.Sku));
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var result = await SearchProductsAsync(new ProductSearchRequest(request.Name, null, 10), ctx, ct);
            return result.Products.FirstOrDefault(p => EqualsIgnoreCase(p.Name, request.Name))
                ?? result.Products.FirstOrDefault();
        }

        return null;
    }
    private async Task<MantisCustomerIdentity?> ResolveCustomerIdentityAsync(
        MantisSettings settings,
        Guid connectionId,
        string? phone,
        CancellationToken ct)
    {
        var normalizedPhone = NormalizeCustomerPhone(phone, settings.Customer);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return null;

        var cacheKey = $"{connectionId:N}:{normalizedPhone}";
        var lookup = _customerLookups.GetOrAdd(
            cacheKey,
            _ => FetchCustomerIdentityAsync(settings, normalizedPhone, ct));
        try
        {
            return await lookup.WaitAsync(ct);
        }
        catch
        {
            _customerLookups.TryRemove(cacheKey, out _);
            throw;
        }
    }

    private async Task<MantisCustomerIdentity?> FetchCustomerIdentityAsync(
        MantisSettings settings,
        string normalizedPhone,
        CancellationToken ct)
    {
        var payload = new
        {
            Pagina = 1,
            CantPag = settings.Customer.LookupPageSize,
            IdeCliente = string.Empty,
            NomCliente = string.Empty,
            CelCliente = normalizedPhone,
            CiuCliente = string.Empty,
            ZonCliente = string.Empty,
            BarCliente = string.Empty,
            RutCliente = string.Empty,
            TelCliente = string.Empty
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var request = CreateRequest(settings, settings.Customer.SearchEndpoint, payload);
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Mantis customer lookup failed ({(int)response.StatusCode}).");

        var mantisResponse = JsonSerializer.Deserialize<MantisCustomerSearchResponse>(responseText, MantisJsonOptions)
            ?? new MantisCustomerSearchResponse();
        if (!string.IsNullOrWhiteSpace(mantisResponse.ErrorKey))
            throw new InvalidOperationException($"Mantis customer lookup failed: {mantisResponse.ErrorKey}");

        var matches = mantisResponse.SDTConsultarClientesCasalins
            .Where(customer =>
                PhoneMatches(customer.CelularCliente, normalizedPhone, settings.Customer)
                || PhoneMatches(customer.TelefonoClientes, normalizedPhone, settings.Customer))
            .Where(customer =>
                !string.IsNullOrWhiteSpace(customer.LlaveNit)
                && !string.IsNullOrWhiteSpace(customer.LlaveCliente))
            .Select(customer => new MantisCustomerIdentity(
                customer.LlaveNit!.Trim(),
                customer.LlaveCliente!.Trim(),
                Clean(customer.NombreCliente),
                Clean(customer.CelularCliente),
                Clean(customer.TelefonoClientes)))
            .GroupBy(customer => $"{customer.LlaveNit}:{customer.LlaveCliente}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        return matches.Count switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException("Mantis returned more than one customer for the same phone.")
        };
    }

    private static bool PhoneMatches(string? candidate, string normalizedPhone, MantisCustomerSettings settings) =>
        string.Equals(
            NormalizeCustomerPhone(candidate, settings),
            normalizedPhone,
            StringComparison.Ordinal);

    private static string? NormalizeCustomerPhone(string? phone, MantisCustomerSettings settings)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.StartsWith("00", StringComparison.Ordinal))
            digits = digits[2..];

        var countryCode = new string(settings.CountryCode.Where(char.IsDigit).ToArray());
        if (countryCode.Length > 0
            && digits.StartsWith(countryCode, StringComparison.Ordinal)
            && digits.Length == countryCode.Length + settings.NationalPhoneLength)
        {
            digits = digits[countryCode.Length..];
        }

        return digits.Length >= 7 ? digits : null;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();


    public async Task<CreateExternalOrderResult> CreateOrderAsync(
        Order order,
        IReadOnlyList<OrderItem> items,
        CommerceAdapterContext ctx,
        CancellationToken ct = default)
    {
        var connection = RequireConnection(ctx);
        var settings = MantisSettings.From(connection);
        if (settings.Order.MockCreateOrders)
        {
            var mockPayload = new
            {
                provider = "mantis",
                mode = "mock",
                order_id = order.OrderId,
                customer = new
                {
                    name = order.CustomerNameSnapshot,
                    document = order.CustomerDocumentSnapshot,
                    phone = order.CustomerPhoneSnapshot,
                    address = order.DeliveryAddressSnapshot
                },
                items = items.Select(item => new
                {
                    code = item.Sku ?? item.ExternalProductId,
                    name = item.ProductNameSnapshot,
                    quantity = item.Quantity
                }),
                total = order.Total,
                created_at = DateTime.UtcNow
            };

            return new CreateExternalOrderResult(
                $"mantis-mock-{order.OrderId:N}",
                null,
                "mocked",
                JsonSerializer.Serialize(mockPayload, CommerceJson.Options));
        }

        var customer = ToMantisIdentity(ctx.Customer) ?? await ResolveCustomerIdentityAsync(
            settings,
            connection.IntegrationConnectionId,
            order.CustomerPhoneSnapshot ?? ctx.CustomerPhone,
            ct);
        if (customer is null)
            throw new InvalidOperationException("Mantis customer was not found for the order phone.");

        var articles = items.Select(item => new
            {
                CodigoArticulos = Clean(item.Sku) ?? Clean(item.ExternalProductId),
                CantidadArticulos = item.Quantity.ToString("0.################", CultureInfo.InvariantCulture)
            })
            .ToList();
        if (articles.Count == 0)
            throw new InvalidOperationException("Mantis order has no items.");
        if (articles.Any(article => string.IsNullOrWhiteSpace(article.CodigoArticulos)))
            throw new InvalidOperationException("Every Mantis order item requires a product code.");

        var payload = new
        {
            customer.LlaveNit,
            customer.LlaveCliente,
            Articulos = articles,
            bodega = settings.Order.Warehouse,
            PedObs = string.IsNullOrWhiteSpace(order.Notes)
                ? $"Creado por Talkio. Ref {order.OrderId:N}"
                : order.Notes.Trim()
        };

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var request = CreateRequest(settings, settings.Order.CreateEndpoint, payload);
        using var response = await _httpClient.SendAsync(request, timeout.Token);
        var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Mantis order creation failed ({(int)response.StatusCode}).");

        using var document = JsonDocument.Parse(responseText);
        var externalOrderId = FindJsonValue(
            document.RootElement,
            "bPedNum",
            "NroPedido",
            "NumeroPedido",
            "PedNum");
        if (string.IsNullOrWhiteSpace(externalOrderId))
            throw new InvalidOperationException("Mantis order creation did not return an order number.");

        return new CreateExternalOrderResult(
            externalOrderId,
            externalOrderId,
            "created",
            responseText);
    }

    private static string? FindJsonValue(JsonElement element, params string[] propertyNames)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (propertyNames.Any(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    && property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    var value = property.Value.ToString().Trim();
                    if (value.Length > 0)
                        return value;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindJsonValue(property.Value, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindJsonValue(item, propertyNames);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested;
            }
        }

        return null;
    }

    private static HttpRequestMessage CreateRequest(MantisSettings settings, string endpoint, object payload)
    {
        if (string.IsNullOrWhiteSpace(settings.AuthorizationToken))
            throw new InvalidOperationException("Mantis authorizationToken is not configured for this business.");

        var request = new HttpRequestMessage(HttpMethod.Post, BuildEndpointUri(settings.BaseUrl, endpoint))
        {
            Content = JsonContent.Create(payload, options: MantisJsonOptions)
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("Authorization", settings.AuthorizationToken);
        return request;
    }

    private static Uri BuildEndpointUri(string baseUrl, string endpoint) =>
        new(new Uri(EnsureTrailingSlash(baseUrl), UriKind.Absolute), endpoint);

    private static object BuildProductSearchPayload(
        ProductSearchRequest request,
        int pageSize,
        MantisCustomerIdentity? customer,
        string warehouse)
    {
        var query = request.Query?.Trim();
        var code = LooksLikeCode(query) ? query : string.Empty;
        var name = string.IsNullOrWhiteSpace(query) || LooksLikeCode(query) ? "VACIO" : query;

        return new
        {
            Pagina = Math.Max(request.Page, 1),
            CantPag = pageSize,
            codproducto = code,
            Nomproducto = name,
            Catproducto = request.Category ?? string.Empty,
            SubCatproducto = request.Subcategory ?? string.Empty,
            Famproducto = request.Family ?? string.Empty,
            Claproducto = request.ProductClass ?? string.Empty,
            Tipoproducto = string.Empty,
            LlaveNit = customer?.LlaveNit ?? string.Empty,
            LlaveCliente = customer?.LlaveCliente ?? string.Empty,
            Bodega = warehouse
        };
    }

    private async Task<ProductReference> UpsertSnapshotAsync(CommerceAdapterContext ctx, ProductReference reference, CancellationToken ct)
    {
        var connectionId = ctx.Connection?.IntegrationConnectionId
            ?? throw new InvalidOperationException("Mantis product snapshot requires a connection.");

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
            return reference with { ProductId = product.ProductId };
        }

        if (IsCommerciallyIncomplete(reference) && IsSellableSnapshot(existing))
        {
            return reference with
            {
                ProductId = existing.ProductId,
                UnitPrice = existing.UnitPrice,
                Currency = existing.Currency,
                StockQuantity = existing.StockQuantity,
                IsActive = existing.IsActive
            };
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
        return reference with { ProductId = existing.ProductId };
    }

    private static bool IsCommerciallyIncomplete(ProductReference reference) =>
        !reference.IsActive && reference.UnitPrice <= 0m && (reference.StockQuantity ?? 0m) <= 0m;

    private static bool IsSellableSnapshot(Product product) =>
        product.IsActive && (!product.ManageStock || (product.StockQuantity ?? 0m) > 0m);
    private async Task<ProductReference> AttachExistingSnapshotIdAsync(
        CommerceAdapterContext ctx,
        ProductReference reference,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reference.ExternalProductId) || ctx.Connection is null)
            return reference;

        var existing = await _unitOfWork.Products.GetByExternalIdAsync(
            ctx.BusinessId,
            ctx.Connection.IntegrationConnectionId,
            reference.ExternalProductId,
            ct);
        return existing is null ? reference : reference with { ProductId = existing.ProductId };
    }


    private static bool ProductMatches(ProductReference product, ProductSearchRequest request)
    {
        var query = request.Query?.Trim();
        var category = request.Category?.Trim();
        var family = request.Family?.Trim();
        var subcategory = request.Subcategory?.Trim();
        var productClass = request.ProductClass?.Trim();
        return (string.IsNullOrWhiteSpace(query)
                || ContainsSearchText(product.Name, query)
                || ContainsSearchText(product.Sku, query)
                || ContainsSearchText(product.Description, query)
                || ContainsSearchText(product.CategoryName, query)
                || ContainsSearchText(product.FamilyName, query)
                || ContainsSearchText(product.SubcategoryName, query)
                || ContainsSearchText(product.ProductClassName, query))
            && MatchesFilter(product.CategoryName, category)
            && MatchesFilter(product.FamilyName, family)
            && MatchesFilter(product.SubcategoryName, subcategory)
            && MatchesFilter(product.ProductClassName, productClass);
    }

    private static bool HasMore(MantisPaginationDto? pagination, int returnedCount, int pageSize)
    {
        if (bool.TryParse(pagination?.NextPage, out var hasNextPage))
            return hasNextPage && returnedCount >= pageSize;

        return returnedCount >= pageSize;
    }

    private static IntegrationConnection RequireConnection(CommerceAdapterContext ctx) =>
        ctx.Connection ?? throw new InvalidOperationException("Mantis requires a commerce connection.");
    private static bool MatchesFilter(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter) || Contains(value, filter);

    private static bool Contains(string? value, string term) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(term, StringComparison.OrdinalIgnoreCase);

    private static bool ContainsSearchText(string? value, string term)
    {
        if (Contains(value, term))
            return true;

        var normalizedValue = NormalizeSearchText(value);
        if (string.IsNullOrWhiteSpace(normalizedValue))
            return false;

        var tokens = CatalogSearchText.GetSearchTerms(term)
            .Select(NormalizeSearchText)
            .Where(token => token.Length >= 2)
            .ToArray();
        return tokens.Length > 0 && tokens.All(token => normalizedValue.Contains(token, StringComparison.Ordinal));
    }

    private static string NormalizeSearchText(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static bool EqualsIgnoreCase(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && left.Equals(right, StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeCode(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Any(char.IsDigit)
        && value.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.');

    private static string EnsureTrailingSlash(string value) =>
        value.EndsWith("/", StringComparison.Ordinal) ? value : value + "/";
}