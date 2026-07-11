using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Infrastructure.Commerce;

public sealed class MantisCommerceAdapter : ICommerceAdapter
{
    private static readonly JsonSerializerOptions MantisJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly IUnitOfWork _unitOfWork;

    public MantisCommerceAdapter(HttpClient httpClient, IUnitOfWork unitOfWork)
    {
        _httpClient = httpClient;
        _unitOfWork = unitOfWork;
    }

    public CommerceProvider Provider => CommerceProvider.Mantis;

    public async Task<ProductSearchResult> SearchProductsAsync(ProductSearchRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var connection = RequireConnection(ctx);
        var settings = MantisSettings.From(connection);
        var pageSize = Math.Clamp(request.Limit > 0 ? request.Limit : settings.Catalog.DefaultPageSize, 1, settings.Catalog.MaxPageSize);
        var (products, hasMore) = await FetchProductsAsync(settings, request, request, pageSize, ct);

        if (products.Count == 0)
        {
            foreach (var fallbackQuery in BuildFallbackQueries(request.Query))
            {
                var fallbackRequest = request with { Query = fallbackQuery };
                (products, hasMore) = await FetchProductsAsync(settings, fallbackRequest, fallbackRequest, pageSize, ct);
                if (products.Count > 0)
                    break;
            }
        }

        if (settings.Catalog.CacheProducts && products.Count > 0)
        {
            var cached = new List<ProductReference>(products.Count);
            foreach (var product in products)
                cached.Add(await UpsertSnapshotAsync(ctx, product, ct));
            await _unitOfWork.SaveChangesAsync(ct);
            products = cached;
        }

        return new ProductSearchResult(products, "mantis", hasMore, ProductSearchAppliedFilters.From(request));
    }
    private async Task<(List<ProductReference> Products, bool HasMore)> FetchProductsAsync(
        MantisSettings settings,
        ProductSearchRequest searchRequest,
        ProductSearchRequest matchRequest,
        int pageSize,
        CancellationToken ct)
    {
        var payload = BuildProductSearchPayload(searchRequest, pageSize);
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

        var products = mantisResponse.SDTConArtCasalins
            .Select(product => MantisProductMapper.ToProductReference(product, settings.Currency))
            .OfType<ProductReference>()
            .Where(product => ProductMatches(product, matchRequest))
            .Take(Math.Clamp(matchRequest.Limit, 1, 50))
            .ToList();

        return (products, HasMore(mantisResponse.SDTPaginadoCasalins, products.Count, pageSize));
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

    public Task<CreateExternalOrderResult> CreateOrderAsync(Order order, IReadOnlyList<OrderItem> items, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var payload = new
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
            items = items.Select(i => new
            {
                code = i.Sku ?? i.ExternalProductId,
                name = i.ProductNameSnapshot,
                quantity = i.Quantity
            }),
            total = order.Total,
            created_at = DateTime.UtcNow
        };

        var responseJson = JsonSerializer.Serialize(payload, CommerceJson.Options);
        return Task.FromResult(new CreateExternalOrderResult(
            $"mantis-mock-{order.OrderId:N}",
            null,
            "mocked",
            responseJson));
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

    private static object BuildProductSearchPayload(ProductSearchRequest request, int pageSize)
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
            Tipoproducto = string.Empty
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

    private static IEnumerable<string> BuildFallbackQueries(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in query.Split([' ', ',', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.Length < 3 || !token.Any(char.IsLetter) || !seen.Add(token))
                continue;

            yield return token;
        }
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

        var tokens = BuildFallbackQueries(term)
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