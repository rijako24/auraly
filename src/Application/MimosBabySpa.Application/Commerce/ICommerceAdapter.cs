using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Commerce;

public interface ICommerceAdapter
{
    CommerceProvider Provider { get; }
    Task<ProductSearchResult> SearchProductsAsync(ProductSearchRequest request, CommerceAdapterContext ctx, CancellationToken ct = default);
    Task<ProductReference?> GetProductAsync(AddOrderItemRequest request, CommerceAdapterContext ctx, CancellationToken ct = default);
    Task<CreateExternalOrderResult> CreateOrderAsync(Order order, IReadOnlyList<OrderItem> items, CommerceAdapterContext ctx, CancellationToken ct = default);
}

/// <summary>
/// Marks adapters whose live catalog is authoritative for price and availability.
/// A caller-provided or cached price must never override the adapter quote.
/// </summary>
public interface IAuthoritativeCommercePricingAdapter;
