namespace Auraly.BuildingBlocks.Domain.RestaurantDemand;

public sealed record RestaurantDemandSale(Guid ProductId, DateOnly Date, decimal Quantity);
public sealed record RestaurantRecipeComponent(Guid ProductId, Guid IngredientProductId, decimal QuantityPerUnit);
public sealed record RestaurantDemandStock(Guid ProductId, bool ManagesStock, decimal QuantityOnHand,
    decimal IncomingQuantity = 0);
public sealed record RestaurantDemandQuantity(Guid ProductId, decimal GrossQuantity, decimal UncoveredQuantity);

public static class RestaurantDemandPlanner
{
    public static IReadOnlyDictionary<Guid, RestaurantDemandQuantity> Forecast(
        DateOnly firstDay, int days, IReadOnlyCollection<DateOnly> openDates,
        IReadOnlyCollection<RestaurantDemandSale> sales,
        IReadOnlyCollection<RestaurantRecipeComponent> recipe,
        IReadOnlyCollection<RestaurantDemandStock> stock,
        DateOnly? historyEndDate = null)
    {
        if (days is < 1 or > 90 || recipe.Any(line => line.QuantityPerUnit <= 0) ||
            stock.Any(line => line.IncomingQuantity < 0))
            throw new ArgumentException("Los datos del sugerido no son válidos.");
        var historyEnd = historyEndDate ?? firstDay;
        var open = openDates.Where(date => date < historyEnd && date >= historyEnd.AddDays(-56))
            .Distinct().ToArray();
        var sold = sales.Where(line => line.Date < historyEnd && line.Date >= historyEnd.AddDays(-56))
            .GroupBy(line => (line.ProductId, line.Date))
            .ToDictionary(group => group.Key, group => group.Sum(line => line.Quantity));
        var demand = new Dictionary<Guid, decimal>();
        foreach (var productId in sold.Keys.Select(key => key.ProductId).Distinct())
        {
            var total = 0m;
            for (var offset = 0; offset < days; offset++)
            {
                var weekday = firstDay.AddDays(offset).DayOfWeek;
                var comparable = open.Where(date => date.DayOfWeek == weekday)
                    .OrderByDescending(date => date).Take(8).ToArray();
                if (comparable.Length == 0) continue;
                total += Math.Max(0, comparable.Sum(date =>
                    sold.GetValueOrDefault((productId, date))) / comparable.Length);
            }
            demand[productId] = decimal.Round(total, 6, MidpointRounding.AwayFromZero);
        }

        var outgoing = recipe.GroupBy(line => line.ProductId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var inventories = stock.ToDictionary(line => line.ProductId);
        var nodes = demand.Keys.Concat(recipe.SelectMany(line => new[] { line.ProductId,
            line.IngredientProductId })).Concat(inventories.Keys).Distinct().ToArray();
        var indegree = nodes.ToDictionary(id => id, _ => 0);
        foreach (var line in recipe) indegree[line.IngredientProductId]++;
        var ready = new Queue<Guid>(nodes.Where(id => indegree[id] == 0));
        var result = new Dictionary<Guid, RestaurantDemandQuantity>();
        while (ready.TryDequeue(out var productId))
        {
            var gross = demand.GetValueOrDefault(productId);
            var available = inventories.TryGetValue(productId, out var balance) && balance.ManagesStock
                ? Math.Max(0, balance.QuantityOnHand) + balance.IncomingQuantity : 0;
            var uncovered = Math.Max(0, gross - available);
            result.Add(productId, new(productId, gross, uncovered));
            if (!outgoing.TryGetValue(productId, out var components)) continue;
            foreach (var line in components)
            {
                demand[line.IngredientProductId] = decimal.Round(
                    demand.GetValueOrDefault(line.IngredientProductId) + uncovered * line.QuantityPerUnit,
                    6, MidpointRounding.AwayFromZero);
                if (--indegree[line.IngredientProductId] == 0)
                    ready.Enqueue(line.IngredientProductId);
            }
        }
        if (result.Count != nodes.Length) throw new ArgumentException("La receta contiene un ciclo.");
        return result;
    }
}
