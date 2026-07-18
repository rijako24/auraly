using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Catalog;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class ApplyOrderChangesOperation : IAgentOperation
{
    public const string OperationId = "commerce.apply_order_changes";

    private readonly CartCommandBatchProcessor _processor;
    private readonly IConversationFactsService? _facts;
    private readonly IProductAliasService? _aliases;

    public ApplyOrderChangesOperation(CartCommandBatchProcessor processor) => _processor = processor;

    public ApplyOrderChangesOperation(CartCommandBatchProcessor processor, IConversationFactsService facts)
    {
        _processor = processor;
        _facts = facts;
    }

    public ApplyOrderChangesOperation(
        CartCommandBatchProcessor processor,
        IConversationFactsService facts,
        IProductAliasService aliases)
    {
        _processor = processor;
        _facts = facts;
        _aliases = aliases;
    }

    public OperationDescriptor Descriptor { get; } = new(
        OperationId,
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "commands": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "operation": { "type": "string", "enum": ["add", "remove", "set_quantity", "cancel_pending"] },
                  "productText": { "type": "string" },
                  "quantity": { "type": ["number", "null"] },
                  "destinationReference": { "type": ["string", "null"] }
                },
                "required": ["operation", "productText", "quantity", "destinationReference"]
              }
            }
          },
          "required": ["commands"]
        }
        """,
        [
            "cart.applied", "cart.partially_applied", "cart.no_changes", "cart.pending_cancelled",
            "cart.conflicting_commands", "cart.multiple_destinations", "cart.product_not_found",
            "cart.product_suggestion", "cart.product_ambiguous", "cart.product_unavailable",
            "cart.item_not_found_or_ambiguous", "cart.insufficient_stock", "cart.invalid_input",
            "cart.needs_clarification"
        ],
        ["commerce.order_draft.write"],
        [],
        ["order_intake"]);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (!input.TryGetProperty("commands", out var commandsElement) || commandsElement.ValueKind != JsonValueKind.Array)
            return OperationOutcome.Fail("cart.invalid_input", "commands must be an array.", true);

        List<CartCommand>? commands;
        try
        {
            commands = JsonSerializer.Deserialize<List<CartCommand>>(
                commandsElement.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            return OperationOutcome.Fail("cart.invalid_input", exception.Message, true);
        }
        if (commands is null)
            return OperationOutcome.Fail("cart.invalid_input", "commands could not be parsed.", true);

        var session = context.Session ?? BuildSession(context);
        var grounded = ProductSelectionMemory.PreserveCatalogAmbiguity(
            session, session.LatestUserMessage ?? string.Empty, commands);

        var hadPendingFact = session.Facts.ContainsKey(PendingCartCommandMemory.FactKey);
        var pending = PendingCartCommandMemory.Read(session);
        if (pending is null && hadPendingFact && _facts is not null)
            await PendingCartCommandMemory.ClearAsync(_facts, session, cancellationToken);
        var discardedPending = pending?.Items
            .Where(item => commands.Any(command =>
                command.Operation.Equals(CartCommandOperations.CancelPending, StringComparison.OrdinalIgnoreCase)
                && CatalogSearchText.NormalizeCompact(command.ProductText)
                    == CatalogSearchText.NormalizeCompact(item.OriginalProductText)))
            .ToList() ?? [];

        var merge = PendingCartCommandMemory.MergeResolution(session, grounded);
        if (merge.WorkItems.Count == 0)
        {
            if (merge.RemainingItems.Count > 0)
            {
                if (_facts is not null)
                    await PendingCartCommandMemory.SaveAsync(_facts, session, merge.RemainingItems, cancellationToken);
                return PendingOutcome(PendingCartCommandMemory.PrimaryIssue(
                    merge.RemainingItems,
                    session.LatestUserMessage,
                    session.Config?.Commerce.Matching));
            }
            if (merge.CancelledAny)
            {
                if (_facts is not null)
                    await PendingCartCommandMemory.ClearAsync(_facts, session, cancellationToken);
                return OperationOutcome.Ok("cart.pending_cancelled", new
                {
                    discarded_items = discardedPending.Select(item => new
                    {
                        product_text = item.OriginalProductText,
                        recognized_name = item.Issue?.ProductCandidates.FirstOrDefault()?.Name,
                        reason = item.Issue?.Code
                    }).ToList()
                });
            }
        }

        var workCommands = merge.WorkItems.Select(item => item.Command).ToList();
        var mutationKey = BuildMutationKey(session, workCommands);
        var result = await _processor.ApplyAsync(session, workCommands, mutationKey, cancellationToken);
        var presentationRequests = result.AppliedCommands.Select(command =>
        {
            var workItem = merge.WorkItems.FirstOrDefault(item =>
                CatalogSearchText.NormalizeCompact(item.Command.ProductText)
                    == CatalogSearchText.NormalizeCompact(command.ProductText));
            return new CartItemPresentationRequest(
                command,
                workItem?.OriginalProductText ?? command.ProductText);
        }).ToList();
        result = result with
        {
            Snapshot = await CartItemPresentationMemory.UpdateAndDecorateAsync(
                _facts, session, result.Snapshot, presentationRequests, cancellationToken)
        };
        var nextPending = merge.RemainingItems.ToList();
        foreach (var unresolved in result.UnresolvedItems)
        {
            var workItem = FindWorkItem(merge.WorkItems, unresolved.Command);
            nextPending.Add(new PendingCartItem(
                unresolved.Command,
                workItem?.OriginalProductText ?? unresolved.Command.ProductText,
                unresolved.Issue,
                true));
        }
        nextPending = CoalescePending(nextPending);
        if (nextPending.Any(item => item.RequiresResolution))
        {
            foreach (var applied in result.AppliedCommands)
            {
                var appliedCommand = new CartCommand(
                    applied.Operation, applied.Product?.Name ?? applied.ProductText, applied.Quantity, null);
                var workItem = FindWorkItem(merge.WorkItems, appliedCommand);
                nextPending.Add(new PendingCartItem(
                    appliedCommand,
                    workItem?.OriginalProductText ?? applied.ProductText,
                    null,
                    false,
                    true));
            }
        }


        if (!result.Replayed)
            await LearnConfirmedAliasesAsync(session, merge.Confirmations, result.AppliedCommands, cancellationToken);
        if (_facts is not null)
        {
            if (nextPending.Count > 0)
                await PendingCartCommandMemory.SaveAsync(_facts, session, nextPending, cancellationToken);
            else if (pending is not null || hadPendingFact)
                await PendingCartCommandMemory.ClearAsync(_facts, session, cancellationToken);
        }

        if (nextPending.Count > 0)
        {
            var issues = nextPending.Where(item => item.Issue is not null).Select(item => item.Issue!).ToList();
            var canFinalizeWithPending = CanFinalizeWithPending(
                nextPending, result.Snapshot, session.Config?.Commerce.PendingCart);
            if (result.AppliedCommands.Count > 0)
                return ClarificationOutcome(
                    "cart.partially_applied", result.Snapshot, issues, result.AppliedCommands,
                    canFinalizeWithPending, isPendingFollowUp: pending is not null);
            if (pending is not null)
            {
                var primary = PendingCartCommandMemory.PrimaryIssue(
                    nextPending, session.LatestUserMessage, session.Config?.Commerce.Matching);
                return ClarificationOutcome(
                    ToOutcomeCode(primary.Code), result.Snapshot, [primary],
                    canFinalizeWithPending: canFinalizeWithPending,
                    isPendingFollowUp: true);
            }
            if (issues.Count > 1)
                return ClarificationOutcome(
                    "cart.partially_applied", result.Snapshot, issues,
                    canFinalizeWithPending: canFinalizeWithPending);
            return ClarificationOutcome(
                ToOutcomeCode(issues.FirstOrDefault()?.Code), result.Snapshot, issues,
                canFinalizeWithPending: canFinalizeWithPending);
        }

        if (result.Success)
            return OperationOutcome.Ok(result.Code, new
            {
                order = ToOrder(result.Snapshot),
                applied_items = PresentableAppliedItems(result.Snapshot, result.AppliedCommands)
            });
        return FailureOutcome(result);
    }

    private async Task LearnConfirmedAliasesAsync(
        AgentConversationContext session,
        IReadOnlyList<PendingAliasConfirmation> confirmations,
        IReadOnlyList<ResolvedCartCommand> applied,
        CancellationToken cancellationToken)
    {
        if (_aliases is null || confirmations.Count == 0 || applied.Count == 0)
            return;
        foreach (var confirmation in confirmations)
        {
            var command = applied.FirstOrDefault(value => value.Product is not null
                && CatalogSearchText.NormalizeCompact(value.Product.Name)
                    == CatalogSearchText.NormalizeCompact(confirmation.SelectedProductName));
            if (command?.Product is not null)
                await _aliases.LearnConfirmedAsync(session, confirmation.OriginalProductText, command.Product, cancellationToken);
        }
    }

    private static PendingCartWorkItem? FindWorkItem(
        IReadOnlyList<PendingCartWorkItem> workItems,
        CartCommand command) =>
        workItems.FirstOrDefault(item =>
            item.Command.Operation.Equals(command.Operation, StringComparison.OrdinalIgnoreCase)
            && CatalogSearchText.NormalizeCompact(item.Command.ProductText)
                == CatalogSearchText.NormalizeCompact(command.ProductText));

    private static List<PendingCartItem> CoalescePending(IReadOnlyList<PendingCartItem> items)
    {
        var result = new List<PendingCartItem>();
        foreach (var item in items)
        {
            var index = result.FindIndex(existing =>
                existing.Command.Operation.Equals(item.Command.Operation, StringComparison.OrdinalIgnoreCase)
                && CatalogSearchText.NormalizeCompact(existing.OriginalProductText)
                    == CatalogSearchText.NormalizeCompact(item.OriginalProductText));
            if (index < 0)
                result.Add(item);
            else
                result[index] = item;
        }
        return result;
    }

    private static bool CanFinalizeWithPending(
        IReadOnlyList<PendingCartItem> pendingItems,
        OrderSnapshot? snapshot,
        PendingCartPolicy? policy)
    {
        if (snapshot?.Items.Count is not > 0)
            return false;
        var unresolved = pendingItems.Where(item => item.RequiresResolution).ToList();
        if (unresolved.Count == 0)
            return false;
        var discardableCodes = (policy?.DiscardOnFinalizeIssueCodes ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return unresolved.All(item => item.Issue is not null
            && discardableCodes.Contains(item.Issue.Code));
    }

    private static OperationOutcome FailureOutcome(CartCommandBatchResult result) =>
        ClarificationOutcome(result.Code, result.Snapshot, result.Issues);

    private static OperationOutcome PendingOutcome(CartCommandIssue issue) =>
        ClarificationOutcome(ToOutcomeCode(issue.Code), null, [issue]);

    private static OperationOutcome ClarificationOutcome(
        string code,
        OrderSnapshot? snapshot,
        IReadOnlyList<CartCommandIssue> issues,
        IReadOnlyList<ResolvedCartCommand>? appliedCommands = null,
        bool canFinalizeWithPending = false,
        bool isPendingFollowUp = false)
    {
        appliedCommands ??= [];
        var first = issues.FirstOrDefault();
        return OperationOutcome.Fail(
            code,
            code == "cart.partially_applied"
                ? "Every requested order change was evaluated; unresolved products still require clarification."
                : "One or more order changes require clarification.",
            true,
            "order_changes_clarification",
            new
            {
                order = ToOrder(snapshot),
                currency = snapshot?.Currency,
                total = snapshot?.Total,
                items = snapshot?.Items.Select(item => new
                {
                    name = item.ProductName,
                    requested_name = item.RequestedName,
                    quantity = item.Quantity,
                    unit_price = item.UnitPrice,
                    line_total = item.LineTotal
                }).ToList() ?? [],
                issues,
                applied_items = appliedCommands
                    .Select(command => new
                    {
                        operation = command.Operation,
                        name = command.Product?.Name ?? command.ProductText,
                        requested_name = FindPresentableRequestedName(snapshot, command),
                        quantity = command.Quantity,
                        unit_price = command.Product?.EffectiveUnitPrice ?? command.Product?.UnitPrice
                    })
                    .ToList(),
                unavailable_items = issues.Where(issue => issue.Code == "product_unavailable")
                    .Select(issue => new
                    {
                        product_text = issue.ProductText,
                        recognized_name = issue.ProductCandidates.FirstOrDefault()?.Name,
                        description = issue.ProductCandidates.FirstOrDefault() is { } product
                            ? $"{issue.ProductText} — {product.Name}"
                            : issue.ProductText
                    }).ToList(),
                ambiguous_groups = issues.Where(issue => issue.Code == "product_ambiguous")
                    .Select(issue => new
                    {
                        product_text = issue.ProductText,
                        options_text = string.Join("\r\n", issue.ProductCandidates.Select(product =>
                            $"- {product.Name} — {(product.IsAvailable
                                ? $"${product.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)} {product.Currency}"
                                : "sin existencia")}"))
                    }).ToList(),
                suggested_options = issues.Where(issue => issue.Code == "product_suggestion")
                    .SelectMany(issue => issue.ProductCandidates.Select(product => new
                    {
                        product_text = issue.ProductText,
                        name = product.Name,
                        unit_price = product.UnitPrice,
                        currency = product.Currency,
                        availability_text = $"${product.UnitPrice.ToString("N2", CultureInfo.InvariantCulture)} {product.Currency}"
                    })).ToList(),
                insufficient_stock_items = issues.Where(issue => issue.Code == "insufficient_stock")
                    .Select(issue => new
                    {
                        product_text = issue.ProductText,
                        requested_quantity = issue.RequestedQuantity,
                        available_quantity = issue.AvailableQuantity,
                        maximum_command_quantity = issue.MaximumCommandQuantity
                    }).ToList(),
                not_found_items = issues.Where(issue => issue.Code is "product_not_found" or "item_not_found_or_ambiguous")
                    .Select(issue => new { product_text = issue.ProductText }).ToList(),
                display_applied_items = PresentableAppliedItems(snapshot, appliedCommands),
                is_pending_follow_up = isPendingFollowUp,
                applied_item_count = appliedCommands.Count,
                unresolved_item_count = issues.Count,
                item_result_count = appliedCommands.Count + issues.Count,
                can_finalize_with_pending = canFinalizeWithPending,
                product_text = first?.ProductText,
                candidates = first?.Candidates ?? [],
                product_options = first?.ProductCandidates ?? [],
                has_suggestion = first?.Code == "product_suggestion",
                has_no_candidates = first?.ProductCandidates.Count == 0,
                requested_quantity = first?.RequestedQuantity,
                available_quantity = first?.AvailableQuantity,
                existing_cart_quantity = first?.ExistingCartQuantity,
                maximum_command_quantity = first?.MaximumCommandQuantity
            });
    }

    private static IReadOnlyList<object> PresentableAppliedItems(
        OrderSnapshot? snapshot,
        IReadOnlyList<ResolvedCartCommand> commands) =>
        commands
            .Select(command => (object)new
            {
                operation = command.Operation,
                removed = command.Operation.Equals(
                    CartCommandOperations.Remove, StringComparison.OrdinalIgnoreCase),
                name = command.Product?.Name ?? command.ProductText,
                requested_name = FindPresentableRequestedName(snapshot, command),
                quantity = command.Quantity,
                unit_price = command.Product?.EffectiveUnitPrice ?? command.Product?.UnitPrice
            })
            .ToList();
    private static object? ToOrder(OrderSnapshot? snapshot) => snapshot is null ? null : new
    {
        currency = snapshot.Currency,
        subtotal = snapshot.Subtotal,
        discount_total = snapshot.DiscountTotal,
        tax_total = snapshot.TaxTotal,
        total = snapshot.Total,
        items = snapshot.Items.Select(item => new
        {
            name = item.ProductName,
            requested_name = item.RequestedName,
            quantity = item.Quantity,
            unit_price = item.UnitPrice,
            line_total = item.LineTotal
        }).ToList()
    };

    private static string? FindPresentableRequestedName(
        OrderSnapshot? snapshot,
        ResolvedCartCommand command) =>
        FindRequestedName(snapshot, command)
        ?? (command.Operation.Equals(CartCommandOperations.Remove, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(command.ProductText)
                ? command.ProductText
                : null);
    private static string? FindRequestedName(
        OrderSnapshot? snapshot,
        ResolvedCartCommand command)
    {
        if (snapshot is null)
            return null;
        var item = snapshot.Items.FirstOrDefault(candidate =>
            command.OrderItemId.HasValue && candidate.OrderItemId == command.OrderItemId.Value
            || command.Product is not null
                && (command.Product.ProductId.HasValue && candidate.ProductId == command.Product.ProductId
                    || !string.IsNullOrWhiteSpace(command.Product.ExternalProductId)
                        && candidate.ExternalProductId?.Equals(
                            command.Product.ExternalProductId, StringComparison.OrdinalIgnoreCase) == true
                    || !string.IsNullOrWhiteSpace(command.Product.Sku)
                        && candidate.Sku?.Equals(command.Product.Sku, StringComparison.OrdinalIgnoreCase) == true
                    || CatalogSearchText.NormalizeCompact(candidate.ProductName)
                        == CatalogSearchText.NormalizeCompact(command.Product.Name)));
        return item?.RequestedName;
    }
    private static string ToOutcomeCode(string? issueCode) => issueCode switch
    {
        "product_suggestion" => "cart.product_suggestion",
        "product_ambiguous" => "cart.product_ambiguous",
        "product_not_found" => "cart.product_not_found",
        "product_unavailable" => "cart.product_unavailable",
        "insufficient_stock" => "cart.insufficient_stock",
        "item_not_found_or_ambiguous" => "cart.item_not_found_or_ambiguous",
        _ => "cart.needs_clarification"
    };

    private static string? BuildMutationKey(AgentConversationContext context, IReadOnlyList<CartCommand> commands)
    {
        if (string.IsNullOrWhiteSpace(context.ProviderMessageId) || commands.Count == 0)
            return null;
        var canonical = string.Join("\n", commands.Select(command => string.Join('|',
            command.Operation.Trim().ToLowerInvariant(),
            CatalogSearchText.NormalizeCompact(command.ProductText),
            command.Quantity?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            CatalogSearchText.NormalizeCompact(command.DestinationReference))));
        var material = string.Join('|',
            context.BusinessId.ToString("N"),
            context.ConversationId.ToString("N"),
            context.ProviderMessageId.Trim(),
            canonical);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static AgentConversationContext BuildSession(OperationContext context) => new()
    {
        AgentId = context.AgentId,
        BusinessId = context.BusinessId,
        ConversationId = context.ConversationId,
        BusinessToday = context.BusinessToday,
        BusinessNow = context.BusinessNow,
        Config = context.Config,
        ConversationState = context.ConversationState,
        Facts = new Dictionary<string, string>(context.Facts, StringComparer.OrdinalIgnoreCase)
    };
}
