using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Agents.Operations.Support;

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
        plan = RemoveNoOpFactClears(plan, context);
        plan = RecoverExplicitOrderList(plan, context);
        if (IsCatalogFollowUp(context) && !HasExplicitRequestedQuantity(NormalizeText(context.LatestUserMessage), context)
            && IsSingleOfferedProductReference(context))
            plan = RemoveCatalogFollowUpQuery(plan);
        plan = RemoveUnsupportedFinalization(plan, context);
        plan = DeferRemovalUntilCatalogReplacementIsSelected(plan);

        var orderChanges = plan.Signals.FirstOrDefault(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        if (orderChanges is null || !TryReadCommands(orderChanges.Value, out var commands))
            return plan;

        var normalizedMessage = NormalizeText(context.LatestUserMessage);
        if (IsCatalogInquiry(normalizedMessage) && !HasExplicitMutationVerb(normalizedMessage))
            return ReplaceInquiryMutationWithCatalogQuery(plan, context, orderChanges, commands);

        if (IsCatalogFollowUp(context) && !HasExplicitRequestedQuantity(normalizedMessage, context))
        {
            var withoutUnsupported = RemoveUnsupportedCatalogFollowUpMutations(
                plan, orderChanges, commands);
            return HasExplicitMutationVerb(normalizedMessage)
                ? withoutUnsupported
                : RemoveOrderChangesSignal(withoutUnsupported);
        }

        return plan;
    }

    private static bool IsSingleOfferedProductReference(TurnPlanningContext context)
    {
        var memoryContext = new AgentConversationContext { Config = context.Config };
        foreach (var fact in context.CurrentFacts)
            memoryContext.Facts[fact.Key] = fact.Value;
        return ProductSelectionMemory.FindCatalogMatches(memoryContext, context.LatestUserMessage).Count > 0;
    }
    private static TurnPlan RemoveCatalogFollowUpQuery(TurnPlan plan) =>
        RemoveSignal(plan, CatalogQuerySignal);
    private static TurnPlan RemoveOrderChangesSignal(TurnPlan plan) =>
        RemoveSignal(plan, OrderChangesSignal);
    private static TurnPlan RemoveSignal(TurnPlan plan, string signalType)
    {
        var signals = plan.Signals
            .Where(signal => !signal.Type.Equals(signalType, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return signals.Count == plan.Signals.Count ? plan : CopyWithSignals(plan, signals);
    }
    private static TurnPlan RemoveNoOpFactClears(TurnPlan plan, TurnPlanningContext context)
    {
        var facts = plan.Facts
            .Where(fact => !fact.Operation.Equals(TurnPlanOperations.Clear, StringComparison.OrdinalIgnoreCase)
                || context.CurrentFacts.ContainsKey(fact.Key))
            .ToList();
        if (facts.Count == plan.Facts.Count)
            return plan;

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = facts,
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = plan.Response
        };
    }

    private static TurnPlan RemoveUnsupportedCatalogFollowUpMutations(
        TurnPlan plan,
        PlannedSignal orderChanges,
        IReadOnlyList<CartCommandCandidate> commands)
    {
        var rawCommands = orderChanges.Value.EnumerateArray().ToArray();
        var retained = rawCommands
            .Zip(commands)
            .Where(pair =>
                !pair.Second.Operation.Equals("add", StringComparison.OrdinalIgnoreCase)
                && !pair.Second.Operation.Equals("set_quantity", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.First.Clone())
            .ToArray();
        if (retained.Length == rawCommands.Length)
            return plan;

        var signals = plan.Signals
            .Where(signal => !ReferenceEquals(signal, orderChanges))
            .ToList();
        if (retained.Length > 0)
        {
            var index = plan.Signals
                .TakeWhile(signal => !ReferenceEquals(signal, orderChanges))
                .Count();
            signals.Insert(
                Math.Min(index, signals.Count),
                new PlannedSignal
                {
                    Type = orderChanges.Type,
                    Value = JsonSerializer.SerializeToElement(retained),
                    Evidence = orderChanges.Evidence,
                    Confidence = orderChanges.Confidence
                });
        }

        return CopyWithSignals(plan, signals);
    }

    private static TurnPlan DeferRemovalUntilCatalogReplacementIsSelected(TurnPlan plan)
    {
        var catalogQuery = plan.Signals.LastOrDefault(signal =>
            signal.Type.Equals(CatalogQuerySignal, StringComparison.OrdinalIgnoreCase));
        var orderChanges = plan.Signals.LastOrDefault(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        if (catalogQuery is null
            || orderChanges is null
            || catalogQuery.Value.ValueKind != JsonValueKind.Object
            || !catalogQuery.Value.TryGetProperty("replacement_reference", out var replacementElement)
            || replacementElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(replacementElement.GetString())
            || orderChanges.Value.ValueKind != JsonValueKind.Array)
            return plan;

        var replacementReference = replacementElement.GetString()!;
        var retainedItems = orderChanges.Value.EnumerateArray()
            .Where(item => !IsDeferredReplacementRemoval(item, replacementReference))
            .Select(item => item.Clone())
            .ToArray();
        if (retainedItems.Length == orderChanges.Value.GetArrayLength())
            return plan;

        var signals = plan.Signals.Where(signal => !ReferenceEquals(signal, orderChanges)).ToList();
        if (retainedItems.Length > 0)
        {
            var replacementSignal = new PlannedSignal
            {
                Type = orderChanges.Type,
                Value = JsonSerializer.SerializeToElement(retainedItems),
                Evidence = orderChanges.Evidence,
                Confidence = orderChanges.Confidence
            };
            var catalogIndex = signals.FindIndex(signal => ReferenceEquals(signal, catalogQuery));
            signals.Insert(catalogIndex < 0 ? signals.Count : catalogIndex, replacementSignal);
        }

        return CopyWithSignals(plan, signals);
    }

    private static bool IsDeferredReplacementRemoval(JsonElement item, string replacementReference)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("operation", out var operation)
            || operation.ValueKind != JsonValueKind.String
            || operation.GetString()?.Equals("remove", StringComparison.OrdinalIgnoreCase) != true
            || !item.TryGetProperty("productText", out var productText)
            || productText.ValueKind != JsonValueKind.String)
            return false;

        var commandReference = NormalizeReference(productText.GetString() ?? string.Empty);
        var replacement = NormalizeReference(replacementReference);
        if (commandReference.Length == 0 || replacement.Length == 0)
            return false;

        return commandReference.Equals(replacement, StringComparison.Ordinal)
            || $" {commandReference} ".Contains($" {replacement} ", StringComparison.Ordinal)
            || $" {replacement} ".Contains($" {commandReference} ", StringComparison.Ordinal);
    }

    private static string NormalizeReference(string value) =>
        Regex.Replace(NormalizeText(value), @"[^\p{L}\p{N}]+", " ").Trim();

    private static TurnPlan RecoverExplicitOrderList(TurnPlan plan, TurnPlanningContext context)
    {
        if (!context.Scope.Signals.ContainsKey(OrderChangesSignal)
            || IsCatalogInquiry(NormalizeText(context.LatestUserMessage))
            || !TryParseOrderList(context.LatestUserMessage, out var commands))
            return plan;

        // A deterministic list is authoritative even when the planner emitted only
        // the first bullet. A valid but truncated signal must not discard the rest.
        var recovered = new PlannedSignal
        {
            Type = OrderChangesSignal,
            Value = JsonSerializer.SerializeToElement(commands.Select(command => new
            {
                operation = "add",
                productText = command.ProductText,
                quantity = command.Quantity,
                destinationReference = (string?)null
            })),
            Evidence = context.LatestUserMessage.Trim(),
            Confidence = 1
        };
        var signals = plan.Signals.ToList();
        var existingIndex = signals.FindIndex(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
            signals[existingIndex] = recovered;
        else
            signals.Add(recovered);

        return CopyWithSignals(plan, signals);
    }

    private static bool TryParseOrderList(string message, out IReadOnlyList<CartCommandCandidate> commands)
    {
        commands = [];
        var parsed = new List<CartCommandCandidate>();
        var bulletLines = 0;
        var meaningfulLines = 0;

        foreach (var rawLine in message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            meaningfulLines++;
            if (BulletPrefixRegex().IsMatch(line))
                bulletLines++;

            var match = OrderListLineRegex().Match(line);
            if (!match.Success
                || !decimal.TryParse(
                    match.Groups["quantity"].Value.Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var quantity)
                || quantity <= 0)
                continue;

            var productText = match.Groups["product"].Value.Trim().TrimEnd(',', ';');
            if (productText.Length < 2 || !productText.Any(char.IsLetter))
                continue;

            parsed.Add(new CartCommandCandidate("add", productText, quantity));
        }

        // A multi-line quantity list is an order request even when the customer omits
        // verbs such as "agrega" or "dame". Requiring two bullets avoids turning
        // numbered prose, recipes or ordinary single-product catalog questions into carts.
        if (parsed.Count < 2
            || bulletLines < 2
            || parsed.Count != meaningfulLines)
            return false;

        commands = parsed;
        return true;
    }

    private static TurnPlan RemoveUnsupportedFinalization(TurnPlan plan, TurnPlanningContext context)
    {
        if (!context.Config.Commerce.Enabled || HasCurrentCartItems(context.StructuredContext))
            return plan;

        var finalizationKeys = context.Config.FactSchema
            .Where(fact => fact.Role?.Equals("order.finalized", StringComparison.OrdinalIgnoreCase) == true)
            .Select(fact => fact.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (finalizationKeys.Count == 0)
            return plan;

        var facts = plan.Facts
            .Where(fact => !finalizationKeys.Contains(fact.Key)
                || !fact.Operation.Equals(TurnPlanOperations.Set, StringComparison.OrdinalIgnoreCase)
                || !IsTrue(fact.Value))
            .ToList();
        if (facts.Count == plan.Facts.Count)
            return plan;

        return new TurnPlan
        {
            FlowIntent = plan.FlowIntent,
            Facts = facts,
            Signals = plan.Signals,
            Decision = plan.Decision,
            Response = plan.Response
        };
    }

    private static bool HasCurrentCartItems(IReadOnlyDictionary<string, JsonElement>? context)
    {
        if (context is null
            || !context.TryGetValue("currentCart", out var cart)
            || cart.ValueKind != JsonValueKind.Object
            || !cart.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return false;

        return items.GetArrayLength() > 0;
    }

    private static bool IsTrue(JsonElement value) => value.ValueKind == JsonValueKind.True
        || value.ValueKind == JsonValueKind.String
        && bool.TryParse(value.GetString(), out var parsed)
        && parsed;

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

    private static bool HasExplicitRequestedQuantity(string message, TurnPlanningContext context) =>
        StartsWithExplicitQuantity(message)
        || MutationQuantityRegex().IsMatch(message)
        || CommerceConversationMatcher.ContainsPhrase(
            message, context.Config.Commerce.Conversation.AdditionalRequestPhrases)
        || (!IsOfferedProductReference(message, context.StructuredContext)
            && (UnitQuantityRegex().IsMatch(message) || TrailingQuantityRegex().IsMatch(message)));

    private static bool IsOfferedProductReference(
        string message,
        IReadOnlyDictionary<string, JsonElement>? structuredContext)
    {
        var reference = NormalizeReference(message);
        if (reference.Length == 0
            || !reference.Any(char.IsLetter)
            || structuredContext is null
            || !structuredContext.TryGetValue("shoppingContext", out var shoppingContext)
            || shoppingContext.ValueKind != JsonValueKind.Object
            || !shoppingContext.TryGetProperty("offers", out var offers)
            || offers.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var offer in offers.EnumerateArray())
        {
            if (offer.ValueKind != JsonValueKind.Object
                || !offer.TryGetProperty("products", out var products)
                || products.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var product in products.EnumerateArray())
            {
                if (product.ValueKind != JsonValueKind.String)
                    continue;
                var productReference = NormalizeReference(product.GetString() ?? string.Empty);
                if (productReference.Equals(reference, StringComparison.Ordinal)
                    || $" {productReference} ".Contains($" {reference} ", StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

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
    [GeneratedRegex(@"\b(?:agrega(?:me|nos)?|anade(?:me|nos)?|pon(?:me|nos)?|dame|danos|incluye|quiero|necesito)\s+(?:(?:de|del|la|el|las|los|esa|ese|esas|esos)\s+){0,2}(?:\d+(?:[.,]\d+)?(?:\s*(?:k|kg|kgs|lb|lbs))?|un|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|trece|catorce|quince|dieciseis|diecisiete|dieciocho|diecinueve|veinte)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MutationQuantityRegex();

    [GeneratedRegex(@"\b(?:\d+(?:[.,]\d+)?|un|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|trece|catorce|quince|dieciseis|diecisiete|dieciocho|diecinueve|veinte)\s+(?:unidad(?:es)?|paquete(?:s)?|caja(?:s)?|bolsa(?:s)?|pieza(?:s)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnitQuantityRegex();
    [GeneratedRegex(@"\b(?:\d+(?:[.,]\d+)?|un|uno|una|dos|tres|cuatro|cinco|seis|siete|ocho|nueve|diez|once|doce|trece|catorce|quince|dieciseis|diecisiete|dieciocho|diecinueve|veinte)\s*(?:[,;]|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TrailingQuantityRegex();


    [GeneratedRegex(@"^\s*[-*•▪◦]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex BulletPrefixRegex();

    [GeneratedRegex(@"^\s*[-*•▪◦]\s*(?<quantity>\d+(?:[.,]\d+)?)\s*(?:x\b\s*)?(?<product>[^\r\n]+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderListLineRegex();

    private sealed record CartCommandCandidate(string Operation, string ProductText, decimal? Quantity);
}
