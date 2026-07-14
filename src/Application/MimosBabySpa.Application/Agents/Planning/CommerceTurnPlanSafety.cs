using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MimosBabySpa.Application.Agents.Planning;

/// <summary>
/// Enforces the semantic boundary between catalog discovery and cart mutation.
/// This is intentionally narrow: the LLM still interprets product language, while
/// this guard prevents an informational catalog turn from inventing an add of one.
/// </summary>
public static partial class CommerceTurnPlanSafety
{
    private const string OrderChangesSignal = "order_changes";
    private const string CatalogQuerySignal = "catalog_query";

    public static TurnPlan Normalize(TurnPlan plan, TurnPlanningContext context)
    {
        var orderChanges = plan.Signals.FirstOrDefault(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        if (orderChanges is null || !TryReadCommands(orderChanges.Value, out var commands))
            return plan;

        var normalizedMessage = NormalizeText(context.LatestUserMessage);
        if (IsCatalogInquiry(normalizedMessage) && !HasExplicitMutationVerb(normalizedMessage))
            return ReplaceInquiryMutationWithCatalogQuery(plan, context, orderChanges, commands);

        if (IsCatalogFollowUp(context)
            && commands.Count > 0
            && commands.All(command => command.Operation.Equals("add", StringComparison.OrdinalIgnoreCase)
                && command.Quantity == 1m)
            && !HasExplicitMutationVerb(normalizedMessage)
            && !StartsWithExplicitQuantity(normalizedMessage))
        {
            return CopyWithSignals(plan, plan.Signals.Where(signal => !ReferenceEquals(signal, orderChanges)));
        }

        return plan;
    }

    private static TurnPlan ReplaceInquiryMutationWithCatalogQuery(
        TurnPlan plan,
        TurnPlanningContext context,
        PlannedSignal orderChanges,
        IReadOnlyList<CartCommandCandidate> commands)
    {
        if (!context.Scope.Signals.ContainsKey(CatalogQuerySignal))
            return CopyWithSignals(plan, plan.Signals.Where(signal => !ReferenceEquals(signal, orderChanges)));

        var retained = plan.Signals
            .Where(signal => !ReferenceEquals(signal, orderChanges))
            .ToList();
        if (retained.Any(signal => signal.Type.Equals(CatalogQuerySignal, StringComparison.OrdinalIgnoreCase)))
            return CopyWithSignals(plan, retained);

        var queries = commands
            .Where(command => command.Operation.Equals("add", StringComparison.OrdinalIgnoreCase))
            .Select(command => command.ProductText.Trim())
            .Where(product => product.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (queries.Length == 0)
            return CopyWithSignals(plan, retained);

        retained.Add(new PlannedSignal
        {
            Type = CatalogQuerySignal,
            Value = JsonSerializer.SerializeToElement(new { queries }),
            Evidence = orderChanges.Evidence,
            Confidence = orderChanges.Confidence
        });
        return CopyWithSignals(plan, retained);
    }

    private static TurnPlan CopyWithSignals(TurnPlan plan, IEnumerable<PlannedSignal> signals) => new()
    {
        FlowIntent = plan.FlowIntent,
        Facts = plan.Facts,
        Signals = signals.ToArray(),
        Decision = plan.Decision,
        Response = plan.Response
    };

    private static bool IsCatalogFollowUp(TurnPlanningContext context)
    {
        if (context.StructuredContext is null
            || !context.StructuredContext.TryGetValue("shoppingContext", out var shoppingContext)
            || shoppingContext.ValueKind != JsonValueKind.Object
            || !shoppingContext.TryGetProperty("interaction", out var interaction)
            || interaction.ValueKind != JsonValueKind.Object
            || !interaction.TryGetProperty("expected_reply", out var expectedReply))
            return false;

        return expectedReply.ValueKind == JsonValueKind.String
            && expectedReply.GetString()?.Equals("catalog_follow_up", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool TryReadCommands(JsonElement value, out IReadOnlyList<CartCommandCandidate> commands)
    {
        commands = [];
        if (value.ValueKind != JsonValueKind.Array)
            return false;

        var parsed = new List<CartCommandCandidate>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !item.TryGetProperty("operation", out var operation)
                || operation.ValueKind != JsonValueKind.String
                || !item.TryGetProperty("productText", out var productText)
                || productText.ValueKind != JsonValueKind.String)
                return false;

            decimal? quantity = null;
            if (item.TryGetProperty("quantity", out var quantityElement)
                && quantityElement.ValueKind == JsonValueKind.Number
                && quantityElement.TryGetDecimal(out var parsedQuantity))
                quantity = parsedQuantity;

            parsed.Add(new CartCommandCandidate(
                operation.GetString() ?? string.Empty,
                productText.GetString() ?? string.Empty,
                quantity));
        }

        commands = parsed;
        return true;
    }

    private static bool IsCatalogInquiry(string message) => CatalogInquiryRegex().IsMatch(message);

    private static bool HasExplicitMutationVerb(string message) => MutationVerbRegex().IsMatch(message);

    private static bool StartsWithExplicitQuantity(string message) => QuantityPrefixRegex().IsMatch(message);

    private static string NormalizeText(string value)
    {
        var decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return builder.ToString().Normalize(NormalizationForm.FormC).Trim();
    }

    [GeneratedRegex(@"\b(tienes?|tienen|hay|manejas?|manejan|vendes?|venden|disponible(?:s)?|disponibilidad|precio(?:s)?|cuanto\s+(?:cuesta|vale)|que\s+(?:opciones|referencias))\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatalogInquiryRegex();

    [GeneratedRegex(@"\b(agrega(?:me|nos)?|anade(?:me|nos)?|pon(?:me|nos)?|dame|danos|incluye|quita(?:me|nos)?|elimina|retira|saca|cambia|actualiza|sube|baja)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MutationVerbRegex();

    [GeneratedRegex(@"^\s*(?:\d+(?:[.,]\d+)?|un|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|trece|catorce|quince|dieciseis|diecisiete|dieciocho|diecinueve|veinte)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantityPrefixRegex();

    private sealed record CartCommandCandidate(string Operation, string ProductText, decimal? Quantity);
}
