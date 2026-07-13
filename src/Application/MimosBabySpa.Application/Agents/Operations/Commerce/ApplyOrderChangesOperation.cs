using System.Text.Json;
using MimosBabySpa.Application.Agents.Operations.Support;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Operations.Commerce;

public sealed class ApplyOrderChangesOperation : IAgentOperation
{
    public const string OperationId = "commerce.apply_order_changes";

    private readonly CartCommandBatchProcessor _processor;
    private readonly IConversationFactsService? _facts;

    public ApplyOrderChangesOperation(CartCommandBatchProcessor processor) => _processor = processor;

    public ApplyOrderChangesOperation(
        CartCommandBatchProcessor processor,
        IConversationFactsService facts)
    {
        _processor = processor;
        _facts = facts;
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
            "cart.applied",
            "cart.no_changes",
            "cart.pending_cancelled",
            "cart.conflicting_commands",
            "cart.multiple_destinations",
            "cart.product_not_found",
            "cart.product_ambiguous",
            "cart.item_not_found_or_ambiguous",
            "cart.insufficient_stock",
            "cart.invalid_input"
        ],
        ["commerce.order_draft.write"],
        [],
        ["order_intake"]);

    public async Task<OperationOutcome> ExecuteAsync(
        JsonElement input,
        OperationContext context,
        CancellationToken cancellationToken = default)
    {
        if (!input.TryGetProperty("commands", out var commandsElement)
            || commandsElement.ValueKind != JsonValueKind.Array)
            return OperationOutcome.Fail("cart.invalid_input", "commands must be an array.", true);

        List<CartCommand>? commands;
        try
        {
            commands = JsonSerializer.Deserialize<List<CartCommand>>(
                commandsElement.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            return OperationOutcome.Fail("cart.invalid_input", exception.Message, true);
        }

        if (commands is null)
            return OperationOutcome.Fail("cart.invalid_input", "commands could not be parsed.", true);

        var session = context.Session ?? new AgentConversationContext
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

        var groundedCommands = ProductSelectionMemory.PreserveCatalogAmbiguity(
            session,
            session.LatestUserMessage ?? string.Empty,
            commands);
        var hadPendingMemory = session.Facts.ContainsKey(PendingCartCommandMemory.FactKey);
        var pending = PendingCartCommandMemory.Read(session);
        if (pending is null && hadPendingMemory && _facts is not null)
            await PendingCartCommandMemory.ClearAsync(_facts, session, cancellationToken);
        var merge = PendingCartCommandMemory.MergeResolution(session, groundedCommands);
        var effectiveCommands = merge.Commands;
        if (pending is not null && !merge.Resolved)
        {
            if (_facts is not null)
            {
                var accumulated = PendingCartCommandMemory.AccumulateUnresolved(pending, groundedCommands);
                await PendingCartCommandMemory.SaveAsync(
                    _facts,
                    session,
                    accumulated,
                    PendingIssue(pending),
                    cancellationToken);
            }
            return AmbiguousOutcome(PendingIssue(pending));
        }

        if (pending is not null && merge.Resolved && effectiveCommands.Count == 0)
        {
            if (_facts is not null)
                await PendingCartCommandMemory.ClearAsync(_facts, session, cancellationToken);
            return OperationOutcome.Ok("cart.pending_cancelled", new
            {
                product_text = pending.AmbiguousProductText
            });
        }

        var result = await _processor.ApplyAsync(session, effectiveCommands, cancellationToken);
        if (result.Success && _facts is not null && pending is not null)
            await PendingCartCommandMemory.ClearAsync(_facts, session, cancellationToken);
        if (!result.Success
            && _facts is not null
            && result.Code is "cart.product_ambiguous" or "cart.product_not_found"
            && result.Issues.FirstOrDefault() is { } ambiguousIssue)
        {
            await PendingCartCommandMemory.SaveAsync(
                _facts,
                session,
                effectiveCommands,
                ambiguousIssue,
                cancellationToken);
        }

        return result.Success
            ? OperationOutcome.Ok(result.Code, new
            {
                order = result.Snapshot is null ? null : new
                {
                    currency = result.Snapshot.Currency,
                    subtotal = result.Snapshot.Subtotal,
                    discount_total = result.Snapshot.DiscountTotal,
                    tax_total = result.Snapshot.TaxTotal,
                    total = result.Snapshot.Total,
                    items = result.Snapshot.Items.Select(item => new
                    {
                        name = item.ProductName,
                        quantity = item.Quantity,
                        unit_price = item.UnitPrice,
                        line_total = item.LineTotal
                    }).ToList()
                }
            })
            : FailureOutcome(result);
    }

    private static OperationOutcome FailureOutcome(CartCommandBatchResult result) =>
        result.Code == "cart.product_ambiguous" && result.Issues.FirstOrDefault() is { } issue
            ? AmbiguousOutcome(issue)
            : OperationOutcome.Fail(
                result.Code,
                result.Code == "cart.multiple_destinations"
                    ? "Only one delivery address can apply to the active order. Ask which provided address should be used for the whole order; no changes were applied."
                    : "The requested order changes could not be applied atomically.",
                true,
                "order_changes_clarification",
                new
                {
                    issues = result.Issues,
                    product_text = result.Issues.FirstOrDefault()?.ProductText,
                    candidates = result.Issues.FirstOrDefault()?.Candidates ?? [],
                    product_options = result.Issues.FirstOrDefault()?.ProductCandidates ?? [],
                    requested_quantity = result.Issues.FirstOrDefault()?.RequestedQuantity,
                    available_quantity = result.Issues.FirstOrDefault()?.AvailableQuantity,
                    existing_cart_quantity = result.Issues.FirstOrDefault()?.ExistingCartQuantity,
                    maximum_command_quantity = result.Issues.FirstOrDefault()?.MaximumCommandQuantity
                });

    private static CartCommandIssue PendingIssue(PendingCartCommandBatch pending) =>
        new(
            "product_ambiguous",
            pending.AmbiguousProductText,
            pending.ProductCandidates.Select(candidate => candidate.Name).ToList())
        {
            ProductCandidates = pending.ProductCandidates
        };

    private static OperationOutcome AmbiguousOutcome(CartCommandIssue issue) =>
        OperationOutcome.Fail(
            "cart.product_ambiguous",
            "The requested order changes could not be applied atomically.",
            true,
            "order_changes_clarification",
            new
            {
                issues = new[] { issue },
                product_text = issue.ProductText,
                candidates = issue.Candidates,
                product_options = issue.ProductCandidates
            });
}