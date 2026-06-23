using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Promotions;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Commerce;

public sealed class CommerceService : ICommerceService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICommerceAdapterFactory _adapterFactory;
    private readonly IPromotionPricingService _promotions;

    public CommerceService(
        IUnitOfWork unitOfWork,
        ICommerceAdapterFactory adapterFactory,
        IPromotionPricingService promotions)
    {
        _unitOfWork = unitOfWork;
        _adapterFactory = adapterFactory;
        _promotions = promotions;
    }

    public async Task<ProductSearchResult> SearchProductsAsync(AgentToolContext ctx, ProductSearchRequest request, CancellationToken ct = default)
    {
        var adapterContext = await BuildContextAsync(ctx, ct);
        var adapter = _adapterFactory.Resolve(adapterContext.Provider);
        var result = await adapter.SearchProductsAsync(request, adapterContext, ct);
        return await EnrichProductPromotionsAsync(ctx.BusinessId, result, ct);
    }

    public async Task<OrderSnapshot> AddItemAsync(AgentToolContext ctx, AddOrderItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");

        var adapterContext = await BuildContextAsync(ctx, ct);
        var adapter = _adapterFactory.Resolve(adapterContext.Provider);
        var product = await adapter.GetProductAsync(request, adapterContext, ct)
            ?? await FindCachedProductAsync(request, adapterContext, ct)
            ?? throw new InvalidOperationException("Product not found.");

        var order = await GetOrCreateDraftAsync(ctx, adapterContext, ct);
        var unitPrice = request.UnitPrice ?? product.UnitPrice;
        var existingItems = await _unitOfWork.OrderItems.GetByOrderIdAsync(ctx.BusinessId, order.OrderId, ct);
        var existingItem = existingItems.FirstOrDefault(item => IsSameOrderProduct(item, product));
        if (existingItem is not null)
        {
            existingItem.Quantity += request.Quantity;
            existingItem.UnitPrice = unitPrice;
            existingItem.LineTotal = existingItem.Quantity * unitPrice;
            existingItem.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.OrderItems.UpdateAsync(existingItem, ct);

            order.Status = OrderStatus.Draft;
            order.UpdatedAt = DateTime.UtcNow;
            await RecalculateAsync(order, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return await BuildSnapshotAsync(order, ct);
        }

        var item = new OrderItem
        {
            OrderItemId = Guid.NewGuid(),
            OrderId = order.OrderId,
            BusinessId = ctx.BusinessId,
            ProductId = product.ProductId,
            IntegrationConnectionId = adapterContext.Connection?.IntegrationConnectionId,
            ExternalProductId = product.ExternalProductId,
            Sku = product.Sku,
            ProductNameSnapshot = product.Name,
            DescriptionSnapshot = product.Description,
            Quantity = request.Quantity,
            UnitPrice = unitPrice,
            DiscountAmount = 0,
            TaxAmount = 0,
            LineTotal = request.Quantity * unitPrice,
            RawPayloadJson = product.RawPayloadJson,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.OrderItems.CreateAsync(item, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        order.Status = OrderStatus.Draft;
        order.UpdatedAt = DateTime.UtcNow;
        await RecalculateAsync(order, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await BuildSnapshotAsync(order, ct);
    }

    private static bool IsSameOrderProduct(OrderItem item, ProductReference product)
    {
        if (product.ProductId.HasValue && item.ProductId == product.ProductId)
            return true;

        if (!string.IsNullOrWhiteSpace(product.ExternalProductId)
            && item.ExternalProductId?.Equals(product.ExternalProductId, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        if (!string.IsNullOrWhiteSpace(product.Sku)
            && item.Sku?.Equals(product.Sku, StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return CatalogSearchText.NormalizeCompact(item.ProductNameSnapshot)
               == CatalogSearchText.NormalizeCompact(product.Name);
    }

    private async Task<ProductReference?> FindCachedProductAsync(
        AddOrderItemRequest request,
        CommerceAdapterContext ctx,
        CancellationToken ct)
    {
        Product? product = null;

        if (request.ProductId.HasValue)
            product = await _unitOfWork.Products.GetByIdAsync(ctx.BusinessId, request.ProductId.Value, ct);

        if (product is null
            && !string.IsNullOrWhiteSpace(request.ExternalProductId)
            && ctx.Connection is not null)
        {
            product = await _unitOfWork.Products.GetByExternalIdAsync(
                ctx.BusinessId,
                ctx.Connection.IntegrationConnectionId,
                request.ExternalProductId,
                ct);
        }

        if (product is null && !string.IsNullOrWhiteSpace(request.Sku))
        {
            var matches = await _unitOfWork.Products.SearchAsync(ctx.BusinessId, request.Sku, null, 10, ct);
            product = matches.FirstOrDefault(p =>
                string.Equals(p.Sku, request.Sku, StringComparison.OrdinalIgnoreCase));
        }

        if (product is null && !string.IsNullOrWhiteSpace(request.Name))
        {
            var matches = await _unitOfWork.Products.SearchAsync(ctx.BusinessId, null, null, 50, ct);
            product = FindBestProductMatch(matches, request.Name);
        }

        return product is null ? null : MapCachedProduct(product);
    }

    private static ProductReference MapCachedProduct(Product product) =>
        new(
            product.ProductId,
            product.ExternalProductId,
            product.Sku,
            product.Name,
            product.Description,
            product.CategoryName,
            product.UnitPrice,
            product.Currency,
            product.StockQuantity,
            !product.ManageStock || (product.StockQuantity ?? 0) > 0,
            null,
            null,
            null,
            null,
            product.RawPayloadJson);

    private static Product? FindBestProductMatch(IReadOnlyList<Product> products, string input)
    {
        var normalizedInput = CatalogSearchText.NormalizeCompact(input);
        if (string.IsNullOrWhiteSpace(normalizedInput))
            return null;

        var exact = products.FirstOrDefault(p =>
            CatalogSearchText.NormalizeCompact(p.Name) == normalizedInput ||
            CatalogSearchText.NormalizeCompact(p.Sku) == normalizedInput);
        if (exact is not null)
            return exact;

        return products.FirstOrDefault(p =>
            CatalogSearchText.NormalizeCompact(p.Name).Contains(normalizedInput, StringComparison.Ordinal) ||
            normalizedInput.Contains(CatalogSearchText.NormalizeCompact(p.Name), StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(p.Sku) && CatalogSearchText.NormalizeCompact(p.Sku).Contains(normalizedInput, StringComparison.Ordinal)));
    }

    public async Task<OrderSnapshot> RemoveItemAsync(AgentToolContext ctx, Guid orderItemId, CancellationToken ct = default)
    {
        var order = await _unitOfWork.Orders.GetActiveDraftByConversationAsync(ctx.BusinessId, ctx.ConversationId, ct)
            ?? throw new InvalidOperationException("No active order draft found.");
        var item = await _unitOfWork.OrderItems.GetByIdAsync(ctx.BusinessId, orderItemId, ct)
            ?? throw new InvalidOperationException("Order item not found.");
        if (item.OrderId != order.OrderId)
            throw new InvalidOperationException("Order item does not belong to the active draft.");

        await _unitOfWork.OrderItems.DeleteAsync(item, ct);
        await RecalculateAsync(order, ct);
        order.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return await BuildSnapshotAsync(order, ct);
    }

    public async Task<OrderSnapshot> UpdateItemQuantityAsync(
        AgentToolContext ctx,
        Guid orderItemId,
        decimal quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return await RemoveItemAsync(ctx, orderItemId, ct);

        var order = await _unitOfWork.Orders.GetActiveDraftByConversationAsync(ctx.BusinessId, ctx.ConversationId, ct)
            ?? throw new InvalidOperationException("No active order draft found.");
        var item = await _unitOfWork.OrderItems.GetByIdAsync(ctx.BusinessId, orderItemId, ct)
            ?? throw new InvalidOperationException("Order item not found.");
        if (item.OrderId != order.OrderId)
            throw new InvalidOperationException("Order item does not belong to the active draft.");

        item.Quantity = quantity;
        item.LineTotal = quantity * item.UnitPrice;
        item.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.OrderItems.UpdateAsync(item, ct);

        await RecalculateAsync(order, ct);
        order.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return await BuildSnapshotAsync(order, ct);
    }

    public async Task<OrderSnapshot> GetDraftAsync(AgentToolContext ctx, CancellationToken ct = default)
    {
        var order = await _unitOfWork.Orders.GetActiveDraftByConversationAsync(ctx.BusinessId, ctx.ConversationId, ct)
            ?? await GetOrCreateDraftAsync(ctx, await BuildContextAsync(ctx, ct), ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await BuildSnapshotAsync(order, ct);
    }

    public async Task<OrderSnapshot> CreateOrderAsync(AgentToolContext ctx, CreateOrderRequest request, CancellationToken ct = default)
    {
        var adapterContext = await BuildContextAsync(ctx, ct);
        var order = await _unitOfWork.Orders.GetActiveDraftByConversationAsync(ctx.BusinessId, ctx.ConversationId, ct)
            ?? throw new InvalidOperationException("No active order draft found.");
        var items = await _unitOfWork.OrderItems.GetByOrderIdAsync(ctx.BusinessId, order.OrderId, ct);
        if (items.Count == 0)
            throw new InvalidOperationException("Order has no items.");

        order.CustomerNameSnapshot = Coalesce(request.CustomerName, order.CustomerNameSnapshot, ctx.Conversation.CustomerName);
        order.CustomerEmailSnapshot = Coalesce(request.CustomerEmail, order.CustomerEmailSnapshot, ctx.Conversation.CustomerEmail);
        order.CustomerPhoneSnapshot = Coalesce(request.CustomerPhone, order.CustomerPhoneSnapshot, ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone));
        order.CustomerDocumentSnapshot = Coalesce(request.CustomerDocument, order.CustomerDocumentSnapshot, GetFact(ctx, "customer_document"));
        order.DeliveryAddressSnapshot = Coalesce(request.DeliveryAddress, order.DeliveryAddressSnapshot, GetFact(ctx, "delivery_address"));
        order.Notes = Coalesce(request.Notes, order.Notes, null);
        order.CustomerConfirmed = request.CustomerConfirmed;

        if (!request.CustomerConfirmed)
        {
            order.Status = OrderStatus.PendingConfirmation;
            order.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Orders.UpdateAsync(order, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return await BuildSnapshotAsync(order, ct);
        }

        if (!string.IsNullOrWhiteSpace(order.ExternalOrderId))
            return await BuildSnapshotAsync(order, ct);

        order.Status = adapterContext.Provider == CommerceProvider.Local ? OrderStatus.Confirmed : OrderStatus.SyncPending;
        order.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Orders.UpdateAsync(order, ct);

        if (adapterContext.Provider != CommerceProvider.Local)
        {
            var adapter = _adapterFactory.Resolve(adapterContext.Provider);
            var evt = await GetOrCreateEventAsync(order, adapterContext, ct);
            try
            {
                var result = await adapter.CreateOrderAsync(order, items, adapterContext, ct);
                order.ExternalOrderId = result.ExternalOrderId;
                order.ExternalDocumentNumber = result.ExternalDocumentNumber;
                order.ExternalStatus = result.ExternalStatus;
                order.Status = OrderStatus.Synced;
                evt.ExternalEventId = result.ExternalOrderId;
                evt.Status = IntegrationEventStatus.Synced;
                evt.ResponseJson = result.ResponseJson;
                evt.LastError = null;
                evt.UpdatedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                order.Status = OrderStatus.SyncFailed;
                evt.Status = IntegrationEventStatus.Failed;
                evt.LastError = ex.Message;
                evt.UpdatedAt = DateTime.UtcNow;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return await BuildSnapshotAsync(order, ct);
    }


    private static void ApplyOrderCheckoutMetadata(AgentToolContext ctx, Order order)
    {
        var checkoutMode = ctx.Config?.Checkout.ResolveMode("order");
        var shipping = checkoutMode?.Shipping ?? new MimosBabySpa.Application.Agents.Configuration.OrderCheckoutShippingDefinition();
        var city = GetFact(ctx, "city");
        var shippingCost = ResolveShippingCost(shipping, city);
        var currency = ctx.Config?.Checkout.Currency;

        if (!string.IsNullOrWhiteSpace(currency))
            order.Currency = currency.Trim().ToUpperInvariant();

        order.Total = order.Subtotal - order.DiscountTotal + order.TaxTotal + shippingCost;
        order.CustomAttributesJson = BuildOrderCustomAttributes(ctx.Facts, city, shippingCost);
    }

    private static decimal ResolveShippingCost(MimosBabySpa.Application.Agents.Configuration.OrderCheckoutShippingDefinition shipping, string? city)
    {
        if (!shipping.Enabled)
            return 0m;

        if (!string.IsNullOrWhiteSpace(city)
            && !string.IsNullOrWhiteSpace(shipping.LocalCity)
            && city.Trim().Equals(shipping.LocalCity.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return shipping.LocalCost;
        }

        return shipping.NationalCost;
    }

    private static string BuildOrderCustomAttributes(
        IReadOnlyDictionary<string, string> facts,
        string? city,
        decimal shippingCost)
    {
        var custom = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["city"] = city,
            ["shipping_cost"] = shippingCost,
            ["facts"] = facts
        };
        return JsonSerializer.Serialize(custom);
    }

    private async Task<CommerceAdapterContext> BuildContextAsync(AgentToolContext ctx, CancellationToken ct)
    {
        var provider = ctx.Config?.Commerce.Provider ?? CommerceProvider.Local;
        if (ctx.Config?.Commerce.Enabled != true)
            provider = CommerceProvider.Local;

        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            ctx.BusinessId,
            provider,
            CommerceCapability.CatalogAndOrders,
            ct);

        if (provider != CommerceProvider.Local && connection is null)
            throw new InvalidOperationException($"Commerce connection '{provider}' is not configured for this business.");
        if (connection is not null && !connection.IsEnabled)
            throw new InvalidOperationException($"Commerce connection '{provider}' is disabled.");

        return new CommerceAdapterContext(ctx.BusinessId, ctx.AgentId, ctx.ConversationId, provider, connection);
    }

    private async Task<Order> GetOrCreateDraftAsync(AgentToolContext ctx, CommerceAdapterContext adapterContext, CancellationToken ct)
    {
        var existing = await _unitOfWork.Orders.GetActiveDraftByConversationAsync(ctx.BusinessId, ctx.ConversationId, ct);
        if (existing is not null)
            return existing;

        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            BusinessId = ctx.BusinessId,
            AgentId = ctx.AgentId,
            ConversationId = ctx.ConversationId,
            IntegrationConnectionId = adapterContext.Connection?.IntegrationConnectionId,
            Source = OrderSource.Bot,
            FulfillmentMode = adapterContext.Provider == CommerceProvider.Local ? OrderFulfillmentMode.Local : OrderFulfillmentMode.External,
            Status = OrderStatus.Draft,
            Currency = GetConnectionCurrency(adapterContext.Connection) ?? "COP",
            IdempotencyKey = $"{ctx.BusinessId:N}:{ctx.ConversationId:N}:{DateTime.UtcNow:yyyyMMddHHmmss}",
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Orders.CreateAsync(order, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return order;
    }

    private async Task RecalculateAsync(Order order, CancellationToken ct)
    {
        var items = await _unitOfWork.OrderItems.GetByOrderIdAsync(order.BusinessId, order.OrderId, ct);
        var pricing = await _promotions.EvaluateAsync(
            order.BusinessId,
            items.Select(i => new PromotionPricingItem(
                i.OrderItemId.ToString("N"),
                PromotionItemType.Product,
                i.ProductId,
                null,
                i.ProductNameSnapshot,
                null,
                i.UnitPrice,
                i.Quantity)).ToList(),
            ct: ct);

        foreach (var item in items)
        {
            var priced = pricing.Items.FirstOrDefault(i => i.Item.Key == item.OrderItemId.ToString("N"));
            if (priced is null)
                continue;

            item.LineTotal = priced.LineSubtotal;
            item.DiscountAmount = priced.DiscountAmount;
            item.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.OrderItems.UpdateAsync(item, ct);
        }

        order.Subtotal = pricing.Subtotal;
        order.DiscountTotal = pricing.DiscountTotal;
        order.TaxTotal = items.Sum(i => i.TaxAmount);
        order.Total = order.Subtotal - order.DiscountTotal + order.TaxTotal;
        await _unitOfWork.Orders.UpdateAsync(order, ct);
    }

    private async Task<ProductSearchResult> EnrichProductPromotionsAsync(
        Guid businessId,
        ProductSearchResult result,
        CancellationToken ct)
    {
        if (result.Products.Count == 0)
            return result;

        var pricing = await _promotions.EvaluateAsync(
            businessId,
            result.Products.Select((p, index) => new PromotionPricingItem(
                index.ToString(CultureInfo.InvariantCulture),
                PromotionItemType.Product,
                p.ProductId,
                null,
                p.Name,
                p.CategoryName,
                p.UnitPrice,
                1)).ToList(),
            ct: ct);

        var products = result.Products.Select((product, index) =>
        {
            var priced = pricing.Items.FirstOrDefault(i => i.Item.Key == index.ToString(CultureInfo.InvariantCulture));
            return priced is null || !priced.HasPromotion
                ? product
                : product with
                {
                    EffectiveUnitPrice = priced.EffectiveUnitPrice,
                    DiscountAmount = priced.DiscountAmount,
                    PromotionName = priced.PromotionName,
                    PromotionSummary = priced.PromotionSummary
                };
        }).ToList();

        return result with { Products = products };
    }

    private async Task<OrderConnectionEvent> GetOrCreateEventAsync(Order order, CommerceAdapterContext ctx, CancellationToken ct)
    {
        var connection = ctx.Connection ?? throw new InvalidOperationException("External order requires a connection.");
        var existing = await _unitOfWork.OrderConnectionEvents.GetByOrderConnectionAsync(order.OrderId, connection.IntegrationConnectionId, ct);
        if (existing is not null)
            return existing;

        var evt = new OrderConnectionEvent
        {
            OrderConnectionEventId = Guid.NewGuid(),
            BusinessId = order.BusinessId,
            OrderId = order.OrderId,
            IntegrationConnectionId = connection.IntegrationConnectionId,
            ConnectionType = ConnectionType.Commerce,
            Provider = (int)ctx.Provider,
            Capability = (int)CommerceCapability.CatalogAndOrders,
            Status = IntegrationEventStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.OrderConnectionEvents.CreateAsync(evt, ct);
        return evt;
    }

    private async Task<OrderSnapshot> BuildSnapshotAsync(Order order, CancellationToken ct)
    {
        var items = await _unitOfWork.OrderItems.GetByOrderIdAsync(order.BusinessId, order.OrderId, ct);
        return new OrderSnapshot(
            order.OrderId,
            order.Status,
            order.Currency,
            order.Subtotal,
            order.DiscountTotal,
            order.TaxTotal,
            order.Total,
            items.Select(i => new OrderItemSnapshot(
                i.OrderItemId,
                i.ProductId,
                i.ExternalProductId,
                i.Sku,
                i.ProductNameSnapshot,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal)).ToList(),
            order.PaymentTransactionId,
            order.ExternalOrderId,
            order.ExternalDocumentNumber,
            order.ExternalStatus);
    }

    private static string? GetConnectionCurrency(IntegrationConnection? connection)
    {
        if (connection is null || string.IsNullOrWhiteSpace(connection.SettingsJson))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(connection.SettingsJson);
            if (doc.RootElement.TryGetProperty("order", out var order)
                && order.TryGetProperty("defaultCurrencyCode", out var currency)
                && currency.ValueKind == JsonValueKind.String)
            {
                return currency.GetString();
            }
        }
        catch
        {
            return null;
        }
        return null;
    }

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string? GetFact(AgentToolContext ctx, string key) =>
        ctx.Facts.TryGetValue(key, out var value) ? value : null;
}





