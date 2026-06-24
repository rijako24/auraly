using System.Globalization;
using System.Text;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class PrepareOrderCheckoutTool : IAgentTool
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICheckoutPaymentCoordinator _checkoutPayments;
    private readonly IConversationVerificationService _verifications;

    public PrepareOrderCheckoutTool(
        IUnitOfWork unitOfWork,
        ICheckoutPaymentCoordinator checkoutPayments,
        IConversationVerificationService verifications)
    {
        _unitOfWork = unitOfWork;
        _checkoutPayments = checkoutPayments;
        _verifications = verifications;
    }

    public string Name => "prepare_order_checkout";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.CheckoutPrepare];

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
            "payment_method": { "type": "string" },
            "payment_required": { "type": "boolean" }
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

        var checkout = ctx.Config?.Checkout ?? new CheckoutDefinitions();
        var checkoutMode = checkout.ResolveMode("order");
        if (checkoutMode is null)
        {
            return ToolResultHelper.Error(
                "checkout_mode_missing",
                "Checkout mode 'order' is not configured for this agent.",
                "Add SettingsJson.checkout.modes.order.");
        }

        var paymentMethodInput = Get(arguments, "payment_method") ?? GetFact(ctx, "payment_method");
        var paymentSelection = ResolvePaymentSelection(checkoutMode, arguments, paymentMethodInput);
        if (paymentSelection.MissingPaymentMethod)
            return ToolResultHelper.MissingPrerequisites(["payment_method"]);

        if (paymentSelection.Error is not null)
            return paymentSelection.Error;

        var paymentMethod = paymentSelection.MethodLabel;
        var paymentRequired = paymentSelection.PaymentRequired;
        var templateId = paymentRequired ? checkoutMode.TemplateWithPayment : checkoutMode.TemplateNoPayment;

        if (string.IsNullOrWhiteSpace(templateId))
            return ToolResultHelper.Error("checkout_template_missing", paymentRequired
                ? "Order checkout requires templateWithPayment."
                : "Order checkout without online payment requires templateNoPayment.");

        if (paymentRequired && string.IsNullOrWhiteSpace(checkoutMode.ConfirmationOutcome))
            return ToolResultHelper.Error("checkout_outcome_missing", "Order checkout with online payment requires confirmationOutcome.");

        var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
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

        var payableCents = (long)Math.Round(orderTotal * 100m, MidpointRounding.AwayFromZero);
        if (payableCents <= 0)
            return ToolResultHelper.Error("invalid_order_total", "Order total must be greater than zero to generate payment.");

        var quote = BuildQuote(ctx, checkoutMode, order, items, shippingCost, payableCents, currency, templateId!, paymentRequired);
        var templateData = BuildTemplateData(order, items, shippingCost, currency, city, deliveryAddress, payerName, payerEmail, paymentPhone, ctx.Facts);
        var checkoutSnapshotJson = BuildCheckoutSnapshot(order, items, shippingCost, city, deliveryAddress, payerName, payerEmail, paymentPhone, ctx.Facts);

        string? paymentLink = null;
        Guid? paymentTransactionId = null;
        if (paymentRequired)
        {
            var linkResult = await _checkoutPayments.EnsurePaymentLinkAsync(
                ctx,
                quote,
                paymentPhone,
                checkoutSnapshotJson,
                reservationSnapshot: null,
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

        templateData["payment_method"] = paymentMethod;

        var checkoutToken = ctx.Turn.RegisterFragment(
            "CHECKOUT",
            quote.TemplateId,
            templateData,
            FragmentRenderMode.Exclusive);
        ctx.Turn.MarkCheckoutPrepared();

        var checkoutDependencies = BuildVerificationDependencies(bindings.RequiredFactRoles, roles, ctx.Facts, paymentMethodInput);
        _verifications.Record(
            ctx,
            VerificationFactTypes.CheckoutPrepared,
            checkoutDependencies,
            ttl: null);

        if (!paymentRequired)
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
            payment_required = paymentRequired,
            payment_transaction_id = paymentTransactionId,
            payment_link = paymentLink,
            order_draft_id = order.OrderDraftId,
            order_status = "Draft",
            is_order_confirmed = false
        });
    }

    private static CheckoutQuote BuildQuote(
        AgentToolContext ctx,
        CheckoutModeDefinition mode,
        OrderDraft order,
        IReadOnlyList<OrderDraftItem> items,
        decimal shippingCost,
        long payableCents,
        string currency,
        string templateId,
        bool paymentRequired)
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
            payableCents,
            payableCents,
            currency,
            paymentRequired ? mode.Payment.Type : "none",
            templateId,
            paymentRequired ? mode.ConfirmationOutcome! : string.Empty,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30));
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

        if (!string.IsNullOrWhiteSpace(paymentMethod))
            dependencies["payment_method"] = paymentMethod.Trim();

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

    private sealed record PaymentSelection(
        bool MissingPaymentMethod,
        string? Error,
        string MethodLabel,
        bool PaymentRequired);

    private static PaymentSelection ResolvePaymentSelection(
        CheckoutModeDefinition mode,
        JsonElement args,
        string? rawPaymentMethod)
    {
        if (mode.PaymentMethods.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(rawPaymentMethod))
                return new PaymentSelection(true, null, string.Empty, false);

            var configured = ResolveConfiguredPaymentMethod(mode, rawPaymentMethod);
            if (configured is null)
            {
                var options = DescribeConfiguredPaymentMethods(mode);
                return new PaymentSelection(
                    false,
                    ToolResultHelper.Error(
                        "invalid_payment_method",
                        "Payment method is not configured for this checkout mode.",
                        string.IsNullOrWhiteSpace(options)
                            ? "Ask the customer for a configured payment method."
                            : $"Ask the customer to choose one of the configured payment methods: {options}.",
                        recoverable: true),
                    string.Empty,
                    false);
            }

            return new PaymentSelection(
                false,
                null,
                PaymentMethodLabel(configured.Value.Key, configured.Value.Method),
                configured.Value.Method.PaymentRequired);
        }

        var paymentRequired = ResolvePaymentRequired(args, mode);
        var methodLabel = !string.IsNullOrWhiteSpace(rawPaymentMethod)
            ? rawPaymentMethod.Trim()
            : paymentRequired
                ? "online"
                : "offline";

        return new PaymentSelection(false, null, methodLabel, paymentRequired);
    }

    private static (string Key, OrderCheckoutPaymentMethodDefinition Method)? ResolveConfiguredPaymentMethod(
        CheckoutModeDefinition mode,
        string rawPaymentMethod)
    {
        var normalizedInput = NormalizePaymentMethodToken(rawPaymentMethod);
        if (string.IsNullOrWhiteSpace(normalizedInput))
            return null;

        foreach (var (key, method) in mode.PaymentMethods)
        {
            if (normalizedInput.Equals(NormalizePaymentMethodToken(key), StringComparison.OrdinalIgnoreCase)
                || normalizedInput.Equals(NormalizePaymentMethodToken(method.Label), StringComparison.OrdinalIgnoreCase)
                || (method.Aliases?.Any(alias => normalizedInput.Equals(NormalizePaymentMethodToken(alias), StringComparison.OrdinalIgnoreCase)) ?? false))
            {
                return (key, method);
            }
        }

        return null;
    }

    private static string DescribeConfiguredPaymentMethods(CheckoutModeDefinition mode) =>
        string.Join(", ", mode.PaymentMethods.Select(kvp => PaymentMethodLabel(kvp.Key, kvp.Value)));

    private static string PaymentMethodLabel(string key, OrderCheckoutPaymentMethodDefinition method) =>
        string.IsNullOrWhiteSpace(method.Label) ? key : method.Label.Trim();

    private static bool ResolvePaymentRequired(JsonElement args, CheckoutModeDefinition mode)
    {
        if (ToolResultHelper.TryGetBool(args, "payment_required", out var required))
            return required;

        var hasPaymentTemplate = !string.IsNullOrWhiteSpace(mode.TemplateWithPayment);
        var hasNoPaymentTemplate = !string.IsNullOrWhiteSpace(mode.TemplateNoPayment);
        if (!hasPaymentTemplate && hasNoPaymentTemplate)
            return false;

        return true;
    }

    private static string NormalizePaymentMethodToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }

        return builder.ToString();
    }

    private static string ShortId(Guid id) => id.ToString("N")[..8].ToUpperInvariant();

    private static string Money(decimal amount) => amount.ToString("N0", CultureInfo.InvariantCulture);
}


