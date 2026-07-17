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

public sealed class CommerceService : ICommerceService, IProductLookupService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICommerceAdapterFactory _adapterFactory;
    private readonly IPromotionPricingService _promotions;
    private readonly IProductCatalogAvailabilityService _availability;
    private readonly IProductCandidateRetriever? _candidateRetriever;

    public CommerceService(
        IUnitOfWork unitOfWork,
        ICommerceAdapterFactory adapterFactory,
        IPromotionPricingService promotions,
        IProductCatalogAvailabilityService availability,
        IProductCandidateRetriever? candidateRetriever = null)
    {
        _unitOfWork = unitOfWork;
        _adapterFactory = adapterFactory;
        _promotions = promotions;
        _availability = availability;
        _candidateRetriever = candidateRetriever;
    }

    public async Task<ProductSearchResult> SearchProductsAsync(AgentConversationContext ctx, ProductSearchRequest request, CancellationToken ct = default)
    {
        var adapterContext = await BuildContextAsync(ctx.BusinessId, ctx.AgentId, ctx.ConversationId, ctx.Config, ctx.ChannelPhone, ctx.CommerceCustomer, ctx.RecipientPhoneNumberId, ct);
        var adapter = _adapterFactory.Resolve(adapterContext.Provider);
        ProductSearchResult result;
        if (adapterContext.Provider == CommerceProvider.Mantis)
        {
            IReadOnlyList<ProductReference> local;
            if (!string.IsNullOrWhiteSpace(request.Query) && _candidateRetriever is not null)
            {
                local = (await _candidateRetriever.RetrieveAsync(ctx, request.Query, ct))
                    .GroupBy(candidate => candidate.Product.ProductId?.ToString("N")
                        ?? candidate.Product.ExternalProductId
                        ?? candidate.Product.Sku
                        ?? candidate.Product.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group
                        .OrderByDescending(candidate => candidate.ExactAlias)
                        .ThenByDescending(candidate => candidate.CanAutoResolve)
                        .First().Product)
                    .OrderByDescending(product => ProductResolutionEngine.Score(request.Query, product))
                    .Take(Math.Clamp(request.Limit, 1, 50))
                    .ToList();
            }
            else
            {
                local = (await _unitOfWork.Products.GetIdentityCatalogAsync(ctx.BusinessId, ct))
                    .Where(product => product.IsActive)
                    .Take(Math.Clamp(request.Limit, 1, 50))
                    .Select(ToProductReference)
                    .ToList();
            }

            var quoted = new List<ProductReference>();
            foreach (var identity in local)
            {
                var live = await adapter.GetProductAsync(
                    new AddOrderItemRequest(
                        identity.ProductId,
                        identity.ExternalProductId,
                        identity.Sku,
                        identity.Name,
                        1m,
                        null),
                    adapterContext,
                    ct);
                if (live is null || !_availability.IsSellable(live))
                    continue;
                if (!MatchesSearchFilters(live, request))
                    continue;
                quoted.Add(!live.ProductId.HasValue && identity.ProductId.HasValue
                    ? live with { ProductId = identity.ProductId }
                    : live);
            }
            result = new ProductSearchResult(quoted, "local-identity+mantis-live");
        }
        else
        {
            result = _availability.FilterSellable(
                await adapter.SearchProductsAsync(request, adapterContext, ct));
            if (result.Products.Count == 0)
            {
                foreach (var fallbackQuery in CatalogSearchText.GetFallbackQueries(request.Query))
                {
                    var fallbackResult = _availability.FilterSellable(
                        await adapter.SearchProductsAsync(request with { Query = fallbackQuery }, adapterContext, ct));
                    if (fallbackResult.Products.Count == 0)
                        continue;
                    result = fallbackResult;
                    break;
                }
            }
        }

        result = result with { AppliedFilters = ProductSearchAppliedFilters.From(request) };
        return await EnrichProductPromotionsAsync(ctx.BusinessId, result, ct);
    }

    public async Task<ProductReference?> GetProductAsync(
        AgentConversationContext ctx,
        ProductLookupRequest request,
        CancellationToken ct = default)
    {
        var adapterContext = await BuildContextAsync(ctx.BusinessId, ctx.AgentId, ctx.ConversationId, ctx.Config, ctx.ChannelPhone, ctx.CommerceCustomer, ctx.RecipientPhoneNumberId, ct);
        var adapter = _adapterFactory.Resolve(adapterContext.Provider);
        var lookup = new AddOrderItemRequest(
            request.ProductId,
            request.ExternalProductId,
            request.Sku,
            request.Name,
            1m,
            null);
        var product = await adapter.GetProductAsync(lookup, adapterContext, ct);
        if (product is null
            && adapterContext.Provider != CommerceProvider.Mantis
            && !string.IsNullOrWhiteSpace(request.SearchText))
        {
            var candidates = _availability.FilterSellable(await adapter.SearchProductsAsync(
                new ProductSearchRequest(request.SearchText, null, 10), adapterContext, ct));
            product = candidates.Products.FirstOrDefault(candidate => MatchesLookupIdentity(candidate, request));
        }
        if (product is null && adapterContext.Provider != CommerceProvider.Mantis)
            product = await FindCachedProductAsync(lookup, adapterContext, ct);
        if (product is null || !_availability.IsSellable(product))
            return null;

        var result = await EnrichProductPromotionsAsync(
            ctx.BusinessId,
            new ProductSearchResult([product], adapterContext.Provider.ToString().ToLowerInvariant()),
            ct);
        return result.Products.FirstOrDefault();
    }

    private static ProductReference ToProductReference(Product product) =>
        new(
            product.ProductId,
            product.ExternalProductId,
            product.Sku,
            product.Name,
            product.Description,
            product.CategoryName,
            0m,
            product.Currency,
            null)
        { IsActive = product.IsActive };

    private static bool MatchesSearchFilters(ProductReference product, ProductSearchRequest request) =>
        MatchesFilter(product.CategoryName, request.Category)
        && MatchesFilter(product.FamilyName, request.Family)
        && MatchesFilter(product.SubcategoryName, request.Subcategory)
        && MatchesFilter(product.ProductClassName, request.ProductClass);

    private static bool MatchesFilter(string? value, string? filter) =>
        string.IsNullOrWhiteSpace(filter)
        || !string.IsNullOrWhiteSpace(value)
            && ProductSearchText.NormalizeWords(value)
                .Contains(ProductSearchText.NormalizeWords(filter), StringComparison.Ordinal);

    private static bool MatchesLookupIdentity(ProductReference product, ProductLookupRequest request)
    {
        if (request.ProductId.HasValue && product.ProductId == request.ProductId)
            return true;
        if (!string.IsNullOrWhiteSpace(request.ExternalProductId)
            && string.Equals(product.ExternalProductId, request.ExternalProductId, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!string.IsNullOrWhiteSpace(request.Sku)
            && string.Equals(product.Sku, request.Sku, StringComparison.OrdinalIgnoreCase))
            return true;
        return !string.IsNullOrWhiteSpace(request.Name)
               && string.Equals(product.Name, request.Name, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<OrderSnapshot> AddItemAsync(AgentConversationContext ctx, AddOrderItemRequest request, CancellationToken ct = default)
    {
        if (request.Quantity <= 0)
            throw new InvalidOperationException("Quantity must be greater than zero.");

        var adapterContext = await BuildContextAsync(ctx.BusinessId, ctx.AgentId, ctx.ConversationId, ctx.Config, ctx.ChannelPhone, ctx.CommerceCustomer, ctx.RecipientPhoneNumberId, ct);
        var adapter = _adapterFactory.Resolve(adapterContext.Provider);
        var product = await adapter.GetProductAsync(request, adapterContext, ct);
        if (product is null && adapterContext.Provider != CommerceProvider.Mantis)
            product = await FindCachedProductAsync(request, adapterContext, ct);
        if (product is null)
            throw new InvalidOperationException("Product not found.");

        if (!_availability.IsSellable(product))
            throw new InvalidOperationException("Product inactive.");

        var draft = await GetOrCreateDraftAsync(ctx, adapterContext, ct);
        var unitPrice = adapterContext.Provider == CommerceProvider.Mantis
            ? product.UnitPrice
            : request.UnitPrice ?? product.UnitPrice;
        var existingItems = await _unitOfWork.OrderDraftItems.GetByDraftIdAsync(ctx.BusinessId, draft.OrderDraftId, ct);
        var existingItem = existingItems.FirstOrDefault(item => IsSameOrderProduct(item, product));
        EnsureRequestedQuantityFitsStock(product, request.Quantity, existingItem?.Quantity ?? 0m);
        if (existingItem is not null)
        {
            existingItem.Quantity += request.Quantity;
            existingItem.UnitPrice = unitPrice;
            existingItem.LineTotal = existingItem.Quantity * unitPrice;
            existingItem.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.OrderDraftItems.UpdateAsync(existingItem, ct);

            draft.UpdatedAt = DateTime.UtcNow;
            await RecalculateAsync(draft, ct);
            await _unitOfWork.SaveChangesAsync(ct);

            return await BuildSnapshotAsync(draft, ct);
        }

        var item = new OrderDraftItem
        {
            OrderDraftItemId = Guid.NewGuid(),
            OrderDraftId = draft.OrderDraftId,
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

        await _unitOfWork.OrderDraftItems.CreateAsync(item, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        draft.UpdatedAt = DateTime.UtcNow;
        await RecalculateAsync(draft, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        return await BuildSnapshotAsync(draft, ct);
    }

    public async Task<OrderSnapshot> RemoveItemAsync(AgentConversationContext ctx, Guid orderItemId, CancellationToken ct = default)
    {
        var draft = await GetActiveDraftAsync(ctx, ct)
            ?? throw new InvalidOperationException("No active order draft found.");
        var item = await _unitOfWork.OrderDraftItems.GetByIdAsync(ctx.BusinessId, orderItemId, ct)
            ?? throw new InvalidOperationException("Order item not found.");
        if (item.OrderDraftId != draft.OrderDraftId)
            throw new InvalidOperationException("Order item does not belong to the active draft.");

        await _unitOfWork.OrderDraftItems.DeleteAsync(item, ct);
        await _unitOfWork.SaveChangesAsync(ct);

        await RecalculateAsync(draft, ct);
        draft.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return await BuildSnapshotAsync(draft, ct);
    }

    public async Task<OrderSnapshot> UpdateItemQuantityAsync(
        AgentConversationContext ctx,
        Guid orderItemId,
        decimal quantity,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return await RemoveItemAsync(ctx, orderItemId, ct);

        var draft = await GetActiveDraftAsync(ctx, ct)
            ?? throw new InvalidOperationException("No active order draft found.");
        var item = await _unitOfWork.OrderDraftItems.GetByIdAsync(ctx.BusinessId, orderItemId, ct)
            ?? throw new InvalidOperationException("Order item not found.");
        if (item.OrderDraftId != draft.OrderDraftId)
            throw new InvalidOperationException("Order item does not belong to the active draft.");

        if (quantity > item.Quantity)
        {
            var adapterContext = await BuildContextAsync(ctx.BusinessId, ctx.AgentId, ctx.ConversationId, ctx.Config, ctx.ChannelPhone, ctx.CommerceCustomer, ctx.RecipientPhoneNumberId, ct);
            var adapter = _adapterFactory.Resolve(adapterContext.Provider);
            var lookup = new AddOrderItemRequest(
                item.ProductId,
                item.ExternalProductId,
                item.Sku,
                item.ProductNameSnapshot,
                quantity,
                item.UnitPrice);
            var product = await adapter.GetProductAsync(lookup, adapterContext, ct)
                ?? await FindCachedProductAsync(lookup, adapterContext, ct)
                ?? throw new InvalidOperationException("Product not found.");
            if (!_availability.IsSellable(product))
                throw new InvalidOperationException("Product inactive.");
            EnsureRequestedQuantityFitsStock(product, quantity, 0m);
        }

        item.Quantity = quantity;
        item.LineTotal = quantity * item.UnitPrice;
        item.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.OrderDraftItems.UpdateAsync(item, ct);

        await RecalculateAsync(draft, ct);
        draft.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(ct);

        return await BuildSnapshotAsync(draft, ct);
    }

    public async Task<OrderSnapshot> GetDraftAsync(AgentConversationContext ctx, CancellationToken ct = default)
    {
        var draft = await GetActiveDraftAsync(ctx, ct);
        return draft is null ? EmptyDraftSnapshot() : await BuildSnapshotAsync(draft, ct);
    }

    public async Task<int> DiscardDraftsAsync(
        Guid businessId,
        Guid conversationId,
        CancellationToken ct = default)
    {
        var drafts = await _unitOfWork.OrderDrafts.GetActiveDraftsByConversationAsync(
            businessId, conversationId, ct);
        foreach (var draft in drafts)
            await _unitOfWork.OrderDrafts.DeleteAsync(draft, ct);

        if (drafts.Count > 0)
            await _unitOfWork.SaveChangesAsync(ct);
        return drafts.Count;
    }

    public async Task<OrderSnapshot> CreateOrderAsync(AgentConversationContext ctx, CreateOrderRequest request, CancellationToken ct = default)
    {
        var draft = await GetActiveDraftAsync(ctx, ct)
            ?? throw new InvalidOperationException("No active order draft found.");

        ApplyCustomerData(ctx, draft, request);
        if (!request.CustomerConfirmed)
        {
            draft.CustomerConfirmed = false;
            draft.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.OrderDrafts.UpdateAsync(draft, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return await BuildSnapshotAsync(draft, ct);
        }

        var adapterContext = await BuildContextAsync(ctx.BusinessId, ctx.AgentId, ctx.ConversationId, ctx.Config, draft.CustomerPhoneSnapshot, ctx.CommerceCustomer, ctx.RecipientPhoneNumberId, ct);
        adapterContext = adapterContext with { WarehouseCode = draft.CommerceWarehouseCode ?? adapterContext.WarehouseCode };
        var order = await ConvertDraftToOrderAsync(draft, adapterContext, customerConfirmed: true, ct);
        await SyncExternalOrderIfNeededAsync(order, adapterContext, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await BuildSnapshotAsync(order, ct);
    }

    public async Task<OrderSnapshot> ConfirmPaidOrderAsync(Guid businessId, Guid paymentTransactionId, AgentConfig config, CancellationToken ct = default)
    {
        var existing = await _unitOfWork.Orders.GetByPaymentTransactionIdAsync(businessId, paymentTransactionId, ct);
        if (existing is not null)
        {
            existing.CustomerConfirmed = true;
            if (existing.Status is OrderStatus.PendingConfirmation or OrderStatus.AwaitingPayment)
                existing.Status = OrderStatus.Confirmed;
            existing.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.Orders.UpdateAsync(existing, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return await BuildSnapshotAsync(existing, ct);
        }

        var draft = await _unitOfWork.OrderDrafts.GetByPaymentTransactionIdAsync(businessId, paymentTransactionId, ct)
            ?? throw new InvalidOperationException("Order draft not found for paid checkout.");
        var adapterContext = await BuildContextAsync(draft.BusinessId, draft.AgentId, draft.ConversationId, config, draft.CustomerPhoneSnapshot, null, null, ct);
        adapterContext = adapterContext with { WarehouseCode = draft.CommerceWarehouseCode };
        var order = await ConvertDraftToOrderAsync(draft, adapterContext, customerConfirmed: true, ct);
        order.PaymentTransactionId = paymentTransactionId;
        order.Status = OrderStatus.Confirmed;
        await SyncExternalOrderIfNeededAsync(order, adapterContext, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return await BuildSnapshotAsync(order, ct);
    }

    private async Task<ProductReference?> FindCachedProductAsync(
        AddOrderItemRequest request,
        CommerceAdapterContext ctx,
        CancellationToken ct)
    {
        Product? product = null;

        if (request.ProductId.HasValue)
            product = await _unitOfWork.Products.GetByIdAsync(ctx.BusinessId, request.ProductId.Value, ct);

        if (product is null && !string.IsNullOrWhiteSpace(request.ExternalProductId) && ctx.Connection is not null)
            product = await _unitOfWork.Products.GetByExternalIdAsync(ctx.BusinessId, ctx.Connection.IntegrationConnectionId, request.ExternalProductId, ct);

        if (product is null && !string.IsNullOrWhiteSpace(request.Sku))
            product = await FindCachedProductBySkuAsync(ctx.BusinessId, ctx.Connection?.IntegrationConnectionId, request.Sku, ct);

        if (product is null && !string.IsNullOrWhiteSpace(request.Name))
            product = await FindCachedProductByNameAsync(ctx.BusinessId, request.Name, ct);

        return product is null
            ? null
            : new ProductReference(
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
            { IsActive = product.IsActive };
    }

    private async Task<Product?> FindCachedProductBySkuAsync(Guid businessId, Guid? connectionId, string sku, CancellationToken ct)
    {
        var products = await _unitOfWork.Products.SearchAsync(businessId, sku, null, 50, ct);
        return products.FirstOrDefault(p =>
            p.IsActive &&
            (!connectionId.HasValue || p.IntegrationConnectionId == connectionId) &&
            !string.IsNullOrWhiteSpace(p.Sku) &&
            p.Sku.Equals(sku, StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Product?> FindCachedProductByNameAsync(Guid businessId, string name, CancellationToken ct)
    {
        var products = await _unitOfWork.Products.SearchAsync(businessId, name, null, 50, ct);
        var normalizedInput = CatalogSearchText.NormalizeCompact(name);
        var active = products.Where(_availability.IsSellable).ToList();
        var exact = active.FirstOrDefault(p => CatalogSearchText.NormalizeCompact(p.Name) == normalizedInput);
        if (exact is not null)
            return exact;

        return active.FirstOrDefault(p =>
            CatalogSearchText.NormalizeCompact(p.Name).Contains(normalizedInput, StringComparison.Ordinal) ||
            normalizedInput.Contains(CatalogSearchText.NormalizeCompact(p.Name), StringComparison.Ordinal) ||
            (!string.IsNullOrWhiteSpace(p.Sku) && CatalogSearchText.NormalizeCompact(p.Sku).Contains(normalizedInput, StringComparison.Ordinal)));
    }

    private static void EnsureRequestedQuantityFitsStock(ProductReference product, decimal additionalQuantity, decimal existingCartQuantity)
    {
        if (!product.StockQuantity.HasValue)
            return;

        var requestedTotal = existingCartQuantity + additionalQuantity;
        if (requestedTotal <= product.StockQuantity.Value)
            return;

        throw new InsufficientProductStockException(
            product.Name,
            requestedTotal,
            product.StockQuantity.Value,
            existingCartQuantity);
    }

    private static bool IsSameOrderProduct(OrderDraftItem item, ProductReference product)
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

    private async Task<CommerceAdapterContext> BuildContextAsync(
        Guid businessId,
        Guid? agentId,
        Guid conversationId,
        AgentConfig? config,
        string? customerPhone,
        CommerceCustomerReference? customer,
        string? recipientPhoneNumberId,
        CancellationToken ct)
    {
        var provider = config?.Commerce.Provider ?? CommerceProvider.Local;
        if (config?.Commerce.Enabled != true)
            provider = CommerceProvider.Local;

        var connection = await _unitOfWork.IntegrationConnections.GetCommerceConnectionAsync(
            businessId,
            provider,
            CommerceCapability.CatalogAndOrders,
            ct);

        if (provider != CommerceProvider.Local && connection is null)
            throw new InvalidOperationException($"Commerce connection '{provider}' is not configured for this business.");
        if (connection is not null && !connection.IsEnabled)
            throw new InvalidOperationException($"Commerce connection '{provider}' is disabled.");

        return new CommerceAdapterContext(
            businessId,
            agentId ?? Guid.Empty,
            conversationId,
            provider,
            connection,
            customerPhone,
            customer,
            await ResolveWarehouseCodeAsync(businessId, provider, connection, recipientPhoneNumberId, ct));
    }

    private async Task<string?> ResolveWarehouseCodeAsync(
        Guid businessId,
        CommerceProvider provider,
        IntegrationConnection? connection,
        string? recipientPhoneNumberId,
        CancellationToken ct)
    {
        if (provider != CommerceProvider.Mantis)
            return null;

        if (connection is null)
            throw new InvalidOperationException("The Mantis commerce connection is not configured.");
        if (string.IsNullOrWhiteSpace(recipientPhoneNumberId))
        {
            throw new InvalidOperationException(
                "The receiving WhatsApp number is required to resolve the Mantis warehouse.");
        }

        var number = await _unitOfWork.BusinessWhatsAppNumbers.GetByWhatsAppPhoneNumberIdAsync(
            recipientPhoneNumberId.Trim());
        if (number is null || number.BusinessId != businessId)
        {
            throw new InvalidOperationException(
                "The receiving WhatsApp number is not configured for this business.");
        }

        var mapping = await _unitOfWork.IntegrationConnections.GetChannelWarehouseAsync(
            businessId,
            connection.IntegrationConnectionId,
            number.BusinessWhatsAppNumberId,
            ct);
        var warehouse = mapping?.WarehouseCode?.Trim();
        if (mapping?.IsActive != true || string.IsNullOrWhiteSpace(warehouse))
        {
            throw new InvalidOperationException(
                "The receiving WhatsApp number does not have an active Mantis warehouse configured in the integration.");
        }

        return warehouse;
    }

    private async Task<OrderDraft?> GetActiveDraftAsync(AgentConversationContext ctx, CancellationToken ct)
    {
        var activeDrafts = await _unitOfWork.OrderDrafts.GetActiveDraftsByConversationAsync(
            ctx.BusinessId,
            ctx.ConversationId,
            ct);

        var current = activeDrafts.FirstOrDefault();
        if (activeDrafts.Count <= 1)
            return current;

        foreach (var stale in activeDrafts.Skip(1))
        {
            await _unitOfWork.OrderDrafts.DeleteAsync(stale, ct);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return current;
    }

    private async Task<OrderDraft> GetOrCreateDraftAsync(AgentConversationContext ctx, CommerceAdapterContext adapterContext, CancellationToken ct)
    {
        var existing = await GetActiveDraftAsync(ctx, ct);
        if (existing is not null)
        {
            if (adapterContext.Provider == CommerceProvider.Mantis
                && !string.IsNullOrWhiteSpace(adapterContext.WarehouseCode))
            {
                if (string.IsNullOrWhiteSpace(existing.CommerceWarehouseCode))
                {
                    existing.CommerceWarehouseCode = adapterContext.WarehouseCode;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.OrderDrafts.UpdateAsync(existing, ct);
                    await _unitOfWork.SaveChangesAsync(ct);
                }
                else if (!existing.CommerceWarehouseCode.Equals(adapterContext.WarehouseCode, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The active cart belongs to a different Mantis warehouse.");
            }
            return existing;
        }

        var draft = new OrderDraft
        {
            OrderDraftId = Guid.NewGuid(),
            BusinessId = ctx.BusinessId,
            AgentId = ctx.AgentId,
            ConversationId = ctx.ConversationId,
            IntegrationConnectionId = adapterContext.Connection?.IntegrationConnectionId,
            Source = OrderSource.Bot,
            CommerceWarehouseCode = adapterContext.WarehouseCode,
            FulfillmentMode = adapterContext.Provider == CommerceProvider.Local ? OrderFulfillmentMode.Local : OrderFulfillmentMode.External,
            Currency = GetConnectionCurrency(adapterContext.Connection) ?? "COP",
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.OrderDrafts.CreateAsync(draft, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return draft;
    }

    private async Task RecalculateAsync(OrderDraft draft, CancellationToken ct)
    {
        var items = await _unitOfWork.OrderDraftItems.GetByDraftIdAsync(draft.BusinessId, draft.OrderDraftId, ct);
        var pricing = await _promotions.EvaluateAsync(
            draft.BusinessId,
            items.Select(i => new PromotionPricingItem(
                i.OrderDraftItemId.ToString("N"),
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
            var priced = pricing.Items.FirstOrDefault(i => i.Item.Key == item.OrderDraftItemId.ToString("N"));
            if (priced is null)
                continue;

            item.LineTotal = priced.LineSubtotal;
            item.DiscountAmount = priced.DiscountAmount;
            item.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.OrderDraftItems.UpdateAsync(item, ct);
        }

        draft.Subtotal = pricing.Subtotal;
        draft.DiscountTotal = pricing.DiscountTotal;
        draft.TaxTotal = items.Sum(i => i.TaxAmount);
        draft.Total = draft.Subtotal - draft.DiscountTotal + draft.TaxTotal;
        await _unitOfWork.OrderDrafts.UpdateAsync(draft, ct);
    }

    private async Task<ProductSearchResult> EnrichProductPromotionsAsync(Guid businessId, ProductSearchResult result, CancellationToken ct)
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

    private void ApplyCustomerData(AgentConversationContext ctx, OrderDraft draft, CreateOrderRequest request)
    {
        draft.CustomerNameSnapshot = Coalesce(request.CustomerName, draft.CustomerNameSnapshot, ctx.Conversation.CustomerName);
        draft.CustomerEmailSnapshot = Coalesce(request.CustomerEmail, draft.CustomerEmailSnapshot, ctx.Conversation.CustomerEmail);
        draft.CustomerPhoneSnapshot = Coalesce(request.CustomerPhone, draft.CustomerPhoneSnapshot, ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone, ctx.Config));
        draft.CustomerDocumentSnapshot = Coalesce(request.CustomerDocument, draft.CustomerDocumentSnapshot, GetFact(ctx, "customer_document"));
        draft.DeliveryAddressSnapshot = Coalesce(request.DeliveryAddress, draft.DeliveryAddressSnapshot, GetFact(ctx, "delivery_address"));
        draft.Notes = Coalesce(request.Notes, draft.Notes, null);
        draft.CustomerConfirmed = request.CustomerConfirmed;
        ApplyOrderCheckoutMetadata(ctx, draft);
    }

    private async Task<Order> ConvertDraftToOrderAsync(OrderDraft draft, CommerceAdapterContext adapterContext, bool customerConfirmed, CancellationToken ct)
    {
        var draftItems = await _unitOfWork.OrderDraftItems.GetByDraftIdAsync(draft.BusinessId, draft.OrderDraftId, ct);
        if (draftItems.Count == 0)
            throw new InvalidOperationException("Order has no items.");

        await EnsureOrderProductsSellableAsync(draft.BusinessId, draftItems, adapterContext, ct);

        var order = new Order
        {
            OrderId = Guid.NewGuid(),
            BusinessId = draft.BusinessId,
            AgentId = draft.AgentId,
            ConversationId = draft.ConversationId,
            IntegrationConnectionId = draft.IntegrationConnectionId,
            PaymentTransactionId = draft.PaymentTransactionId,
            CommerceWarehouseCode = draft.CommerceWarehouseCode,
            Source = draft.Source,
            FulfillmentMode = draft.FulfillmentMode,
            Status = customerConfirmed
                ? adapterContext.Provider == CommerceProvider.Local ? OrderStatus.Confirmed : OrderStatus.SyncPending
                : OrderStatus.PendingConfirmation,
            CustomerNameSnapshot = draft.CustomerNameSnapshot,
            CustomerEmailSnapshot = draft.CustomerEmailSnapshot,
            CustomerPhoneSnapshot = draft.CustomerPhoneSnapshot,
            CustomerDocumentSnapshot = draft.CustomerDocumentSnapshot,
            DeliveryAddressSnapshot = draft.DeliveryAddressSnapshot,
            Notes = draft.Notes,
            Currency = draft.Currency,
            Subtotal = draft.Subtotal,
            DiscountTotal = draft.DiscountTotal,
            TaxTotal = draft.TaxTotal,
            Total = draft.Total,
            CustomerConfirmed = customerConfirmed,
            IdempotencyKey = $"{draft.BusinessId:N}:{draft.ConversationId:N}:{DateTime.UtcNow:yyyyMMddHHmmss}",
            CustomAttributesJson = draft.CustomAttributesJson,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Orders.CreateAsync(order, ct);
        foreach (var draftItem in draftItems)
        {
            await _unitOfWork.OrderItems.CreateAsync(new OrderItem
            {
                OrderItemId = Guid.NewGuid(),
                OrderId = order.OrderId,
                BusinessId = draftItem.BusinessId,
                ProductId = draftItem.ProductId,
                IntegrationConnectionId = draftItem.IntegrationConnectionId,
                ExternalProductId = draftItem.ExternalProductId,
                Sku = draftItem.Sku,
                ProductNameSnapshot = draftItem.ProductNameSnapshot,
                DescriptionSnapshot = draftItem.DescriptionSnapshot,
                Quantity = draftItem.Quantity,
                UnitPrice = draftItem.UnitPrice,
                DiscountAmount = draftItem.DiscountAmount,
                TaxAmount = draftItem.TaxAmount,
                LineTotal = draftItem.LineTotal,
                RawPayloadJson = draftItem.RawPayloadJson,
                CreatedAt = DateTime.UtcNow
            }, ct);
        }
        await _unitOfWork.OrderDrafts.DeleteAsync(draft, ct);
        await _unitOfWork.SaveChangesAsync(ct);
        return order;
    }

    private async Task EnsureOrderProductsSellableAsync(
        Guid businessId,
        IReadOnlyList<OrderDraftItem> items,
        CommerceAdapterContext adapterContext,
        CancellationToken ct)
    {
        var unavailable = await _availability.FindUnavailableDraftItemsAsync(businessId, items, ct);
        if (unavailable.Count > 0)
            throw new InvalidOperationException("Product inactive.");

        if (adapterContext.Provider == CommerceProvider.Local)
            return;

        var adapter = _adapterFactory.Resolve(adapterContext.Provider);
        foreach (var item in items)
        {
            var live = await adapter.GetProductAsync(
                new AddOrderItemRequest(
                    item.ProductId,
                    item.ExternalProductId,
                    item.Sku,
                    item.ProductNameSnapshot,
                    item.Quantity,
                    null),
                adapterContext,
                ct);
            if (live is null || !_availability.IsSellable(live))
                throw new InvalidOperationException("Product inactive.");
            if (live.StockQuantity.HasValue && item.Quantity > live.StockQuantity.Value)
            {
                throw new InsufficientProductStockException(
                    live.Name,
                    item.Quantity,
                    live.StockQuantity.Value,
                    0m);
            }
        }
    }

    private async Task SyncExternalOrderIfNeededAsync(Order order, CommerceAdapterContext adapterContext, CancellationToken ct)
    {
        if (adapterContext.Provider == CommerceProvider.Local || !string.IsNullOrWhiteSpace(order.ExternalOrderId))
            return;

        var items = await _unitOfWork.OrderItems.GetByOrderIdAsync(order.BusinessId, order.OrderId, ct);
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

        order.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.Orders.UpdateAsync(order, ct);
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

    private static void ApplyOrderCheckoutMetadata(AgentConversationContext ctx, OrderDraft draft)
    {
        var checkoutMode = ctx.Config?.Checkout.ResolveMode("order");
        var shipping = checkoutMode?.Shipping ?? new OrderCheckoutShippingDefinition();
        var city = GetFact(ctx, "city");
        var shippingCost = ResolveShippingCost(shipping, city);
        var currency = ctx.Config?.Checkout.Currency;

        if (!string.IsNullOrWhiteSpace(currency))
            draft.Currency = currency.Trim().ToUpperInvariant();

        draft.Total = draft.Subtotal - draft.DiscountTotal + draft.TaxTotal + shippingCost;
        draft.CustomAttributesJson = BuildOrderCustomAttributes(ctx.Facts, city, shippingCost);
    }

    private async Task<OrderSnapshot> BuildSnapshotAsync(OrderDraft draft, CancellationToken ct)
    {
        var items = await _unitOfWork.OrderDraftItems.GetByDraftIdAsync(draft.BusinessId, draft.OrderDraftId, ct);
        return new OrderSnapshot(
            draft.OrderDraftId,
            OrderStatus.Draft,
            draft.Currency,
            draft.Subtotal,
            draft.DiscountTotal,
            draft.TaxTotal,
            draft.Total,
            items.Select(i => new OrderItemSnapshot(
                i.OrderDraftItemId,
                i.ProductId,
                i.ExternalProductId,
                i.Sku,
                i.ProductNameSnapshot,
                i.Quantity,
                i.UnitPrice,
                i.LineTotal)).ToList(),
            draft.PaymentTransactionId);
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

    private static OrderSnapshot EmptyDraftSnapshot() =>
        new(Guid.Empty, OrderStatus.Draft, "COP", 0, 0, 0, 0, []);

    private static decimal ResolveShippingCost(OrderCheckoutShippingDefinition shipping, string? city)
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

    private static string BuildOrderCustomAttributes(IReadOnlyDictionary<string, string> facts, string? city, decimal shippingCost)
    {
        var custom = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["city"] = city,
            ["shipping_cost"] = shippingCost,
            ["facts"] = facts
        };
        return JsonSerializer.Serialize(custom);
    }

    private static string? GetConnectionCurrency(IntegrationConnection? connection)
    {
        if (connection is null || string.IsNullOrWhiteSpace(connection.SettingsJson))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(connection.SettingsJson);
            if (doc.RootElement.TryGetProperty("currency", out var currency) && currency.ValueKind == JsonValueKind.String)
                return currency.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string? GetFact(AgentConversationContext ctx, string key) =>
        ctx.Facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();
}
