using System.Globalization;
using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Models;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Agents.Operations.Reservation;

public sealed record ReservationCheckoutPreparationRequest(
    Guid BusinessId,
    Guid ConversationId,
    AgentConfig Config,
    ConversationState ConversationState,
    IReadOnlyDictionary<string, string> Facts,
    string? Service,
    bool AddOnsProvided,
    string? AddOns,
    string? PaymentMethod,
    PaymentTransaction? ActivePayment = null);

public sealed record ReservationCheckoutPreparationResult
{
    public bool Success { get; init; }
    public string Code { get; init; } = string.Empty;
    public string? Message { get; init; }
    public bool Recoverable { get; init; }
    public IReadOnlyList<string> MissingPrerequisites { get; init; } = [];
    public IReadOnlyList<string> AvailablePaymentMethods { get; init; } = [];
    public CheckoutQuote? Quote { get; init; }
    public IReadOnlyDictionary<string, object?> TemplateData { get; init; }
        = new Dictionary<string, object?>();
    public IReadOnlyDictionary<string, string> VerificationDependencies { get; init; }
        = new Dictionary<string, string>();
    public PaymentTransaction? Payment { get; init; }
    public string? PaymentLink { get; init; }
    public string? PaymentMethodFactKey { get; init; }
    public string? PaymentMethodFactValue { get; init; }

    public bool ActivePaymentDiscarded { get; init; }
    public static ReservationCheckoutPreparationResult Missing(params string[] facts) => new()
    {
        Code = "input.missing_prerequisites",
        Message = "Required checkout data is missing.",
        Recoverable = true,
        MissingPrerequisites = facts
    };

    public static ReservationCheckoutPreparationResult Fail(
        string code,
        string message,
        bool recoverable = false,
        IReadOnlyList<string>? availablePaymentMethods = null) => new()
    {
        Code = code,
        Message = message,
        Recoverable = recoverable,
        AvailablePaymentMethods = availablePaymentMethods ?? []
    };
}

public interface IReservationCheckoutPreparationService
{
    Task<ReservationCheckoutPreparationResult> PrepareAsync(
        ReservationCheckoutPreparationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Authoritative reservation/enrollment checkout use case. It resolves catalog and pricing,
/// validates configured prerequisites and availability, prepares payment and returns presentation
/// data. It does not mutate conversation facts, verifications or response fragments.
/// </summary>
public sealed class ReservationCheckoutPreparationService : IReservationCheckoutPreparationService
{
    private readonly ReservationPricingResolver _pricing;
    private readonly IAddOnCatalogService _addOnCatalog;
    private readonly ICheckoutPaymentCoordinator _checkoutPayments;
    private readonly IConversationVerificationService _verifications;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ServiceNameResolver _serviceNameResolver;

    public ReservationCheckoutPreparationService(
        ReservationPricingResolver pricing,
        IAddOnCatalogService addOnCatalog,
        ICheckoutPaymentCoordinator checkoutPayments,
        IConversationVerificationService verifications,
        IUnitOfWork unitOfWork,
        ServiceNameResolver serviceNameResolver)
    {
        _pricing = pricing;
        _addOnCatalog = addOnCatalog;
        _checkoutPayments = checkoutPayments;
        _verifications = verifications;
        _unitOfWork = unitOfWork;
        _serviceNameResolver = serviceNameResolver;
    }

    public async Task<ReservationCheckoutPreparationResult> PrepareAsync(
        ReservationCheckoutPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        var roles = new FactRoleIndex(request.Config.FactSchema);
        var quoteResult = await BuildQuoteAsync(request, roles, cancellationToken);
        if (!quoteResult.Success)
            return quoteResult;

        var quote = quoteResult.Quote!;
        var missing = FindMissingRequiredFacts(quote, roles, request.Facts);
        if (missing.Count > 0)
            return ReservationCheckoutPreparationResult.Missing([.. missing]);

        var availabilityFailure = EnsureAvailabilityVerified(request, quote, roles);
        if (availabilityFailure is not null)
            return availabilityFailure;

        var templateData = BuildTemplateData(quote, roles, request.Facts);
        var paymentContext = new CheckoutPaymentContext(
            request.BusinessId,
            request.ConversationId,
            request.ActivePayment);
        PaymentTransaction? payment = null;
        string? paymentLink = null;
        var activePaymentDiscarded = false;

        if (quote.RequiresManualConfirmation)
        {
            var snapshot = BuildCheckoutSnapshot(request, quote, roles);
            if (!snapshot.Success)
                return snapshot.Failure!;

            var result = await _checkoutPayments.EnsureManualPaymentAsync(
                paymentContext,
                quote,
                snapshot.Json!,
                cancellationToken);
            if (!result.Success)
            {
                return ReservationCheckoutPreparationResult.Fail(
                    "payment.manual_preparation_failed",
                    result.ErrorMessage ?? "Failed to prepare manual payment confirmation.");
            }

            payment = result.Payment;
            templateData["payment_pending_manual_confirmation"] = true;
        }
        else if (quote.PayableCents > 0)
        {
            var paymentPhone = ResolveSystemFact(quote, roles, request.Facts, CheckoutSystemSlots.PaymentPhone);
            if (string.IsNullOrWhiteSpace(paymentPhone))
            {
                return ReservationCheckoutPreparationResult.Fail(
                    "payment.phone_missing",
                    "Checkout requires payment but no payment phone binding was resolved.",
                    recoverable: true);
            }

            var snapshot = BuildCheckoutSnapshot(request, quote, roles);
            if (!snapshot.Success)
                return snapshot.Failure!;

            var result = await _checkoutPayments.EnsurePaymentLinkAsync(
                paymentContext,
                quote,
                paymentPhone,
                snapshot.Json!,
                cancellationToken);
            if (!result.Success)
            {
                return ReservationCheckoutPreparationResult.Fail(
                    "payment.link_generation_failed",
                    result.ErrorMessage ?? "Failed to generate payment link.");
            }

            payment = result.Payment;
            paymentLink = result.LinkUrl;
            templateData["link_url"] = paymentLink;
        }
        else
        {
            var discard = await _checkoutPayments.DiscardActiveCheckoutAsync(
                paymentContext,
                quote.CheckoutKind,
                cancellationToken);
            activePaymentDiscarded = discard.DiscardedPayment is not null;
        }

        return new ReservationCheckoutPreparationResult
        {
            Success = true,
            Code = "checkout.prepared",
            Quote = quote,
            TemplateData = templateData,
            VerificationDependencies = BuildVerificationDependencies(quote, roles, request.Facts),
            Payment = payment,
            PaymentLink = paymentLink,
            PaymentMethodFactKey = ResolvePaymentMethodFactKey(roles),
            PaymentMethodFactValue = quote.PaymentMethodKey,
            ActivePaymentDiscarded = activePaymentDiscarded
        };
    }

    private async Task<ReservationCheckoutPreparationResult> BuildQuoteAsync(
        ReservationCheckoutPreparationRequest request,
        FactRoleIndex roles,
        CancellationToken cancellationToken)
    {
        var serviceName = Normalize(request.Service)
            ?? roles.GetByRole(request.Facts, "booking.service")
            ?? ConversationFactKeys.Get(request.Facts, ConversationFactKeys.Service);
        if (string.IsNullOrWhiteSpace(serviceName))
            return ReservationCheckoutPreparationResult.Missing("service");

        var addOns = request.AddOnsProvided
            ? Normalize(request.AddOns)
            : roles.GetByRole(request.Facts, "booking.addons")
                ?? ConversationFactKeys.Get(request.Facts, ConversationFactKeys.AddOns);
        addOns = Normalize(addOns);

        var service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(request.BusinessId, serviceName);
        if (service is null)
        {
            var canonical = await _serviceNameResolver.ResolveAsync(
                request.BusinessId,
                serviceName,
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(canonical))
                service = await _unitOfWork.Services.GetByBusinessIdAndNameAsync(request.BusinessId, canonical);
        }

        if (service is null)
        {
            return ReservationCheckoutPreparationResult.Fail(
                "catalog.service_not_found",
                $"Service '{serviceName}' was not found in the catalog.",
                recoverable: true);
        }

        if (!string.IsNullOrWhiteSpace(addOns))
        {
            var validation = await _addOnCatalog.ValidateAsync(
                request.BusinessId,
                service.ServiceName,
                addOns,
                cancellationToken);
            if (!validation.IsValid)
            {
                return ReservationCheckoutPreparationResult.Fail(
                    "catalog.invalid_add_ons",
                    validation.ErrorMessage ?? "Invalid add-on selection.",
                    recoverable: true);
            }
            addOns = validation.NormalizedCsv;
        }

        var checkoutKind = ResolveCheckoutKind(service);
        var checkoutKindText = checkoutKind.ToString().ToLowerInvariant();
        var mode = request.Config.Checkout.ResolveMode(checkoutKindText);
        if (mode is null)
        {
            return ReservationCheckoutPreparationResult.Fail(
                "configuration.checkout_mode_missing",
                $"Checkout mode '{checkoutKindText}' is not configured for this agent.");
        }

        var pricing = await _pricing.ResolveAsync(
            request.BusinessId,
            BuildPricingItems(service.ServiceName, addOns),
            cancellationToken);
        if (pricing is null)
            return ReservationCheckoutPreparationResult.Fail("pricing.unresolved", "Could not resolve pricing from the catalog.");

        var totalCents = (long)(pricing.Total * 100);
        var paymentMethod = Normalize(request.PaymentMethod) ?? ResolvePaymentMethod(request.Facts, roles);
        var selection = CheckoutPaymentSelectionResolver.Resolve(mode, checkoutKindText, totalCents, paymentMethod);
        if (selection.MissingPaymentMethod)
            return ReservationCheckoutPreparationResult.Missing("payment_method");
        if (selection.Error is not null)
        {
            return ReservationCheckoutPreparationResult.Fail(
                selection.Error.Code,
                selection.Error.Message,
                selection.Error.Recoverable,
                selection.Error.AvailablePaymentMethods);
        }

        var bindings = CheckoutModeBindingDefaults.Resolve(checkoutKind, mode);
        var quote = new CheckoutQuote(
            request.BusinessId,
            request.ConversationId,
            checkoutKind,
            service.ServiceId,
            service.ServiceName,
            service.ServiceCategory?.Name,
            service.DurationMinutes > 0 ? service.DurationMinutes : 60,
            pricing.LineItems.Select(item => new CheckoutQuoteLineItem(
                item.Name,
                item.Price,
                item.IncludeInCheckoutTotal)).ToList(),
            totalCents,
            selection.PayableCents,
            string.IsNullOrWhiteSpace(request.Config.Checkout.Currency)
                ? "COP"
                : request.Config.Checkout.Currency.Trim().ToUpperInvariant(),
            selection.MethodKey,
            selection.MethodLabel,
            selection.PaymentPercentage,
            selection.TemplateId,
            selection.ConfirmationOutcome,
            new Dictionary<string, string>(bindings.RequiredFactRoles, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(bindings.SystemFactBindings, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(bindings.TemplateFactBindings, StringComparer.OrdinalIgnoreCase),
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(30))
        {
            RequiresManualConfirmation = selection.RequiresManualConfirmation,
            ManualExpirationMinutes = selection.ManualExpirationMinutes
        };

        return new ReservationCheckoutPreparationResult
        {
            Success = true,
            Code = "checkout.quote_resolved",
            Quote = quote
        };
    }

    private ReservationCheckoutPreparationResult? EnsureAvailabilityVerified(
        ReservationCheckoutPreparationRequest request,
        CheckoutQuote quote,
        FactRoleIndex roles)
    {
        if (quote.CheckoutKind != CheckoutKind.Reservation)
            return null;

        var serviceKey = roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service;
        var dateKey = roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate;
        var timeKey = roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime;
        var date = ResolveSystemFact(quote, roles, request.Facts, CheckoutSystemSlots.ReservationDate);
        var time = ResolveSystemFact(quote, roles, request.Facts, CheckoutSystemSlots.ReservationTime);
        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
        {
            return ReservationCheckoutPreparationResult.Fail(
                "availability.verification_missing",
                "Reservation checkout requires service, date and time before preparing payment.",
                recoverable: true);
        }

        var dependencies = VerificationSnapshot.FromValues(
            new KeyValuePair<string, string>(serviceKey, quote.ServiceName),
            new KeyValuePair<string, string>(dateKey, date),
            new KeyValuePair<string, string>(timeKey, time));
        return _verifications.IsActive(
            request.ConversationState,
            VerificationFactTypes.AvailabilityChecked,
            dependencies)
            ? null
            : ReservationCheckoutPreparationResult.Fail(
                "availability.verification_stale",
                "Availability has not been verified for the exact service, date and time used for checkout.",
                recoverable: true);
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
            ["total"] = Money(quote.TotalCents),
            ["total_cents"] = quote.TotalCents,
            ["payable"] = Money(quote.PayableCents),
            ["payable_cents"] = quote.PayableCents,
            ["currency"] = quote.Currency,
            ["payment_method"] = quote.PaymentMethodKey,
            ["payment_method_label"] = quote.PaymentMethodLabel,
            ["payment_percentage"] = quote.PaymentPercentage,
            ["line_items"] = quote.LineItems.Select(ToTemplateLineItem).Cast<object>().ToList(),
            ["service_price"] = (quote.LineItems.FirstOrDefault()?.Price ?? quote.TotalCents / 100m)
                .ToString("N0", CultureInfo.InvariantCulture),
            ["addons"] = quote.LineItems.Skip(1).Select(ToTemplateLineItem).Cast<object>().ToList(),
            ["deposit"] = Money(quote.PayableCents),
            ["deposit_pct"] = quote.TotalCents > 0
                ? (int)Math.Round(quote.PayableCents * 100m / quote.TotalCents)
                : 0
        };
        foreach (var (key, value) in facts)
            data.TryAdd(key, value);
        foreach (var (field, binding) in quote.TemplateFactBindings)
            data[field] = ResolveBinding(roles, facts, binding);
        if (data.TryGetValue("date_formatted", out var rawDate)
            && rawDate is string dateText
            && AgentDateRules.TryParseDate(dateText, out var date))
        {
            data["date_formatted"] = date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        }
        return data;
    }

    private static Dictionary<string, object?> ToTemplateLineItem(CheckoutQuoteLineItem item) => new()
    {
        ["name"] = item.Name,
        ["price"] = item.Price.ToString("N0", CultureInfo.InvariantCulture),
        ["include_in_checkout_total"] = item.IncludeInCheckoutTotal,
        ["checkout_note"] = item.IncludeInCheckoutTotal
            ? string.Empty
            : " (valor informativo; no incluido en el anticipo)"
    };

    private static (bool Success, string? Json, ReservationCheckoutPreparationResult? Failure) BuildCheckoutSnapshot(
        ReservationCheckoutPreparationRequest request,
        CheckoutQuote quote,
        FactRoleIndex roles)
    {
        var system = quote.SystemFactBindings.Keys.ToDictionary(
            slot => slot,
            slot => ResolveSystemFact(quote, roles, request.Facts, slot),
            StringComparer.OrdinalIgnoreCase);
        if (quote.CheckoutKind == CheckoutKind.Reservation)
        {
            if (!AgentDateRules.TryParseDate(system.GetValueOrDefault(CheckoutSystemSlots.ReservationDate), out _))
            {
                return (false, null, ReservationCheckoutPreparationResult.Fail(
                    "input.invalid_reservation_date",
                    "Reservation date binding is missing or invalid.",
                    recoverable: true));
            }
            if (!TimeOnly.TryParse(system.GetValueOrDefault(CheckoutSystemSlots.ReservationTime), out _))
            {
                return (false, null, ReservationCheckoutPreparationResult.Fail(
                    "input.invalid_reservation_time",
                    "Reservation time binding is missing or invalid.",
                    recoverable: true));
            }
        }

        return (true, JsonSerializer.Serialize(new
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
            custom_attributes_json = ReservationCustomAttributes.BuildJson(request.Facts, request.Config.FactSchema),
            facts = request.Facts
        }), null);
    }

    private static List<string> FindMissingRequiredFacts(
        CheckoutQuote quote,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts) =>
        quote.RequiredFactRoles
            .Where(binding => string.IsNullOrWhiteSpace(ResolveBinding(roles, facts, binding.Value)))
            .Select(binding => binding.Key)
            .ToList();

    private static IReadOnlyDictionary<string, string> BuildVerificationDependencies(
        CheckoutQuote quote,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts)
    {
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in quote.RequiredFactRoles.Values)
        {
            var key = roles.KeyByRole(binding) ?? binding;
            if (!string.IsNullOrWhiteSpace(key))
                dependencies[key] = facts.TryGetValue(key, out var value) ? value : string.Empty;
        }
        if (!string.IsNullOrWhiteSpace(quote.PaymentMethodKey))
            dependencies[ResolvePaymentMethodFactKey(roles) ?? "payment_method"] = quote.PaymentMethodKey;
        return dependencies;
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

    private static string? ResolveSystemFact(
        CheckoutQuote quote,
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        string slot) =>
        quote.SystemFactBindings.TryGetValue(slot, out var binding)
            ? ResolveBinding(roles, facts, binding)
            : null;

    private static string? ResolveBinding(
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        string binding)
    {
        var key = roles.KeyByRole(binding) ?? binding;
        return facts.TryGetValue(key, out var value) ? Normalize(value) : null;
    }

    private static string? ResolvePaymentMethod(
        IReadOnlyDictionary<string, string> facts,
        FactRoleIndex roles)
    {
        var key = ResolvePaymentMethodFactKey(roles);
        return key is not null && facts.TryGetValue(key, out var value) ? Normalize(value) : null;
    }

    private static string? ResolvePaymentMethodFactKey(FactRoleIndex roles) =>
        roles.KeyByRole("payment.method")
        ?? (roles.EntryFor("payment_method") is not null ? "payment_method" : null);

    private static string Money(long cents) =>
        (cents / 100m).ToString("N0", CultureInfo.InvariantCulture);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
