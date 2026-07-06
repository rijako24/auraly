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
[AgentToolMetadata("prepare_checkout", Capabilities = new[] { ToolCapabilities.CheckoutPrepare })]
public sealed class PrepareCheckoutTool : IAgentTool
{
    private readonly ReservationPricingResolver _pricing;
    private readonly IAddOnCatalogService _addOnCatalog;
    private readonly ICheckoutPaymentCoordinator _checkoutPayments;
    private readonly IConversationFactsService _factsService;
    private readonly IConversationVerificationService _verifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _serviceNameResolver;

    public PrepareCheckoutTool(
        ReservationPricingResolver pricing,
        IAddOnCatalogService addOnCatalog,
        ICheckoutPaymentCoordinator checkoutPayments,
        IConversationFactsService factsService,
        IConversationVerificationService verifications,
        IUnitOfWork unitOfWork,
        ServiceNameResolver serviceNameResolver)
    {
        _pricing = pricing;
        _addOnCatalog = addOnCatalog;
        _checkoutPayments = checkoutPayments;
        _factsService = factsService;
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
            "add_ons": { "type": "string", "description": "Comma-separated add-on names, optional." },
            "payment_method": { "type": "string", "description": "Configured payment method key or alias. Optional when the checkout mode has a single method." }
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

        var availabilityError = EnsureReservationAvailabilityVerified(quote, roles, ctx);
        if (availabilityError is not null)
            return availabilityError;

        await CheckoutPaymentFact.PersistSelectionAsync(_factsService, ctx, roles, quote, cancellationToken);

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

            var linkResult = await _checkoutPayments.EnsurePaymentLinkAsync(
                ctx,
                quote,
                paymentPhone,
                snapshotResult.CheckoutSnapshotJson!,
                cancellationToken);

            if (!linkResult.Success)
                return ToolResultHelper.Error("payment_link_failed", linkResult.ErrorMessage ?? "Failed to generate payment link.");

            linkUrl = linkResult.LinkUrl;
            payment = linkResult.Payment;
            templateData["link_url"] = linkUrl;
        }
        else
        {
            await _checkoutPayments.DiscardActiveCheckoutAsync(ctx, quote.CheckoutKind, cancellationToken);
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
                    validation.ErrorCode ?? "invalid_add_ons",
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
        var paymentMethodInput = Get(arguments, "payment_method") ?? CheckoutPaymentFact.Get(ctx, roles);
        var paymentSelection = CheckoutPaymentSelectionResolver.Resolve(checkoutMode, checkoutKindText, totalCents, paymentMethodInput);
        if (paymentSelection.MissingPaymentMethod)
            return (null, ToolResultHelper.MissingPrerequisites(["payment_method"]));

        if (paymentSelection.Error is not null)
            return (null, ToolError(paymentSelection.Error));

        var bindings = CheckoutModeBindingDefaults.Resolve(checkoutKind, checkoutMode);
        var quote = new CheckoutQuote(
            ctx.BusinessId,
            ctx.ConversationId,
            checkoutKind,
            service.ServiceId,
            service.ServiceName,
            service.ServiceCategory?.Name,
            service.DurationMinutes > 0 ? service.DurationMinutes : 60,
            pricing.LineItems.Select(li => new CheckoutQuoteLineItem(li.Name, li.Price, li.IncludeInCheckoutTotal)).ToList(),
            totalCents,
            paymentSelection.PayableCents,
            currency,
            paymentSelection.MethodKey,
            paymentSelection.MethodLabel,
            paymentSelection.PaymentPercentage,
            paymentSelection.TemplateId,
            paymentSelection.ConfirmationOutcome,
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
            ["payment_method"] = quote.PaymentMethodKey,
            ["payment_method_label"] = quote.PaymentMethodLabel,
            ["payment_percentage"] = quote.PaymentPercentage,
            ["line_items"] = quote.LineItems.Select(li => (object)new Dictionary<string, object?>
            {
                ["name"] = li.Name,
                ["price"] = li.Price.ToString("N0", CultureInfo.InvariantCulture),
                ["include_in_checkout_total"] = li.IncludeInCheckoutTotal,
                ["checkout_note"] = li.IncludeInCheckoutTotal
                    ? string.Empty
                    : " (valor informativo; no incluido en el anticipo)"
            }).ToList(),
            ["service_price"] = (quote.LineItems.FirstOrDefault()?.Price ?? quote.TotalCents / 100m)
                .ToString("N0", CultureInfo.InvariantCulture),
            ["addons"] = quote.LineItems
                .Skip(1)
                .Select(li => (object)new Dictionary<string, object?>
                {
                    ["name"] = li.Name,
                    ["price"] = li.Price.ToString("N0", CultureInfo.InvariantCulture),
                    ["include_in_checkout_total"] = li.IncludeInCheckoutTotal,
                    ["checkout_note"] = li.IncludeInCheckoutTotal
                        ? string.Empty
                        : " (valor informativo; no incluido en el anticipo)"
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

        CheckoutPaymentFact.AddDependency(dependencies, roles, quote.PaymentMethodKey);
        return dependencies;
    }

    private string? EnsureReservationAvailabilityVerified(
        CheckoutQuote quote,
        FactRoleIndex roles,
        AgentToolContext ctx)
    {
        if (quote.CheckoutKind != CheckoutKind.Reservation)
            return null;

        var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
        var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;

        var service = quote.ServiceName;
        var date = ResolveSystemFact(quote, roles, ctx.Facts, CheckoutSystemSlots.ReservationDate);
        var time = ResolveSystemFact(quote, roles, ctx.Facts, CheckoutSystemSlots.ReservationTime);

        if (string.IsNullOrWhiteSpace(service)
            || string.IsNullOrWhiteSpace(date)
            || string.IsNullOrWhiteSpace(time))
        {
            return ToolResultHelper.Error(
                "availability_verification_missing",
                "Reservation checkout requires service, date and time before preparing payment.",
                "Collect service, date and time, then call check_availability for that exact slot.",
                recoverable: true);
        }

        var dependencies = VerificationSnapshot.FromValues(
            new KeyValuePair<string, string>(serviceKey, service),
            new KeyValuePair<string, string>(dateKey, date),
            new KeyValuePair<string, string>(timeKey, time));

        if (_verifications.IsActive(ctx.ConversationState, VerificationFactTypes.AvailabilityChecked, dependencies))
            return null;

        return ToolResultHelper.Error(
            "availability_verification_stale",
            "Availability has not been verified for the exact service, date and time that would be used for checkout.",
            "Call check_availability again using the same service, date and time before prepare_checkout.",
            recoverable: true);
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

    private static string? Get(JsonElement args, string property) =>
        ToolResultHelper.TryGetString(args, property, out var value) ? value : null;

    private static string ToolError(CheckoutPaymentSelectionError error) =>
        ToolResultHelper.Error(error.Code, error.Message, error.Hint, error.Recoverable);

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

    private static (string? CheckoutSnapshotJson, string? Error) BuildCheckoutSnapshot(
        CheckoutQuote quote,
        FactRoleIndex roles,
        AgentToolContext ctx)
    {
        var system = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var slot in quote.SystemFactBindings.Keys)
            system[slot] = ResolveSystemFact(quote, roles, ctx.Facts, slot);

        if (quote.CheckoutKind == CheckoutKind.Reservation)
        {
            var dateStr = system.GetValueOrDefault(CheckoutSystemSlots.ReservationDate);
            var timeStr = system.GetValueOrDefault(CheckoutSystemSlots.ReservationTime);

            if (!AgentDateRules.TryParseDate(dateStr, out _))
                return (null, ToolResultHelper.Error("invalid_reservation_date", "Reservation date binding is missing or invalid."));
            if (!TimeOnly.TryParse(timeStr, out _))
                return (null, ToolResultHelper.Error("invalid_reservation_time", "Reservation time binding is missing or invalid."));
        }

        var snapshot = new
        {
            kind = quote.CheckoutKind.ToString(),
            service_id = quote.ServiceId,
            service_name = quote.ServiceName,
            service_category = quote.ServiceCategory,
            duration_minutes = quote.DurationMinutes,
            payer_name = system.GetValueOrDefault(CheckoutSystemSlots.PayerName),
            payment_phone = system.GetValueOrDefault(CheckoutSystemSlots.PaymentPhone),
            payer_email = system.GetValueOrDefault(CheckoutSystemSlots.PayerEmail),
            fixed_schedule = system.GetValueOrDefault(CheckoutSystemSlots.FixedSchedule),
            reservation_date = system.GetValueOrDefault(CheckoutSystemSlots.ReservationDate),
            reservation_time = system.GetValueOrDefault(CheckoutSystemSlots.ReservationTime),
            payment_method = quote.PaymentMethodKey,
            payment_method_label = quote.PaymentMethodLabel,
            payment_percentage = quote.PaymentPercentage,
            facts = ctx.Facts
        };

        return (JsonSerializer.Serialize(snapshot), null);
    }
}
