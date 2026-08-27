using System.Collections.Concurrent;
using Auraly.Contracts.Catalog;
using Auraly.Pos.Edge.Infrastructure;

namespace Auraly.Pos.Edge.Host;

public static class PosSynchronizationPermissions
{
    public const string ReadEvents = "pos.synchronization.events.read";
}

public sealed record PosSynchronizationEvent(
    long Sequence,
    DateTimeOffset OccurredAt,
    string Level,
    string Category,
    string Title,
    string? Detail,
    Guid? ProductId = null,
    decimal? PreviousPrice = null,
    decimal? NewPrice = null);

public sealed class PosSynchronizationEventLog(TimeProvider timeProvider)
    : IPosSynchronizationEventSink
{
    private const int Capacity = 250;
    private readonly ConcurrentQueue<PosSynchronizationEvent> events = new();
    private long sequence;

    public IReadOnlyList<PosSynchronizationEvent> Read(int take = 100) =>
        events.Reverse().Take(Math.Clamp(take, 1, Capacity)).ToArray();

    public void Record(string level, string category, string title, string? detail = null) =>
        Add(new(Interlocked.Increment(ref sequence), timeProvider.GetUtcNow(),
            level, category, title, detail));

    public void ProductReceived(PosCatalogItem product, PosCatalogItem? previous, bool bootstrap)
    {
        var changedPrice = previous is not null && previous.UnitPrice != product.UnitPrice;
        var title = previous is null
            ? $"Producto descargado: {product.Name}"
            : changedPrice
                ? $"Precio actualizado: {product.Name}"
                : $"Producto actualizado: {product.Name}";
        var detail = bootstrap
            ? $"Catálogo inicial · {product.ProductCode}"
            : changedPrice
                ? $"{product.CurrencyCode} {previous!.UnitPrice:N2} → {product.UnitPrice:N2} · {product.ProductCode}"
                : product.ProductCode;
        Add(new(Interlocked.Increment(ref sequence), timeProvider.GetUtcNow(),
            "Info", "Catalog", title, detail, product.ProductId,
            changedPrice ? previous!.UnitPrice : null,
            changedPrice ? product.UnitPrice : null));
    }

    private void Add(PosSynchronizationEvent item)
    {
        events.Enqueue(item);
        while (events.Count > Capacity) events.TryDequeue(out _);
    }
}
