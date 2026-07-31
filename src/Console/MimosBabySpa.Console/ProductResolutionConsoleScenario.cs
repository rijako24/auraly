using MimosBabySpa.Application.Agents;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Domain.Catalog;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Console;

internal static class ProductResolutionConsoleScenario
{
    public static async Task<int> RunAsync()
    {
        var jamon = Product("JAMON CUNIT X 500GR", "CF17");
        var sandwich = Product("JAMON SANDWICH PIETRAN X 500GR", "PI12");
        var small = Product("SALCHICHA LONG X 550GR", "CF59");
        var large = Product("SALCHICHA LONG X 1100GR", "CF20");

        var checks = new List<(string Name, bool Passed)>
        {
            ("error ortografico produce sugerencia segura",
                ProductResolutionEngine.Resolve("jamonada cunichef", Candidates(jamon, sandwich)).Status
                    == ProductResolutionStatus.SuggestionRequired),
            ("alias confirmado del cliente auto-resuelve",
                ProductResolutionEngine.Resolve("jamonada cunichef",
                    [new(jamon, ProductMatchSource.CustomerAlias, true, true)]).Status
                    == ProductResolutionStatus.Resolved),
            ("alias global no aprobado solo sugiere",
                ProductResolutionEngine.Resolve("jamonada cunichef",
                    [new(jamon, ProductMatchSource.BusinessAlias, true, false)]).Status
                    == ProductResolutionStatus.SuggestionRequired),
            ("presentacion numerica es restriccion dura",
                ProductResolutionEngine.Resolve("salchicha long 550", Candidates(small, large)).Selected == small),
            ("familia sin presentacion permanece ambigua",
                ProductResolutionEngine.Resolve("salchicha long", Candidates(small, large)).Status
                    == ProductResolutionStatus.Ambiguous),
            ("texto ajeno no inventa opciones",
                ProductResolutionEngine.Resolve("producto marciano azul", Candidates(jamon)).Candidates.Count == 0),
            ("indice normaliza variaciones conocidas",
                ProductSearchText.GetSearchKeys("jamonada cunichef").Contains("jamon")
                && ProductSearchText.GetSearchKeys("jamonada cunichef").Contains("cuni"))
        };

        var resolver = new FixtureResolver(new Dictionary<string, IReadOnlyList<ProductReference>>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["JAMON CUNIT X 500GR"] = [jamon],
            ["salchicha long"] = [small, large]
        });
        var store = new FixtureStore();
        var batch = await new CartCommandBatchProcessor(resolver, store).ApplyAsync(
            new AgentConversationContext { BusinessId = Guid.NewGuid(), ConversationId = Guid.NewGuid() },
            [
                new(CartCommandOperations.Add, "JAMON CUNIT X 500GR", 2, null),
                new(CartCommandOperations.Add, "salchicha long", 1, null)
            ]);
        checks.Add(("lote parcial aplica lo seguro una sola vez",
            batch.Code == "cart.partially_applied"
            && store.ApplyCalls == 1
            && store.Applied.Count == 1
            && store.Applied[0].Product?.Name == jamon.Name
            && batch.UnresolvedItems.Count == 1
            && batch.UnresolvedItems[0].Issue.ProductCandidates.Count == 2));

        foreach (var check in checks)
            System.Console.WriteLine($"[{(check.Passed ? "PASS" : "FAIL")}] {check.Name}");

        var failures = checks.Count(check => !check.Passed);
        System.Console.WriteLine($"Product resolution smoke: {checks.Count - failures}/{checks.Count} passed.");
        return failures == 0 ? 0 : 1;
    }

    private static IReadOnlyList<RetrievedProductCandidate> Candidates(params ProductReference[] products) =>
        products.Select(product => new RetrievedProductCandidate(product, ProductMatchSource.LocalLexicalIndex)).ToList();

    private static ProductReference Product(string name, string sku) =>
        new(Guid.NewGuid(), sku, sku, name, null, null, 10m, "COP", 100m);

    private sealed class FixtureResolver(IReadOnlyDictionary<string, IReadOnlyList<ProductReference>> products)
        : ICartProductResolver
    {
        public Task<IReadOnlyList<ProductReference>> FindAsync(
            AgentConversationContext context, string productText, CancellationToken cancellationToken = default) =>
            Task.FromResult(products.TryGetValue(productText, out var matches)
                ? matches
                : (IReadOnlyList<ProductReference>)[]);
    }

    private sealed class FixtureStore : ICartMutationStore
    {
        public int ApplyCalls { get; private set; }
        public IReadOnlyList<ResolvedCartCommand> Applied { get; private set; } = [];

        public Task<OrderSnapshot> GetCurrentAsync(
            AgentConversationContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(EmptySnapshot());

        public Task<OrderSnapshot> ApplyAtomicallyAsync(
            AgentConversationContext context, IReadOnlyList<ResolvedCartCommand> commands,
            CancellationToken cancellationToken = default)
        {
            ApplyCalls++;
            Applied = commands;
            return Task.FromResult(EmptySnapshot());
        }

        private static OrderSnapshot EmptySnapshot() =>
            new(Guid.Empty, OrderStatus.Draft, "COP", 0, 0, 0, []);
    }
}
