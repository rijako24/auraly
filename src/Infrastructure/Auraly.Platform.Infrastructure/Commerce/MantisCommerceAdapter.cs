using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Domain.Catalog;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Infrastructure.Commerce;

public sealed class MantisCommerceAdapter :
    ICommerceAdapter,
    IAuthoritativeCommercePricingAdapter,
    ICommerceCustomerLookup,
    ICommerceProductIdentitySource,
    ICommerceProductDeltaIdentitySource,
    ICommerceCustomerIdentitySource,
    ICommerceOrderHistorySource
{
    private static readonly JsonSerializerOptions MantisJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new FlexibleStringJsonConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly IUnitOfWork _unitOfWork;

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
            context.BusinessId,
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

    private static MantisCustomerIdentity? ToMantisIdentity(MantisGenericCustomerSettings customer) =>
        !string.IsNullOrWhiteSpace(customer.LlaveNit)
        && !string.IsNullOrWhiteSpace(customer.LlaveCliente)
            ? new MantisCustomerIdentity(
                customer.LlaveNit.Trim(),
                customer.LlaveCliente.Trim(),
                null,
                null,
                null)
            : null;



    public async Task<ProductSearchResult> SearchProductsAsync(ProductSearchRequest request, CommerceAdapterContext ctx, CancellationToken ct = default)
    {
        var connection = RequireConnection(ctx);
        var settings = MantisSettings.From(connection);
        var customer = ToMantisIdentity(ctx.Customer)
            ?? await ResolveCustomerIdentityAsync(settings, ctx.BusinessId, connection.IntegrationConnectionId, ctx.CustomerPhone, ct)
            ?? ToMantisIdentity(settings.GenericCustomer);
        var pageSize = Math.Clamp(request.Limit > 0 ? request.Limit : settings.Catalog.DefaultPageSize, 1, settings.Catalog.MaxPageSize);
        var (products, hasMore) = await FetchProductsAsync(settings, request, request, pageSize, customer, Clean(ctx.WarehouseCode) ?? settings.Catalog.Warehouse, ct);

        if (products.Count > 0)
        {
            var cached = new List<ProductReference>(products.Count);
            foreach (var product in products)
                cached.Add(await AttachExistingSnapshotIdAsync(ctx, product, ct));

            products = cached;
        }

        return new ProductSearchResult(products, "mantis", hasMore, ProductSearchAppliedFilters.From(request));
    }

    public async Task<ProductIdentityPage> GetProductIdentityPageAsync(
        CommerceAdapterContext context,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var connection = RequireConnection(context);
        var settings = MantisSettings.From(connection);
        var effectivePageSize = Math.Clamp(pageSize, 1, settings.Catalog.MaxPageSize);
        var request = new ProductSearchRequest(
            null,
            null,
            effectivePageSize,
            IncludeStock: false,
            Page: Math.Max(page, 1));
        var response = await FetchProductPageAsync(settings, request, effectivePageSize, ToMantisIdentity(settings.GenericCustomer), settings.Catalog.Warehouse, ct);
        var products = (response.SDTConArtCasalins ?? [])
            .Select(product => MantisProductMapper.ToProductReference(product, settings.Currency))
            .OfType<ProductReference>()
            .ToList();
        return new ProductIdentityPage(
            products,
            HasMore(response.SDTPaginadoCasalins, products.Count, effectivePageSize));
    }


    public async Task<ProductIdentityPage> GetProductIdentityDeltaPageAsync(
        CommerceAdapterContext context,
        DateTime changedOnUtc,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var connection = RequireConnection(context);
        var settings = MantisSettings.From(connection);
        var effectivePageSize = Math.Clamp(pageSize, 1, settings.Catalog.MaxPageSize);
        var request = new ProductSearchRequest(
            null,
            null,
            effectivePageSize,
            IncludeStock: false,
            Page: Math.Max(page, 1));
        var response = await FetchProductPageAsync(
            settings,
            request,
            effectivePageSize,
            ToMantisIdentity(settings.GenericCustomer),
            settings.Catalog.Warehouse,
            ct,
            modificationDate: changedOnUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        var products = (response.SDTConArtCasalins ?? [])
            .Select(product => MantisProductMapper.ToProductReference(product, settings.Currency))
            .OfType<ProductReference>()
            .ToList();
        return new ProductIdentityPage(products,
            HasMore(response.SDTPaginadoCasalins, products.Count, effectivePageSize));
    }
    private async Task<(List<ProductReference> Products, bool HasMore)> FetchProductsAsync(
        MantisSettings settings,
        ProductSearchRequest searchRequest,
        ProductSearchRequest matchRequest,
        int pageSize,
        MantisCustomerIdentity? customer,
        string warehouse,
        CancellationToken ct)
    {
        var mantisResponse = await FetchProductPageAsync(
            settings, searchRequest, pageSize, customer, warehouse, ct);
        IReadOnlyList<MantisProductDto> productDtos = mantisResponse.SDTConArtCasalins ?? [];
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
                    settings, searchRequest with { Page = itemPage }, 1, customer, warehouse, ct);
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
        string warehouse,
        CancellationToken ct,
        string creationDate = "",
        string modificationDate = "")
    {
        var payload = BuildProductSearchPayload(
            request, pageSize, customer, warehouse, creationDate, modificationDate);
        var responseText = await PostJsonWithRetryAsync(
            settings,
            settings.Catalog.SearchEndpoint,
            payload,
            "product search",
            ct);
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

            var matched = result.Products.FirstOrDefault(p =>
                EqualsIgnoreCase(p.ExternalProductId, request.ExternalProductId) ||
                EqualsIgnoreCase(p.Sku, request.Sku));
            if (matched is not null)
                return matched;

            return result.HasMore
                ? await FindLocalGenericIdentityAsync(request, ctx, ct)
                : null;
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var result = await SearchProductsAsync(new ProductSearchRequest(request.Name, null, 10), ctx, ct);
            return result.Products.FirstOrDefault(p => EqualsIgnoreCase(p.Name, request.Name))
                ?? result.Products.FirstOrDefault();
        }

        return null;
    }

    private async Task<ProductReference?> FindLocalGenericIdentityAsync(
        AddOrderItemRequest request,
        CommerceAdapterContext context,
        CancellationToken ct)
    {
        // A customer-specific quote must always come from Mantis. Falling back to
        // a generic local snapshot here would silently apply the wrong price list.
        if (context.Customer is not null || context.Connection is null)
            return null;

        Product? product = null;
        if (!string.IsNullOrWhiteSpace(request.ExternalProductId))
        {
            product = await _unitOfWork.Products.GetByExternalIdAsync(
                context.BusinessId,
                context.Connection.IntegrationConnectionId,
                request.ExternalProductId,
                ct);
        }
        if (product is null && !string.IsNullOrWhiteSpace(request.Sku))
            product = await _unitOfWork.Products.GetBySkuAsync(context.BusinessId, request.Sku, ct);
        if (product is null || !product.IsActive)
            return null;

        return new ProductReference(
            product.ProductId,
            product.ExternalProductId,
            product.Sku,
            product.Name,
            product.Description,
            product.CategoryName,
            product.UnitPrice,
            product.Currency,
            product.StockQuantity,
            RawPayloadJson: product.RawPayloadJson)
        { IsActive = true };
    }
    private async Task<MantisCustomerIdentity?> ResolveCustomerIdentityAsync(
        MantisSettings settings,
        Guid businessId,
        Guid connectionId,
        string? phone,
        CancellationToken ct)
    {
        var normalizedPhone = NormalizeCustomerPhone(phone, settings.Customer);
        if (string.IsNullOrWhiteSpace(normalizedPhone))
            return null;

        var customerRepository = _unitOfWork.ExternalCommerceCustomers;
        var local = customerRepository is null
            ? []
            : await customerRepository.FindActiveByPhoneAsync(businessId, connectionId, normalizedPhone, ct);
        if (local.Count == 1)
        {
            var customer = local[0];
            return new MantisCustomerIdentity(
                customer.ExternalAccountId,
                customer.ExternalCustomerId,
                customer.Name,
                customer.Phone,
                null);
        }
        if (local.Count > 1)
            return null;

        var remote = await FetchCustomerIdentityAsync(settings, normalizedPhone, ct);
        if (remote is null)
            return null;

        if (customerRepository is null)
            return remote;

        var now = DateTime.UtcNow;
        var existing = await customerRepository.GetByExternalKeysAsync(
            businessId,
            connectionId,
            remote.LlaveNit,
            remote.LlaveCliente,
            ct);
        if (existing is null)
        {
            await customerRepository.CreateAsync(new ExternalCommerceCustomer
            {
                ExternalCommerceCustomerId = Guid.NewGuid(),
                BusinessId = businessId,
                IntegrationConnectionId = connectionId,
                ExternalAccountId = remote.LlaveNit,
                ExternalCustomerId = remote.LlaveCliente,
                Name = remote.Name,
                PhoneNormalized = normalizedPhone,
                Phone = remote.CellPhone ?? remote.Telephone,
                IsActive = true,
                LastSyncedAt = now,
                CreatedAt = now
            }, ct);
        }
        else
        {
            existing.Name = remote.Name;
            existing.PhoneNormalized = normalizedPhone;
            existing.Phone = remote.CellPhone ?? remote.Telephone;
            existing.IsActive = true;
            existing.LastSyncedAt = now;
            existing.UpdatedAt = now;
            await customerRepository.UpdateAsync(existing, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return remote;
    }

    public async Task<ExternalCustomerIdentityPage> GetCustomerIdentityPageAsync(
        CommerceAdapterContext context,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var settings = MantisSettings.From(RequireConnection(context));
        var take = Math.Clamp(pageSize, 1, 20);
        var payload = new
        {
            Pagina = Math.Max(page, 1),
            CantPag = take,
            IdeCliente = string.Empty,
            NomCliente = string.Empty,
            CelCliente = string.Empty,
            CiuCliente = string.Empty,
            ZonCliente = string.Empty,
            BarCliente = string.Empty,
            RutCliente = string.Empty,
            TelCliente = string.Empty
        };

        var responseText = await PostJsonWithRetryAsync(
            settings, settings.Customer.SearchEndpoint, payload,
            "customer synchronization", ct);

        var result = JsonSerializer.Deserialize<MantisCustomerSearchResponse>(responseText, MantisJsonOptions)
            ?? new MantisCustomerSearchResponse();
        if (!string.IsNullOrWhiteSpace(result.ErrorKey))
            throw new InvalidOperationException($"Mantis customer synchronization failed: {result.ErrorKey}");

        var customers = result.SDTConsultarClientesCasalins
            .Where(customer => !string.IsNullOrWhiteSpace(customer.LlaveNit)
                && !string.IsNullOrWhiteSpace(customer.LlaveCliente))
            .Select(customer =>
            {
                var phone = Clean(customer.CelularCliente) ?? Clean(customer.TelefonoClientes);
                var normalized = NormalizeCustomerPhone(phone, settings.Customer);
                return normalized is null
                    ? null
                    : new ExternalCustomerIdentityReference(
                        customer.LlaveNit!.Trim(),
                        customer.LlaveCliente!.Trim(),
                        Clean(customer.NombreCliente),
                        normalized,
                        phone);
            })
            .OfType<ExternalCustomerIdentityReference>()
            .GroupBy(customer => $"{customer.ExternalAccountId}:{customer.ExternalCustomerId}", StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();

        var pagination = result.SDTPaginadoCasalins;
        var hasMore = bool.TryParse(pagination?.NextPage, out var next)
            ? next
            : result.SDTConsultarClientesCasalins.Count >= take;
        return new ExternalCustomerIdentityPage(customers, hasMore);
    }

    public async Task<IReadOnlyList<CommerceOrderHistoryRecord>> GetOrderHistoryAsync(
        CommerceAdapterContext context,
        CommerceOrderHistoryQuery query,
        CancellationToken ct = default)
    {
        var settings = MantisSettings.From(RequireConnection(context));
        var payload = new
        {
            NroPedido = Clean(query.ExternalOrderId) ?? string.Empty,
            IdeClientes = Clean(query.ExternalCustomerLookupId) ?? string.Empty,
            FechaInicial = query.From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty,
            FechaFinal = query.To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty
        };
        var responseText = await PostJsonWithRetryAsync(
            settings,
            settings.Order.QueryEndpoint,
            payload,
            "order history query",
            ct);
        var response = JsonSerializer.Deserialize<MantisOrderHistoryResponse>(
                responseText,
                MantisJsonOptions)
            ?? new MantisOrderHistoryResponse();
        if (!string.IsNullOrWhiteSpace(response.ErrorKey))
            throw new InvalidOperationException($"Mantis order history query failed: {response.ErrorKey}");

        return (response.SDTConsultarPedidoCasalins ?? [])
            .Select(MantisOrderHistoryMapper.ToRecord)
            .OfType<CommerceOrderHistoryRecord>()
            .ToList();
    }
    private async Task<MantisCustomerIdentity?> FetchCustomerIdentityAsync(
        MantisSettings settings,
        string normalizedPhone,
        CancellationToken ct)
    {
        var countryCode = new string(settings.Customer.CountryCode.Where(char.IsDigit).ToArray());
        var internationalPhone = countryCode.Length > 0
            && normalizedPhone.Length == settings.Customer.NationalPhoneLength
                ? countryCode + normalizedPhone
                : normalizedPhone;
        var attempts = new[] { normalizedPhone, internationalPhone }
            .Distinct(StringComparer.Ordinal)
            .SelectMany(phone => new[]
            {
                (Cell: phone, Telephone: string.Empty),
                (Cell: string.Empty, Telephone: phone)
            });

        foreach (var attempt in attempts)
        {
            var payload = new
            {
                Pagina = 1,
                CantPag = settings.Customer.LookupPageSize,
                IdeCliente = string.Empty,
                NomCliente = string.Empty,
                CelCliente = attempt.Cell,
                CiuCliente = string.Empty,
                ZonCliente = string.Empty,
                BarCliente = string.Empty,
                RutCliente = string.Empty,
                TelCliente = attempt.Telephone
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

            if (matches.Count == 1)
                return matches[0];
            if (matches.Count > 1)
                return null;
        }

        return null;
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
        var customer = ToMantisIdentity(ctx.Customer) ?? await ResolveCustomerIdentityAsync(
            settings,
            order.BusinessId,
            connection.IntegrationConnectionId,
            order.CustomerPhoneSnapshot ?? ctx.CustomerPhone,
            ct)
            ?? ToMantisIdentity(settings.GenericCustomer);
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
            LlaveNit = customer?.LlaveNit ?? string.Empty,
            LlaveCliente = customer?.LlaveCliente ?? string.Empty,
            Articulos = articles,
            bodega = Clean(ctx.WarehouseCode) ?? settings.Order.Warehouse,
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

    private async Task<string> PostJsonWithRetryAsync(
        MantisSettings settings,
        string endpoint,
        object payload,
        string operation,
        CancellationToken ct)
    {
        Exception? lastFailure = null;
        const int maxAttempts = 3;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
                using var request = CreateRequest(settings, endpoint, payload);
                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                var responseText = await response.Content.ReadAsStringAsync(timeout.Token);
                if (response.IsSuccessStatusCode)
                    return responseText;

                var status = (int)response.StatusCode;
                var failure = new HttpRequestException(
                    $"Mantis {operation} failed ({status}).",
                    null,
                    response.StatusCode);
                if (attempt == maxAttempts || !IsTransientStatus(status))
                    throw failure;
                lastFailure = failure;
            }
            catch (OperationCanceledException exception) when (!ct.IsCancellationRequested)
            {
                lastFailure = exception;
                if (attempt == maxAttempts)
                {
                    throw new TimeoutException(
                        $"Mantis {operation} timed out after {maxAttempts} attempts.",
                        exception);
                }
            }
            catch (HttpRequestException exception) when (
                attempt < maxAttempts
                && (!exception.StatusCode.HasValue
                    || IsTransientStatus((int)exception.StatusCode.Value)))
            {
                lastFailure = exception;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(500 * attempt), ct);
        }

        throw new HttpRequestException(
            $"Mantis {operation} failed after {maxAttempts} attempts.",
            lastFailure);
    }

    private static bool IsTransientStatus(int status) =>
        status is 408 or 429 || status >= 500;

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
        string warehouse,
        string creationDate = "",
        string modificationDate = "")
    {
        var query = request.Query?.Trim();
        var code = LooksLikeCode(query) ? query : string.Empty;
        var name = string.IsNullOrWhiteSpace(query) || LooksLikeCode(query) ? string.Empty : query;

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
            Bodega = warehouse,
            FechaCreacion = creationDate,
            FechaModificacion = modificationDate
        };
    }

    private async Task<ProductReference> AttachExistingSnapshotIdAsync(
        CommerceAdapterContext ctx,
        ProductReference reference,
        CancellationToken ct)
    {
        var products = _unitOfWork.Products;
        if (products is null || string.IsNullOrWhiteSpace(reference.ExternalProductId) || ctx.Connection is null)
            return reference;

        var existing = await products.GetByExternalIdAsync(
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
            return hasNextPage;

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