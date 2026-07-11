using MimosBabySpa.Application.Agents.Configuration;
using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Commerce;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

[AgentToolMetadata("prepare_order_checkout", Capabilities = new[] { ToolCapabilities.CheckoutPrepare })]
public sealed class PrepareOrderCheckoutTool : IAgentTool
{
private readonly IUnitOfWork _unitOfWork;
    private readonly IProductCatalogAvailabilityService _availability;
    private readonly ICheckoutPaymentCoordinator _checkoutPayments;
    private readonly IConversationFactsService _factsService;
    private readonly IConversationVerificationService _verifications;

    public PrepareOrderCheckoutTool(
        IUnitOfWork unitOfWork,
        IProductCatalogAvailabilityService availability,
        ICheckoutPaymentCoordinator checkoutPayments,
        IConversationFactsService factsService,
        IConversationVerificationService verifications)
    {
        _unitOfWork = unitOfWork;
        _availability = availability;
        _checkoutPayments = checkoutPayments;
        _factsService = factsService;
        _verifications = verifications;
    }

    public string Name => "prepare_order_checkout";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.CheckoutPrepare];

    public IReadOnlyList<string> OperatingGroups => [ToolOperatingGroups.OrderIntake];

    public string Description =>
        "Prepares the current order draft checkout, renders the configured order summary and creates a payment link only when required.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "customer_name": { "type": "string" },
            "customer_email": { "type": "string" },
            "customer_phone": { "type": "string" },
            "customer_document": { "type": "string" },
            "delivery_address": { "type": "string" },
            "notes": { "type": "string" },
            "payment_method": { "type": "string" }
          }
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (ctx.Turn is null)
            return ToolResultHelper.Error("internal_error", "Turn context is not available for template rendering.");

        var order = await _unitOfWork.OrderDrafts.GetActiveByConversationAsync(
            ctx.BusinessId,
            ctx.ConversationId,
            cancellationToken);
        if (order is null)
            return ToolResultHelper.Error("order_draft_missing", "No active order draft was found.");

        var items = await _unitOfWork.OrderDraftItems.GetByDraftIdAsync(ctx.BusinessId, order.OrderDraftId, cancellationToken);
        if (items.Count == 0)
            return ToolResultHelper.MissingPrerequisites(["order_items"]);
        var unavailableItems = await _availability.FindUnavailableDraftItemsAsync(ctx.BusinessId, items, cancellationToken);
        if (unavailableItems.Count > 0)
        {
            return ToolResultHelper.Error("product_inactive", "The order contains an unavailable product and cannot be checked out.");
        }

        var checkout = ctx.Config?.Checkout ?? new CheckoutDefinitions();
        var checkoutMode = checkout.ResolveMode("order");
        if (checkoutMode is null)
        {
            return ToolResultHelper.Error("checkout_mode_missing", "Checkout mode 'order' is not configured for this agent.");
        }

        var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var paymentMethodInput = Get(arguments, "payment_method") ?? CheckoutPaymentFact.Get(ctx, roles);
        var bindings = CheckoutModeBindingDefaults.Resolve(CheckoutKind.Order, checkoutMode);
        var missing = FindMissingRequiredFacts(bindings.RequiredFactRoles, roles, ctx.Facts, arguments);
        if (missing.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. missing]);

        var city = ResolveBinding(roles, ctx.Facts, bindings.SystemFactBindings, CheckoutSystemSlots.City)
            ?? Get(arguments, "city");
        var deliveryAddress = Get(arguments, "delivery_address")
            ?? ResolveBinding(roles, ctx.Facts, bindings.SystemFactBindings, CheckoutSystemSlots.DeliveryAddress);
        var paymentPhone = Get(arguments, "customer_phone")
            ?? ResolveBinding(roles, ctx.Facts, bindings.SystemFactBindings, CheckoutSystemSlots.PaymentPhone);
        var payerName = Get(arguments, "customer_name")
            ?? ResolveBinding(roles, ctx.Facts, bindings.SystemFactBindings, CheckoutSystemSlots.PayerName)
            ?? ctx.Conversation.CustomerName;
        var payerEmail = Get(arguments, "customer_email")
            ?? ResolveBinding(roles, ctx.Facts, bindings.SystemFactBindings, CheckoutSystemSlots.PayerEmail)
            ?? ctx.Conversation.CustomerEmail;
        var customerDocument = Get(arguments, "customer_document")
            ?? GetFact(ctx, "customer_document");
        var notes = Get(arguments, "notes") ?? order.Notes;

        if (string.IsNullOrWhiteSpace(paymentPhone))
            return ToolResultHelper.MissingPrerequisites(["payment_phone"]);

        var shippingCost = ResolveShippingCost(checkoutMode.Shipping, city);
        var orderTotal = order.Subtotal - order.DiscountTotal + order.TaxTotal + shippingCost;
        var totalCents = (long)Math.Round(orderTotal * 100m, MidpointRounding.AwayFromZero);
        if (totalCents <= 0)
            return ToolResultHelper.Error("invalid_order_total", "Order total must be greater than zero.");

        var paymentSelection = CheckoutPaymentSelectionResolver.Resolve(checkoutMode, "order", totalCents, paymentMethodInput);
        if (paymentSelection.MissingPaymentMethod)
            return ToolResultHelper.MissingPrerequisites(["payment_method"]);

        if (paymentSelection.Error is not null)
            return ToolError(paymentSelection.Error);

        await CheckoutPaymentFact.PersistSelectionAsync(_factsService, ctx, roles, paymentSelection, cancellationToken);

        var currency = string.IsNullOrWhiteSpace(checkout.Currency) ? "COP" : checkout.Currency.Trim().ToUpperInvariant();

        order.CustomerNameSnapshot = payerName;
        order.CustomerEmailSnapshot = payerEmail;
        order.CustomerPhoneSnapshot = paymentPhone;
        order.CustomerDocumentSnapshot = customerDocument;
        order.DeliveryAddressSnapshot = deliveryAddress;
        order.Notes = notes;
        order.Currency = currency;
        order.Total = orderTotal;
        order.CustomerConfirmed = false;
        order.UpdatedAt = DateTime.UtcNow;
        order.CustomAttributesJson = BuildOrderCustomAttributes(ctx.Facts, city, shippingCost);
        await _unitOfWork.OrderDrafts.UpdateAsync(order, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var quote = BuildQuote(ctx, order, items, shippingCost, totalCents, paymentSelection, currency);
        var templateData = BuildTemplateData(order, items, shippingCost, currency, city, deliveryAddress, payerName, payerEmail, paymentPhone, ctx.Facts);
        var checkoutSnapshotJson = BuildCheckoutSnapshot(order, items, shippingCost, city, deliveryAddress, payerName, payerEmail, paymentPhone, paymentSelection, ctx.Facts);

        string? paymentLink = null;
        Guid? paymentTransactionId = null;
        if (quote.RequiresManualConfirmation)
        {
            var manualResult = await _checkoutPayments.EnsureManualPaymentAsync(
                ctx,
                quote,
                checkoutSnapshotJson,
                cancellationToken);
            if (!manualResult.Success)
                return ToolResultHelper.Error("manual_payment_failed", manualResult.ErrorMessage ?? "Failed to prepare manual payment confirmation.");

            if (manualResult.Payment is not null && order.PaymentTransactionId != manualResult.Payment.PaymentTransactionId)
            {
                order.PaymentTransactionId = manualResult.Payment.PaymentTransactionId;
                order.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.OrderDrafts.UpdateAsync(order, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            paymentTransactionId = manualResult.Payment?.PaymentTransactionId;
            templateData["payment_pending_manual_confirmation"] = true;
        }
        else if (quote.PayableCents > 0)
        {
            var linkResult = await _checkoutPayments.EnsurePaymentLinkAsync(
                ctx,
                quote,
                paymentPhone,
                checkoutSnapshotJson,
                cancellationToken);
            if (!linkResult.Success)
                return ToolResultHelper.Error("payment_link_failed", linkResult.ErrorMessage ?? "Failed to generate payment link.");

            if (linkResult.Payment is not null && order.PaymentTransactionId != linkResult.Payment.PaymentTransactionId)
            {
                order.PaymentTransactionId = linkResult.Payment.PaymentTransactionId;
                order.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.OrderDrafts.UpdateAsync(order, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            paymentLink = linkResult.LinkUrl;
            paymentTransactionId = linkResult.Payment?.PaymentTransactionId;
            templateData["link_url"] = linkResult.LinkUrl;
        }
        else
        {
            if (order.PaymentTransactionId.HasValue)
            {
                var linkedPayment = await _unitOfWork.PaymentTransactions.GetByIdAsync(
                    order.PaymentTransactionId.Value,
                    cancellationToken);
                if (linkedPayment?.Status == PaymentTransactionStatus.Created
                    && linkedPayment.CheckoutKind == quote.CheckoutKind)
                {
                    order.PaymentTransactionId = null;
                    order.UpdatedAt = DateTime.UtcNow;
                    await _unitOfWork.OrderDrafts.UpdateAsync(order, cancellationToken);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                    ctx.ActivePayment ??= linkedPayment;
                }
            }

            await _checkoutPayments.DiscardActiveCheckoutAsync(
                ctx,
                quote.CheckoutKind,
                cancellationToken);
        }

        templateData["payment_method"] = paymentSelection.MethodKey;
        templateData["payment_method_label"] = paymentSelection.MethodLabel;
        templateData["payment_percentage"] = paymentSelection.PaymentPercentage;

        var checkoutToken = ctx.Turn.RegisterFragment(
            "CHECKOUT",
            quote.TemplateId,
            templateData,
            FragmentRenderMode.Exclusive);
        ctx.Turn.MarkCheckoutPrepared();

        var checkoutDependencies = BuildVerificationDependencies(bindings.RequiredFactRoles, roles, ctx.Facts, paymentSelection.MethodKey);
        _verifications.Record(
            ctx,
            VerificationFactTypes.CheckoutPrepared,
            checkoutDependencies,
            ttl: null);

        if (quote.PayableCents <= 0 && !quote.RequiresManualConfirmation)
        {
            _verifications.Record(
                ctx,
                VerificationFactTypes.CheckoutNoPaymentPrepared,
                checkoutDependencies,
                ttl: null);
        }

        return ToolResultHelper.Ok(new
        {
            checkout_token = checkoutToken,
            checkout_kind = quote.CheckoutKind.ToString(),
            template_id = quote.TemplateId,
            payment_required = quote.PayableCents > 0,
            payment_pending_manual_confirmation = quote.RequiresManualConfirmation,
            payment_transaction_id = paymentTransactionId,
            payment_link = paymentLink,
            order_draft_id = order.OrderDraftId,
            order_status = "Draft",
            is_order_confirmed = false
        });
    }

    private static CheckoutQuote BuildQuote(
        AgentToolContext ctx,
        OrderDraft order,
        IReadOnlyList<OrderDraftItem> items,
        decimal shippingCost,
        long totalCents,
        CheckoutPaymentSelection paymentSelection,
        string currency)
    {
        var lineItems = items
            .Select(i => new CheckoutQuoteLineItem($"{i.ProductNameSnapshot} x{i.Quantity:N0}", i.LineTotal))
            .ToList();
        if (shippingCost > 0)
            lineItems.Add(new CheckoutQuoteLineItem("Envio", shippingCost));

        return new CheckoutQuote(
            ctx.BusinessId,
            ctx.ConversationId,
            CheckoutKind.Order,
            Guid.Empty,
            $"Pedido {ShortId(order.OrderDraftId)}",
            "Order",
            0,
            lineItems,
            totalCents,
            paymentSelection.PayableCents,
            currency,
            paymentSelection.MethodKey,
            paymentSelection.MethodLabel,
            paymentSelection.PaymentPercentage,
            paymentSelection.TemplateId,
            paymentSelection.ConfirmationOutcome,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30))
        {
            RequiresManualConfirmation = paymentSelection.RequiresManualConfirmation,
            ManualExpirationMinutes = paymentSelection.ManualExpirationMinutes
        };
    }

    private static Dictionary<string, object?> BuildTemplateData(
        OrderDraft order,
        IReadOnlyList<OrderDraftItem> items,
        decimal shippingCost,
        string currency,
        string? city,
        string? deliveryAddress,
        string? customerName,
        string? customerEmail,
        string customerPhone,
        IReadOnlyDictionary<string, string> facts)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["order_draft_id"] = order.OrderDraftId,
            ["order_number"] = ShortId(order.OrderDraftId),
            ["subtotal"] = Money(order.Subtotal),
            ["shipping_cost"] = Money(shippingCost),
            ["total"] = Money(order.Total),
            ["total_cents"] = (long)Math.Round(order.Total * 100m, MidpointRounding.AwayFromZero),
            ["currency"] = currency,
            ["city"] = city,
            ["delivery_address"] = deliveryAddress,
            ["customer_name"] = customerName,
            ["customer_email"] = customerEmail,
            ["customer_phone"] = customerPhone,
            ["line_items"] = items.Select(i => (object)new Dictionary<string, object?>
            {
                ["name"] = i.ProductNameSnapshot,
                ["quantity"] = i.Quantity.ToString("N0", CultureInfo.InvariantCulture),
                ["unit_price"] = Money(i.UnitPrice),
                ["line_total"] = Money(i.LineTotal)
            }).ToList()
        };

        foreach (var (key, value) in facts)
            data.TryAdd(key, value);

        return data;
    }

    private static string BuildCheckoutSnapshot(
        OrderDraft order,
        IReadOnlyList<OrderDraftItem> items,
        decimal shippingCost,
        string? city,
        string? deliveryAddress,
        string? customerName,
        string? customerEmail,
        string customerPhone,
        CheckoutPaymentSelection paymentSelection,
        IReadOnlyDictionary<string, string> facts)
    {
        var snapshot = new
        {
            kind = CheckoutKind.Order.ToString(),
            order_draft_id = order.OrderDraftId,
            order_number = ShortId(order.OrderDraftId),
            subtotal = order.Subtotal,
            shipping_cost = shippingCost,
            total = order.Total,
            currency = order.Currency,
            payer_name = customerName,
            payer_email = customerEmail,
            payment_phone = customerPhone,
            payment_method = paymentSelection.MethodKey,
            payment_method_label = paymentSelection.MethodLabel,
            payment_percentage = paymentSelection.PaymentPercentage,
            city,
            delivery_address = deliveryAddress,
            line_items = items.Select(i => new
            {
                order_item_id = i.OrderDraftItemId,
                product_id = i.ProductId,
                sku = i.Sku,
                name = i.ProductNameSnapshot,
                quantity = i.Quantity,
                unit_price = i.UnitPrice,
                line_total = i.LineTotal
            }),
            facts
        };

        return JsonSerializer.Serialize(snapshot);
    }

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

    private static string BuildOrderCustomAttributes(
        IReadOnlyDictionary<string, string> facts,
        string? city,
        decimal shippingCost)
    {
        var custom = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["city"] = city,
            ["shipping_cost"] = shippingCost,
            ["facts"] = facts
        };
        return JsonSerializer.Serialize(custom);
    }

    private static List<string> FindMissingRequiredFacts(
        IReadOnlyDictionary<string, string> required,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        JsonElement arguments)
    {
        var missing = new List<string>();
        foreach (var (label, binding) in required)
        {
            var key = roles.KeyByRole(binding) ?? binding;
            var hasFact = facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);
            var hasArg = (TryGetString(arguments, key, out var argValue)
                    || TryGetString(arguments, label, out argValue)
                    || (label.Equals("payment_phone", StringComparison.OrdinalIgnoreCase)
                        && TryGetString(arguments, "customer_phone", out argValue)))
                && !string.IsNullOrWhiteSpace(argValue);
            if (!hasFact && !hasArg)
                missing.Add(label);
        }

        return missing;
    }

    private static IReadOnlyDictionary<string, string> BuildVerificationDependencies(
        IReadOnlyDictionary<string, string> required,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        string? paymentMethod)
    {
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in required.Values)
        {
            var key = roles.KeyByRole(binding) ?? binding;
            if (!string.IsNullOrWhiteSpace(key))
                dependencies[key] = facts.TryGetValue(key, out var value) ? value : string.Empty;
        }

        CheckoutPaymentFact.AddDependency(dependencies, roles, paymentMethod);

        return dependencies;
    }

    private static string? ResolveBinding(
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        IReadOnlyDictionary<string, string> bindings,
        string slot)
    {
        if (!bindings.TryGetValue(slot, out var binding))
            return null;

        var key = roles.KeyByRole(binding) ?? binding;
        return facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static string? Get(JsonElement args, string property) =>
        TryGetString(args, property, out var value) ? value : null;

    private static bool TryGetString(JsonElement args, string property, out string? value) =>
        ToolResultHelper.TryGetString(args, property, out value);

    private static string? GetFact(AgentToolContext ctx, string key) =>
        ctx.Facts.TryGetValue(key, out var value) ? value : null;

    private static string ToolError(CheckoutPaymentSelectionError error)
    {
        var llm = error.AvailablePaymentMethods is { Count: > 0 }
            ? new
            {
                next_action = "select_payment_method",
                available_payment_methods = error.AvailablePaymentMethods
            }
            : null;

        return llm is null
            ? ToolResultHelper.Error(error.Code, error.Message)
            : ToolResultHelper.ErrorWithLlm(error.Code, error.Message, llm, recoverable: error.Recoverable);
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string Money(decimal amount) => amount.ToString("N0", CultureInfo.InvariantCulture);
}
