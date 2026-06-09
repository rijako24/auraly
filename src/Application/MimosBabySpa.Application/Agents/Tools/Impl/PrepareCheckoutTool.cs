using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

/// <summary>
/// Resolves tenant checkout data, renders the configured summary and creates a payment link when needed.
/// It does not check availability, assign staff, or create reservations.
/// </summary>
public sealed class PrepareCheckoutTool : IAgentTool
{
    private readonly ReservationPricingResolver _pricing;
    private readonly IAddOnCatalogService _addOnCatalog;
    private readonly IPaymentLinkService _paymentLinks;
    private readonly IPaymentLifecycleService _paymentLifecycle;
    private readonly ICheckoutQuoteService _quotes;
    private readonly IConversationVerificationService _verifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _serviceNameResolver;

    public PrepareCheckoutTool(
        ReservationPricingResolver pricing,
        IAddOnCatalogService addOnCatalog,
        IPaymentLinkService paymentLinks,
        IPaymentLifecycleService paymentLifecycle,
        ICheckoutQuoteService quotes,
        IConversationVerificationService verifications,
        IUnitOfWork unitOfWork,
        ServiceNameResolver serviceNameResolver)
    {
        _pricing = pricing;
        _addOnCatalog = addOnCatalog;
        _paymentLinks = paymentLinks;
        _paymentLifecycle = paymentLifecycle;
        _quotes = quotes;
        _verifications = verifications;
        _unitOfWork = unitOfWork;
        _serviceNameResolver = serviceNameResolver;
    }

    public string Name => "prepare_checkout";

    public IReadOnlyList<string> Capabilities => [ToolCapabilities.CheckoutPrepare];

    public string Description =>
        "Resolves an authoritative checkout from catalog data, tenant checkout settings and current facts; " +
        "then renders the summary and creates a payment link when payment is required. Does not check availability.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string", "description": "Exact service name from the catalog. Optional when booking.service fact is already set." },
            "add_ons": { "type": "string", "description": "Comma-separated add-on names, optional." }
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

        var roles = new FactRoleIndex(ctx.Config?.FactSchema ?? []);
        var quoteResult = await BuildQuoteAsync(arguments, ctx, roles, cancellationToken);
        if (quoteResult.Error is not null)
            return quoteResult.Error;

        var quote = quoteResult.Quote!;
        var missing = FindMissingRequiredFacts(quote, roles, ctx.Facts);
        if (missing.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. missing]);

        var templateData = BuildTemplateData(quote, roles, ctx.Facts);
        string? linkUrl = null;
        PaymentTransaction? payment = null;

        if (quote.PayableCents > 0)
        {
            var paymentPhone = ResolveSystemFact(quote, roles, ctx.Facts, CheckoutSystemSlots.PaymentPhone);
            if (string.IsNullOrWhiteSpace(paymentPhone))
            {
                return ToolResultHelper.Error(
                    "payment_phone_missing",
                    "Checkout requires payment but no payment phone binding was resolved.",
                    $"Configure a factSchema entry with role 'customer.phone' or override the advanced checkout binding for {CheckoutSystemSlots.PaymentPhone}.");
            }

            var snapshotResult = BuildCheckoutSnapshot(quote, roles, ctx);
            if (snapshotResult.Error is not null)
                return snapshotResult.Error;

            var linkResult = await EnsurePaymentLinkAsync(
                ctx,
                quote,
                paymentPhone,
                snapshotResult.CheckoutSnapshotJson!,
                snapshotResult.ReservationSnapshot,
                cancellationToken);

            if (linkResult.Error is not null)
                return linkResult.Error;

            linkUrl = linkResult.LinkUrl;
            payment = linkResult.Payment;
            templateData["link_url"] = linkUrl;
        }

        var checkoutToken = ctx.Turn.RegisterFragment(
            "CHECKOUT",
            quote.TemplateId,
            templateData,
            FragmentRenderMode.Exclusive);
        ctx.Turn.MarkCheckoutPrepared();

        var checkoutDependencies = BuildVerificationDependencies(quote, roles, ctx.Facts);
        _verifications.Record(
            ctx,
            VerificationFactTypes.CheckoutPrepared,
            checkoutDependencies,
            ttl: null);

        if (quote.CheckoutKind == CheckoutKind.Reservation && quote.PayableCents <= 0)
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
            payment_transaction_id = payment?.PaymentTransactionId,
            is_booking_confirmed = false
        });
    }

    private async Task<(CheckoutQuote? Quote, string? Error)> BuildQuoteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        FactRoleIndex roles,
        CancellationToken cancellationToken)
    {
        if (!ToolResultHelper.TryGetString(arguments, "service", out var serviceName))
            serviceName = roles.GetByRole(ctx.Facts, "booking.service")
                ?? ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service);

        if (string.IsNullOrWhiteSpace(serviceName))
            return (null, ToolResultHelper.MissingPrerequisites(["service"]));

        ToolResultHelper.TryGetString(arguments, "add_ons", out var addOns);
        addOns ??= roles.GetByRole(ctx.Facts, "booking.addons")
            ?? ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.AddOns);

        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(ctx.BusinessId, serviceName);
        if (service is null)
        {
            var canonicalServiceName = await _serviceNameResolver.ResolveAsync(ctx.BusinessId, serviceName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(canonicalServiceName))
            {
                service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(ctx.BusinessId, canonicalServiceName);
            }
        }

        if (service is null)
        {
            return (null, ToolResultHelper.Error(
                "service_not_found",
                $"Service '{serviceName}' was not found in the catalog.",
                "Use the canonical service name already selected if it can be resolved. Only call get_service_catalog if the service is genuinely unknown or ambiguous."));
        }

        if (!string.IsNullOrWhiteSpace(addOns))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                ctx.BusinessId, service.ServiceName, addOns, cancellationToken);
            if (!validation.IsValid)
            {
                return (null, ToolResultHelper.Error(
                    "invalid_add_ons",
                    validation.ErrorMessage ?? "Invalid add-on selection.",
                    validation.Hint));
            }

            addOns = validation.NormalizedCsv;
        }

        var checkout = ctx.Config?.Checkout ?? new CheckoutDefinitions();
        var checkoutKind = ResolveCheckoutKind(service);
        var checkoutKindText = checkoutKind.ToString().ToLowerInvariant();

        var checkoutMode = checkout.ResolveMode(checkoutKindText);
        if (checkoutMode is null)
        {
            return (null, ToolResultHelper.Error(
                "checkout_mode_missing",
                $"Checkout mode '{checkoutKindText}' is not configured for this agent.",
                "Add the mode to SettingsJson.checkout.modes."));
        }

        var pricing = await _pricing.ResolveAsync(
            ctx.BusinessId,
            BuildPricingItems(service.ServiceName, addOns),
            cancellationToken);
        if (pricing is null)
            return (null, ToolResultHelper.Error("pricing_failed", "Could not resolve pricing from the catalog."));

        var totalCents = (long)(pricing.Total * 100);
        var currency = string.IsNullOrWhiteSpace(checkout.Currency) ? "COP" : checkout.Currency.Trim().ToUpperInvariant();

        var payableCents = ResolvePayableCents(totalCents, checkoutMode.Payment);
        var templateId = payableCents > 0 ? checkoutMode.TemplateWithPayment : checkoutMode.TemplateNoPayment;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return (null, ToolResultHelper.Error(
                "checkout_template_missing",
                $"Checkout mode '{checkoutKindText}' has no template configured for this payment case.",
                "Set templateWithPayment or templateNoPayment in SettingsJson.checkout.modes."));
        }

        if (payableCents > 0 && string.IsNullOrWhiteSpace(checkoutMode.ConfirmationOutcome))
        {
            return (null, ToolResultHelper.Error(
                "checkout_outcome_missing",
                $"Checkout mode '{checkoutKindText}' requires confirmationOutcome for paid checkouts.",
                "Set confirmationOutcome in SettingsJson.checkout.modes."));
        }

        var bindings = CheckoutModeBindingDefaults.Resolve(checkoutKind, checkoutMode);
        var quote = new CheckoutQuote(
            ctx.BusinessId,
            ctx.ConversationId,
            checkoutKind,
            service.ServiceId,
            service.ServiceName,
            service.ServiceCategory?.Name,
            service.DurationMinutes > 0 ? service.DurationMinutes : 60,
            pricing.LineItems.Select(li => new CheckoutQuoteLineItem(li.Name, li.Price)).ToList(),
            totalCents,
            payableCents,
            currency,
            checkoutMode.Payment.Type,
            templateId,
            checkoutMode.ConfirmationOutcome ?? string.Empty,
            new Dictionary<string, string>(bindings.RequiredFactRoles, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(bindings.SystemFactBindings, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(bindings.TemplateFactBindings, StringComparer.OrdinalIgnoreCase),
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30));

        return (quote, null);
    }

    private static IReadOnlyDictionary<string, string?> BuildPricingItems(string service, string? addOns)
    {
        var items = new Dictionary<string, string?> { ["service"] = service };
        if (!string.IsNullOrWhiteSpace(addOns))
            items["add_ons"] = addOns;
        return items;
    }

    private static CheckoutKind ResolveCheckoutKind(Service service) =>
        service.FulfillmentKind == ServiceFulfillmentKind.Enrollment
            ? CheckoutKind.Enrollment
            : CheckoutKind.Reservation;

    private static long ResolvePayableCents(
        long totalCents,
        CheckoutPaymentDefinition payment)
    {
        if (payment.Type.Equals("none", StringComparison.OrdinalIgnoreCase))
            return 0;

        if (payment.Type.Equals("full", StringComparison.OrdinalIgnoreCase))
            return totalCents;

        if (payment.Type.Equals("percentage", StringComparison.OrdinalIgnoreCase))
            return totalCents * Math.Clamp(payment.Percentage ?? 0, 0, 100) / 100;

        if (payment.Type.Equals("deposit", StringComparison.OrdinalIgnoreCase))
            return totalCents * Math.Clamp(payment.Percentage ?? 0, 0, 100) / 100;

        return 0;
    }

    private async Task<(string? LinkUrl, PaymentTransaction? Payment, string? Error)> EnsurePaymentLinkAsync(
        AgentToolContext ctx,
        CheckoutQuote quote,
        string phone,
        string checkoutSnapshotJson,
        ReservationIntentSnapshot? reservationSnapshot,
        CancellationToken cancellationToken)
    {
        var activePayment = ctx.ActivePayment
            ?? await _paymentLifecycle.GetActiveByConversationAsync(ctx.ConversationId, cancellationToken);
        var quoteHash = _quotes.ComputeHash(quote);

        if (activePayment?.LinkUrl is not null
            && activePayment.ExpiresAt.HasValue
            && activePayment.ExpiresAt.Value > DateTime.UtcNow
            && activePayment.CheckoutKind == quote.CheckoutKind
            && string.Equals(activePayment.QuoteHash, quoteHash, StringComparison.Ordinal))
        {
            ctx.ActivePayment = activePayment;
            return (activePayment.LinkUrl, activePayment, null);
        }

        PaymentTransaction? supersededPayment = null;
        if (activePayment is not null && activePayment.Status == PaymentTransactionStatus.Created)
            supersededPayment = activePayment;

        var result = await _paymentLinks.GenerateAnticipoLinkAsync(
            new PaymentLinkRequest(
                ctx.BusinessId,
                ctx.ConversationId,
                phone,
                quote.ServiceName,
                quote.PayableCents,
                quote.Currency,
                ExpirationMinutes: 60),
            cancellationToken);

        if (!result.Success)
        {
            return (null, null, ToolResultHelper.Error(
                "payment_link_failed",
                result.ErrorMessage ?? "Failed to generate payment link."));
        }

        var payment = await _paymentLifecycle.CreatePendingCheckoutAsync(
            ctx.BusinessId,
            ctx.ConversationId,
            quote.CheckoutKind,
            checkoutSnapshotJson,
            quoteHash,
            quote.ConfirmationOutcome,
            result.PaymentReferenceId!,
            result.PaymentLinkUrl!,
            quote.PayableCents,
            quote.Currency,
            result.ExpiresAt ?? DateTime.UtcNow.AddHours(1),
            reservationSnapshot,
            cancellationToken);

        if (supersededPayment is not null)
            await _paymentLifecycle.MarkSupersededAsync(supersededPayment, payment.PaymentTransactionId, cancellationToken);

        ctx.ActivePayment = payment;
        return (payment.LinkUrl, payment, null);
    }

    private static Dictionary<string, object?> BuildTemplateData(
        CheckoutQuote quote,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["checkout_kind"] = quote.CheckoutKind.ToString(),
            ["service_name"] = quote.ServiceName,
            ["service_category"] = quote.ServiceCategory,
            ["total"] = (quote.TotalCents / 100m).ToString("N0", CultureInfo.InvariantCulture),
            ["total_cents"] = quote.TotalCents,
            ["payable"] = (quote.PayableCents / 100m).ToString("N0", CultureInfo.InvariantCulture),
            ["payable_cents"] = quote.PayableCents,
            ["currency"] = quote.Currency,
            ["payment_type"] = quote.PaymentType,
            ["line_items"] = quote.LineItems.Select(li => (object)new Dictionary<string, object?>
            {
                ["name"] = li.Name,
                ["price"] = li.Price.ToString("N0", CultureInfo.InvariantCulture)
            }).ToList(),
            ["service_price"] = (quote.LineItems.FirstOrDefault()?.Price ?? quote.TotalCents / 100m)
                .ToString("N0", CultureInfo.InvariantCulture),
            ["addons"] = quote.LineItems
                .Skip(1)
                .Select(li => (object)new Dictionary<string, object?>
                {
                    ["name"] = li.Name,
                    ["price"] = li.Price.ToString("N0", CultureInfo.InvariantCulture)
                })
                .ToList(),
            ["deposit"] = (quote.PayableCents / 100m).ToString("N0", CultureInfo.InvariantCulture),
            ["deposit_pct"] = quote.TotalCents > 0
                ? (int)Math.Round(quote.PayableCents * 100m / quote.TotalCents)
                : 0
        };

        foreach (var (key, value) in facts)
            data.TryAdd(key, value);

        foreach (var (templateField, binding) in quote.TemplateFactBindings)
            data[templateField] = ResolveBinding(roles, facts, binding);

        if (data.TryGetValue("date_formatted", out var rawDate)
            && rawDate is string dateText
            && AgentDateRules.TryParseDate(dateText, out var parsedDate))
        {
            data["date_formatted"] = parsedDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }

        return data;
    }

    private static List<string> FindMissingRequiredFacts(
        CheckoutQuote quote,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts)
    {
        var missing = new List<string>();
        foreach (var (label, binding) in quote.RequiredFactRoles)
        {
            if (string.IsNullOrWhiteSpace(ResolveBinding(roles, facts, binding)))
                missing.Add(label);
        }

        return missing;
    }

    private static IReadOnlyDictionary<string, string> BuildVerificationDependencies(
        CheckoutQuote quote,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts)
    {
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in quote.RequiredFactRoles.Values)
        {
            var key = roles.KeyByRole(binding) ?? binding;
            if (string.IsNullOrWhiteSpace(key))
                continue;

            dependencies[key] = facts.TryGetValue(key, out var value) ? value : string.Empty;
        }

        return dependencies;
    }

    private static string? ResolveSystemFact(
        CheckoutQuote quote,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        string slot)
    {
        return quote.SystemFactBindings.TryGetValue(slot, out var binding)
            ? ResolveBinding(roles, facts, binding)
            : null;
    }

    private static string? ResolveBinding(
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        string binding)
    {
        var key = roles.KeyByRole(binding) ?? binding;
        return facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private static (string? CheckoutSnapshotJson, ReservationIntentSnapshot? ReservationSnapshot, string? Error) BuildCheckoutSnapshot(
        CheckoutQuote quote,
        FactRoleIndex roles,
        AgentToolContext ctx)
    {
        var system = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in quote.SystemFactBindings.Keys)
            system[slot] = ResolveSystemFact(quote, roles, ctx.Facts, slot);

        var snapshot = new
        {
            kind = quote.CheckoutKind.ToString(),
            service_id = quote.ServiceId,
            service_name = quote.ServiceName,
            service_category = quote.ServiceCategory,
            payer_name = system.GetValueOrDefault(CheckoutSystemSlots.PayerName),
            payment_phone = system.GetValueOrDefault(CheckoutSystemSlots.PaymentPhone),
            payer_email = system.GetValueOrDefault(CheckoutSystemSlots.PayerEmail),
            fixed_schedule = system.GetValueOrDefault(CheckoutSystemSlots.FixedSchedule),
            reservation_date = system.GetValueOrDefault(CheckoutSystemSlots.ReservationDate),
            reservation_time = system.GetValueOrDefault(CheckoutSystemSlots.ReservationTime),
            facts = ctx.Facts
        };

        ReservationIntentSnapshot? reservationSnapshot = null;
        if (quote.CheckoutKind == CheckoutKind.Reservation)
        {
            var dateStr = system.GetValueOrDefault(CheckoutSystemSlots.ReservationDate);
            var timeStr = system.GetValueOrDefault(CheckoutSystemSlots.ReservationTime);

            if (!AgentDateRules.TryParseDate(dateStr, out var date))
                return (null, null, ToolResultHelper.Error("invalid_reservation_date", "Reservation date binding is missing or invalid."));
            if (!TimeOnly.TryParse(timeStr, out var time))
                return (null, null, ToolResultHelper.Error("invalid_reservation_time", "Reservation time binding is missing or invalid."));

            reservationSnapshot = new ReservationIntentSnapshot(
                quote.ServiceId,
                quote.ServiceName,
                date.ToDateTime(time),
                quote.DurationMinutes,
                PreferredEmployeeId: null,
                system.GetValueOrDefault(CheckoutSystemSlots.PayerName),
                system.GetValueOrDefault(CheckoutSystemSlots.PayerEmail),
                system.GetValueOrDefault(CheckoutSystemSlots.PaymentPhone),
                [],
                JsonSerializer.Serialize(ctx.Facts));
        }

        return (JsonSerializer.Serialize(snapshot), reservationSnapshot, null);
    }
}
