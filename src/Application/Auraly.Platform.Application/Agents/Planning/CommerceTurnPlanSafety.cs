using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using Auraly.Platform.Application.Commerce;
using Auraly.Platform.Application.Agents.Operations.Support;

namespace Auraly.Platform.Application.Agents.Planning;

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
        if (IsCatalogFollowUp(context) && IsSingleOfferedProductReference(context))
            plan = RemoveCatalogFollowUpQuery(plan);
        plan = RemoveUnsupportedFinalization(plan, context);
        plan = DeferRemovalUntilCatalogReplacementIsSelected(plan);
        plan = NormalizeNewOfferedProductQuantities(plan, context);

        return AuthorizeCartMutations(plan, context);
    }

    public static TurnPlan AuthorizeRecoveredCartMutations(
        TurnPlan originalPlan,
        TurnPlan recoveredPlan,
        TurnPlanningContext context)
    {
        var original = originalPlan.Signals.FirstOrDefault(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        var recovered = recoveredPlan.Signals.FirstOrDefault(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        if (recovered is null
            || original is not null
            && original.Value.GetRawText().Equals(recovered.Value.GetRawText(), StringComparison.Ordinal)
            && string.Equals(original.Evidence, recovered.Evidence, StringComparison.Ordinal))
            return recoveredPlan;

        return AuthorizeCartMutations(recoveredPlan, context);
    }

    public static TurnPlan AuthorizeCartMutations(TurnPlan plan, TurnPlanningContext context)
    {
        var orderChanges = plan.Signals.FirstOrDefault(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        if (orderChanges is null || !TryReadCommands(orderChanges.Value, out var commands))
            return plan;

        return AuthorizeMutationBatch(plan, context, orderChanges, commands);
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

    private static TurnPlan NormalizeNewOfferedProductQuantities(
        TurnPlan plan,
        TurnPlanningContext context)
    {
        var orderChanges = plan.Signals.FirstOrDefault(signal =>
            signal.Type.Equals(OrderChangesSignal, StringComparison.OrdinalIgnoreCase));
        if (orderChanges?.Value.ValueKind != JsonValueKind.Array)
            return plan;

        var changed = false;
        var normalizedCommands = orderChanges.Value.EnumerateArray()
            .Select(command =>
            {
                if (!ShouldConvertSetQuantityToAdd(command, context))
                    return command.Clone();

                changed = true;
                var properties = command.EnumerateObject().ToDictionary(
                    property => property.Name,
                    property => property.Value.Clone(),
                    StringComparer.Ordinal);
                properties["operation"] = JsonSerializer.SerializeToElement("add");
                return JsonSerializer.SerializeToElement(properties);
            })
            .ToArray();
        if (!changed)
            return plan;

        var replacement = new PlannedSignal
        {
            Type = orderChanges.Type,
            Value = JsonSerializer.SerializeToElement(normalizedCommands),
            Evidence = orderChanges.Evidence,
            Confidence = orderChanges.Confidence
        };
        return CopyWithSignals(
            plan,
            plan.Signals.Select(signal =>
                ReferenceEquals(signal, orderChanges) ? replacement : signal));
    }

    private static bool ShouldConvertSetQuantityToAdd(
        JsonElement command,
        TurnPlanningContext context)
    {
        if (command.ValueKind != JsonValueKind.Object
            || !command.TryGetProperty("operation", out var operation)
            || operation.ValueKind != JsonValueKind.String
            || !operation.GetString()!.Equals("set_quantity", StringComparison.OrdinalIgnoreCase)
            || !command.TryGetProperty("productText", out var productTextElement)
            || productTextElement.ValueKind != JsonValueKind.String
            || !command.TryGetProperty("quantity", out var quantity)
            || quantity.ValueKind != JsonValueKind.Number)
            return false;

        var productText = NormalizeReference(productTextElement.GetString() ?? string.Empty);
        return productText.Length > 0
            && IsExactOfferedProduct(productText, context)
            && !CurrentCartContainsExactProduct(productText, context.StructuredContext);
    }

    private static bool IsExactOfferedProduct(
        string productText,
        TurnPlanningContext context)
    {
        if (EnumerateProductNames(context.StructuredContext, "shoppingContext", "offers", "products")
            .Any(name => NormalizeReference(name).Equals(productText, StringComparison.Ordinal)))
            return true;

        var memoryContext = new AgentConversationContext { Config = context.Config };
        foreach (var fact in context.CurrentFacts)
            memoryContext.Facts[fact.Key] = fact.Value;
        return ProductSelectionMemory.FindCatalogMatches(memoryContext, productText)
            .Any(product => NormalizeReference(product.Name).Equals(productText, StringComparison.Ordinal));
    }

    private static bool CurrentCartContainsExactProduct(
        string productText,
        IReadOnlyDictionary<string, JsonElement>? structuredContext)
    {
        if (structuredContext is null
            || !structuredContext.TryGetValue("currentCart", out var cart)
            || cart.ValueKind != JsonValueKind.Object
            || !cart.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return false;

        return items.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
            && NormalizeReference(name.GetString() ?? string.Empty)
                .Equals(productText, StringComparison.Ordinal));
    }

    private static IEnumerable<string> EnumerateProductNames(
        IReadOnlyDictionary<string, JsonElement>? structuredContext,
        string contextKey,
        string collectionKey,
        string productCollectionKey)
    {
        if (structuredContext is null
            || !structuredContext.TryGetValue(contextKey, out var context)
            || context.ValueKind != JsonValueKind.Object
            || !context.TryGetProperty(collectionKey, out var collections)
            || collections.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var collection in collections.EnumerateArray())
        {
            if (collection.ValueKind != JsonValueKind.Object
                || !collection.TryGetProperty(productCollectionKey, out var products)
                || products.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var product in products.EnumerateArray())
                if (product.ValueKind == JsonValueKind.String)
                    yield return product.GetString() ?? string.Empty;
        }
    }

    private static TurnPlan RecoverExplicitOrderList(TurnPlan plan, TurnPlanningContext context)
    {
        if (!context.Scope.Signals.ContainsKey(OrderChangesSignal)
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

    private static TurnPlan AuthorizeMutationBatch(
        TurnPlan plan,
        TurnPlanningContext context,
        PlannedSignal orderChanges,
        IReadOnlyList<CartCommandCandidate> commands)
    {
        var rawCommands = orderChanges.Value.EnumerateArray().ToArray();
        var retained = new List<JsonElement>(rawCommands.Length);
        var discoveryQueries = new List<string>();
        var redundantCatalogTargets = new List<string>();
        var hasExactSignalEvidence = IsExactMessageEvidence(
            orderChanges.Evidence,
            context.LatestUserMessage);

        foreach (var pair in rawCommands.Zip(commands))
        {
            var command = pair.Second;
            var mutatesQuantity = command.Operation.Equals("add", StringComparison.OrdinalIgnoreCase)
                || command.Operation.Equals("set_quantity", StringComparison.OrdinalIgnoreCase);
            var resolvesPending = ResolvesPendingMutation(command, context);
            var coveredByCatalogRead = mutatesQuantity
                && IsCoveredByCatalogRead(plan, command.ProductText);
            var explicitMutationRequest = mutatesQuantity
                && IsExplicitMutationRequest(orderChanges.Evidence, command, context);
            var conflictsWithCatalogRead = mutatesQuantity
                && !explicitMutationRequest
                && (coveredByCatalogRead || IsCatalogInquiryRequest(orderChanges.Evidence));
            var groundedQuantity = mutatesQuantity && HasGroundedQuantity(command, context);
            var validExistingTarget = !command.Operation.Equals("set_quantity", StringComparison.OrdinalIgnoreCase)
                || CurrentCartContainsReference(command.ProductText, context.StructuredContext);
            var validRemovalTarget = !command.Operation.Equals("remove", StringComparison.OrdinalIgnoreCase)
                || CurrentCartContainsReference(command.ProductText, context.StructuredContext)
                || resolvesPending;
            var validPendingCancellation = !command.Operation.Equals("cancel_pending", StringComparison.OrdinalIgnoreCase)
                || resolvesPending;

            var authorized = hasExactSignalEvidence
                && !conflictsWithCatalogRead
                && validExistingTarget
                && validRemovalTarget
                && validPendingCancellation
                && (!mutatesQuantity || groundedQuantity || resolvesPending);
            if (authorized)
            {
                retained.Add(pair.First.Clone());
                if (coveredByCatalogRead && explicitMutationRequest)
                    redundantCatalogTargets.Add(command.ProductText);
                continue;
            }

            if (mutatesQuantity
                && !IsCatalogFollowUp(context)
                && !IsOfferedProductReference(context.LatestUserMessage, context.StructuredContext)
                && !string.IsNullOrWhiteSpace(command.ProductText))
                discoveryQueries.Add(command.ProductText.Trim());
        }

        if (retained.Count == rawCommands.Length)
            return RemoveCoveredCatalogTargets(plan, redundantCatalogTargets);

        var signals = plan.Signals
            .Where(signal => !ReferenceEquals(signal, orderChanges))
            .ToList();
        if (retained.Count > 0)
        {
            var index = plan.Signals.TakeWhile(signal => !ReferenceEquals(signal, orderChanges)).Count();
            signals.Insert(Math.Min(index, signals.Count), new PlannedSignal
            {
                Type = orderChanges.Type,
                Value = JsonSerializer.SerializeToElement(retained),
                Evidence = orderChanges.Evidence,
                Confidence = orderChanges.Confidence
            });
        }

        if (discoveryQueries.Count > 0
            && context.Scope.Signals.TryGetValue(CatalogQuerySignal, out var catalogSignal)
            && !signals.Any(signal => signal.Type.Equals(CatalogQuerySignal, StringComparison.OrdinalIgnoreCase)))
        {
            var targets = discoveryQueries
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var value = new Dictionary<string, object?>
            {
                ["intent"] = "search_target",
                ["target"] = new { kind = "product", text = targets[0] },
                ["additional_targets"] = targets.Skip(1).ToArray()
            };
            if (SchemaContainsProperty(catalogSignal.ValueSchema, "replacement_reference"))
                value["replacement_reference"] = null;

            signals.Add(new PlannedSignal
            {
                Type = CatalogQuerySignal,
                Value = JsonSerializer.SerializeToElement(value),
                Evidence = orderChanges.Evidence,
                Confidence = orderChanges.Confidence
            });
        }

        return RemoveCoveredCatalogTargets(
            CopyWithSignals(plan, signals),
            redundantCatalogTargets);
    }

    private static bool IsExplicitMutationRequest(
        string evidence,
        CartCommandCandidate command,
        TurnPlanningContext context)
    {
        if (!IsExactMessageEvidence(evidence, context.LatestUserMessage))
            return false;

        var normalizedEvidence = NormalizeText(evidence);
        if (ExplicitMutationVerbRegex().IsMatch(normalizedEvidence))
            return true;

        if (IsCatalogInquiryRequest(evidence))
            return false;

        // Quantity-led lines such as "2 product X" are direct order lines even
        // without an introductory verb. The catalog-question check above keeps
        // quantities mentioned only as requested availability read-only.
        return HasGroundedQuantity(command, context);
    }

    private static bool IsCatalogInquiryRequest(string evidence)
    {
        var normalizedEvidence = NormalizeText(evidence);
        return CatalogInquiryRegex().IsMatch(normalizedEvidence)
            || normalizedEvidence.Contains('?')
            || normalizedEvidence.Contains('¿');
    }

    private static TurnPlan RemoveCoveredCatalogTargets(
        TurnPlan plan,
        IReadOnlyCollection<string> productTexts)
    {
        var references = productTexts
            .Select(NormalizeReference)
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (references.Length == 0)
            return plan;

        var changed = false;
        var signals = new List<PlannedSignal>(plan.Signals.Count);
        foreach (var signal in plan.Signals)
        {
            if (!signal.Type.Equals(CatalogQuerySignal, StringComparison.OrdinalIgnoreCase)
                || !IsCatalogTargetSearch(signal.Value)
                || !TryRemoveCatalogTargets(signal, references, out var replacement))
            {
                signals.Add(signal);
                continue;
            }

            changed = true;
            if (replacement is not null)
                signals.Add(replacement);
        }

        return changed ? CopyWithSignals(plan, signals) : plan;
    }

    private static bool TryRemoveCatalogTargets(
        PlannedSignal signal,
        IReadOnlyCollection<string> mutationReferences,
        out PlannedSignal? replacement)
    {
        replacement = signal;
        if (!signal.Value.TryGetProperty("target", out var target)
            || target.ValueKind != JsonValueKind.Object
            || !target.TryGetProperty("text", out var targetText)
            || targetText.ValueKind != JsonValueKind.String)
            return false;

        var primaryText = targetText.GetString() ?? string.Empty;
        var primaryCovered = IsCoveredReference(primaryText, mutationReferences);
        var additional = signal.Value.TryGetProperty("additional_targets", out var additionalElement)
            && additionalElement.ValueKind == JsonValueKind.Array
            ? additionalElement.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty)
                .ToArray()
            : [];
        var retainedAdditional = additional
            .Where(query => !IsCoveredReference(query, mutationReferences))
            .ToList();
        if (!primaryCovered && retainedAdditional.Count == additional.Length)
            return false;

        if (primaryCovered && retainedAdditional.Count == 0)
        {
            replacement = null;
            return true;
        }

        var properties = signal.Value.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.Clone(),
            StringComparer.Ordinal);
        if (primaryCovered)
        {
            var promotedText = retainedAdditional[0];
            retainedAdditional.RemoveAt(0);
            var targetKind = target.TryGetProperty("kind", out var kind)
                && kind.ValueKind == JsonValueKind.String
                ? kind.GetString()
                : "product";
            properties["target"] = JsonSerializer.SerializeToElement(
                new { kind = targetKind, text = promotedText });
        }
        properties["additional_targets"] = JsonSerializer.SerializeToElement(retainedAdditional);

        replacement = new PlannedSignal
        {
            Type = signal.Type,
            Value = JsonSerializer.SerializeToElement(properties),
            Evidence = signal.Evidence,
            Confidence = signal.Confidence
        };
        return true;
    }

    private static bool IsCoveredReference(
        string query,
        IReadOnlyCollection<string> mutationReferences)
    {
        var normalizedQuery = NormalizeReference(query);
        return normalizedQuery.Length > 0
            && mutationReferences.Any(reference => SameReference(reference, normalizedQuery));
    }

    private static bool IsExactMessageEvidence(string evidence, string message)
    {
        var normalizedEvidence = NormalizeText(evidence);
        return normalizedEvidence.Length > 0
            && NormalizeText(message).Contains(normalizedEvidence, StringComparison.Ordinal);
    }

    private static bool HasGroundedQuantity(CartCommandCandidate command, TurnPlanningContext context)
    {
        if (command.Quantity is not { } expected || expected <= 0)
            return false;

        var message = NormalizeText(context.LatestUserMessage);
        var product = NormalizeText(command.ProductText);
        var evidenceOutsideProduct = product.Length == 0
            ? message
            : Regex.Replace(
                $" {message} ",
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(product)}(?![\p{{L}}\p{{N}}])",
                " ",
                RegexOptions.CultureInvariant).Trim();

        foreach (Match match in NumericQuantityRegex().Matches(evidenceOutsideProduct))
        {
            if (decimal.TryParse(
                    match.Value.Replace(',', '.'),
                    NumberStyles.Number,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                && parsed == expected)
                return true;
        }

        foreach (var quantityWord in context.Config.Commerce.Conversation.QuantityWords)
        {
            if (quantityWord.Value == expected
                && CommerceConversationMatcher.ContainsPhrase(evidenceOutsideProduct, [quantityWord.Key]))
                return true;
        }

        return expected == 1m
            && CommerceConversationMatcher.ContainsPhrase(
                evidenceOutsideProduct,
                context.Config.Commerce.Conversation.AdditionalRequestPhrases);
    }

    private static bool IsCoveredByCatalogRead(TurnPlan plan, string productText)
    {
        var reference = NormalizeReference(productText);
        if (reference.Length == 0)
            return false;

        return plan.Signals.Any(signal =>
        {
            if (!signal.Type.Equals(CatalogQuerySignal, StringComparison.OrdinalIgnoreCase)
                || !IsCatalogTargetSearch(signal.Value))
                return false;

            return ReadCatalogTargets(signal.Value).Any(query =>
                SameReference(reference, NormalizeReference(query)));
        });
    }

    private static bool IsCatalogTargetSearch(JsonElement value) =>
        value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty("intent", out var intent)
        && intent.ValueKind == JsonValueKind.String
        && intent.GetString()!.Equals("search_target", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> ReadCatalogTargets(JsonElement value)
    {
        if (value.TryGetProperty("target", out var target)
            && target.ValueKind == JsonValueKind.Object
            && target.TryGetProperty("text", out var text)
            && text.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(text.GetString()))
            yield return text.GetString()!;

        if (!value.TryGetProperty("additional_targets", out var additional)
            || additional.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var item in additional.EnumerateArray())
            if (item.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(item.GetString()))
                yield return item.GetString()!;
    }

    private static bool SchemaContainsProperty(JsonElement schema, string property)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return false;
        if (schema.TryGetProperty("properties", out var properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty(property, out _))
            return true;
        return schema.TryGetProperty("anyOf", out var anyOf)
            && anyOf.ValueKind == JsonValueKind.Array
            && anyOf.EnumerateArray().Any(branch => SchemaContainsProperty(branch, property));
    }

    private static bool ResolvesPendingMutation(CartCommandCandidate command, TurnPlanningContext context)
    {
        var pending = PendingCartCommandMemory.Read(context.CurrentFacts);
        if (pending is null)
            return false;

        var messageReference = NormalizeReference(context.LatestUserMessage);
        var commandReference = NormalizeReference(command.ProductText);
        var cancelsPending = command.Operation.Equals(
            CartCommandOperations.CancelPending,
            StringComparison.OrdinalIgnoreCase);

        return pending.Items.Where(item => !item.AlreadyApplied).Any(item =>
            (cancelsPending || item.Command.Quantity == command.Quantity)
            && (cancelsPending
                || item.Command.Operation.Equals(command.Operation, StringComparison.OrdinalIgnoreCase)
                || item.Command.Operation.Equals("add", StringComparison.OrdinalIgnoreCase)
                && command.Operation.Equals("set_quantity", StringComparison.OrdinalIgnoreCase)
                || item.Command.Operation.Equals("set_quantity", StringComparison.OrdinalIgnoreCase)
                && command.Operation.Equals("add", StringComparison.OrdinalIgnoreCase))
            && new[] { item.Command.ProductText, item.OriginalProductText, item.Issue?.ProductText }
                .Concat(item.Issue?.ProductCandidates.Select(candidate => candidate.Name) ?? [])
                .Where(reference => !string.IsNullOrWhiteSpace(reference))
                .Any(reference =>
                    messageReference.Length > 0
                        && SameReference(messageReference, NormalizeReference(reference!))
                    || cancelsPending
                        && commandReference.Length > 0
                        && SameReference(commandReference, NormalizeReference(reference!))));
    }

    private static bool CurrentCartContainsReference(
        string productText,
        IReadOnlyDictionary<string, JsonElement>? structuredContext)
    {
        var reference = NormalizeReference(productText);
        if (reference.Length == 0
            || structuredContext is null
            || !structuredContext.TryGetValue("currentCart", out var cart)
            || cart.ValueKind != JsonValueKind.Object
            || !cart.TryGetProperty("items", out var items)
            || items.ValueKind != JsonValueKind.Array)
            return false;

        return items.EnumerateArray().Any(item =>
            item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty("name", out var name)
            && name.ValueKind == JsonValueKind.String
            && SameReference(reference, NormalizeReference(name.GetString() ?? string.Empty)));
    }

    private static bool SameReference(string left, string right) =>
        left.Equals(right, StringComparison.Ordinal)
        || left.Length >= 3 && right.Contains(left, StringComparison.Ordinal)
        || right.Length >= 3 && left.Contains(right, StringComparison.Ordinal);

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

    [GeneratedRegex(@"^\s*[-*•▪◦]\s*", RegexOptions.CultureInvariant)]
    private static partial Regex BulletPrefixRegex();

    [GeneratedRegex(@"^\s*[-*•▪◦]\s*(?<quantity>\d+(?:[.,]\d+)?)\s*(?:x\b\s*)?(?<product>[^\r\n]+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OrderListLineRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])\d+(?:[.,]\d+)?(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NumericQuantityRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:agreg\p{L}*|anad\p{L}*|incluy\p{L}*|sum\p{L}*)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitMutationVerbRegex();

    [GeneratedRegex(@"(?<![\p{L}\p{N}])(?:tienes?|tienen|hay|disponib\p{L}*|existencia|precio|cuesta|vale|opciones|venden|manejan)(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CatalogInquiryRegex();

    private sealed record CartCommandCandidate(string Operation, string ProductText, decimal? Quantity);
}
