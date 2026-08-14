using Microsoft.Extensions.Logging;
using Auraly.Platform.Application.Agents.Facts;
using Auraly.Platform.Application.BusinessRules;
using Auraly.Platform.Application.Configuration;
using Auraly.Platform.Application.DTOs;
using Auraly.Platform.Application.Services;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Agents.Operations.Reservation;

public sealed record ReservationCreationRequest(
    Guid AgentId,
    Guid BusinessId,
    Guid ConversationId,
    DateOnly BusinessToday,
    AgentConfig Config,
    IReadOnlyDictionary<string, string> Facts,
    bool CustomerConfirmed,
    string? Service = null,
    string? Date = null,
    string? Time = null,
    string? CustomerName = null,
    string? CustomerPhone = null,
    string? CustomerEmail = null,
    string? AddOns = null,
    string? ConversationCustomerName = null,
    string? ConversationCustomerEmail = null,
    string? ChannelPhone = null,
    PaymentTransaction? ActivePayment = null,
    Domain.Entities.Reservation? ExistingReservation = null);

public sealed record ReservationCreationResult
{
    public bool Success { get; init; }
    public string Code { get; init; } = string.Empty;
    public string? Message { get; init; }
    public bool Recoverable { get; init; }
    public IReadOnlyList<string> MissingPrerequisites { get; init; } = [];
    public Guid? ReservationId { get; init; }
    public string? Service { get; init; }
    public string? Date { get; init; }
    public string? Time { get; init; }
    public string? CustomerName { get; init; }
    public string? CustomerPhone { get; init; }
    public string? Employee { get; init; }
    public int? DurationMinutes { get; init; }
    public IReadOnlyList<string> AddOnNames { get; init; } = [];
    public bool IsBookingConfirmed { get; init; }
    public bool IdempotentReplay { get; init; }
    public Domain.Entities.Reservation? Reservation { get; init; }
    public IReadOnlyDictionary<string, string> EffectiveFacts { get; init; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public static ReservationCreationResult Missing(params string[] values) => new()
    {
        Code = "input.missing_prerequisites",
        Message = "Required reservation data is missing.",
        Recoverable = true,
        MissingPrerequisites = values
    };

    public static ReservationCreationResult Fail(
        string code,
        string message,
        bool recoverable = false) => new()
    {
        Code = code,
        Message = message,
        Recoverable = recoverable
    };
}

public interface IReservationCreationService
{
    Task<ReservationCreationResult> CreateAsync(
        ReservationCreationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Deterministic reservation creation use case. It validates confirmation, payment state,
/// business rules and current availability before creating exactly one reservation.
/// </summary>
public sealed class ReservationCreationService : IReservationCreationService
{
    private readonly IReservationService _reservations;
    private readonly IReservationIntentBuilder _intentBuilder;
    private readonly IBusinessRuleEngine _rules;
    private readonly IAvailabilityService _availability;
    private readonly ISchedulingPolicyProvider _schedulingPolicy;
    private readonly ServiceNameResolver _serviceNames;
    private readonly IPaymentLifecycleService _payments;
    private readonly IReservationLifecycleService _reservationLifecycle;
    private readonly ILogger<ReservationCreationService> _logger;

    public ReservationCreationService(
        IReservationService reservations,
        IReservationIntentBuilder intentBuilder,
        IBusinessRuleEngine rules,
        IAvailabilityService availability,
        ISchedulingPolicyProvider schedulingPolicy,
        ServiceNameResolver serviceNames,
        IPaymentLifecycleService payments,
        IReservationLifecycleService reservationLifecycle,
        ILogger<ReservationCreationService> logger)
    {
        _reservations = reservations;
        _intentBuilder = intentBuilder;
        _rules = rules;
        _availability = availability;
        _schedulingPolicy = schedulingPolicy;
        _serviceNames = serviceNames;
        _payments = payments;
        _reservationLifecycle = reservationLifecycle;
        _logger = logger;
    }

    public async Task<ReservationCreationResult> CreateAsync(
        ReservationCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        var roles = new FactRoleIndex(request.Config.FactSchema);
        var service = Coalesce(request.Service, GetFact(roles, request.Facts, "booking.service", ConversationFactKeys.Service));
        var dateText = Coalesce(request.Date, GetFact(roles, request.Facts, "booking.date", ConversationFactKeys.DesiredDate));
        var timeText = Coalesce(request.Time, GetFact(roles, request.Facts, "booking.time", ConversationFactKeys.DesiredTime));
        var customerName = Coalesce(
            request.CustomerName,
            GetFact(roles, request.Facts, "customer.name", ConversationFactKeys.CustomerName),
            request.ConversationCustomerName);
        var customerPhone = Coalesce(
            request.CustomerPhone,
            GetFact(roles, request.Facts, "customer.phone", ConversationFactKeys.CustomerPhone),
            request.ChannelPhone);
        var customerEmail = Coalesce(
            request.CustomerEmail,
            GetFact(roles, request.Facts, "customer.email", ConversationFactKeys.CustomerEmail),
            request.ConversationCustomerEmail);
        var addOns = Coalesce(request.AddOns, GetFact(roles, request.Facts, "booking.addons", ConversationFactKeys.AddOns));

        var missing = new List<string>();
        if (service is null) missing.Add("service");
        if (dateText is null) missing.Add("date");
        if (timeText is null) missing.Add("time");
        if (customerName is null) missing.Add("customer_name");
        if (customerPhone is null) missing.Add("customer_phone");
        if (missing.Count > 0)
            return ReservationCreationResult.Missing([.. missing]);

        if (!AgentDateRules.TryParseDate(dateText, out var date))
            return ReservationCreationResult.Fail("input.invalid_date", $"'{dateText}' is not a valid date.", true);
        if (AgentDateRules.IsPastDate(date, request.BusinessToday))
            return ReservationCreationResult.Fail("input.past_date", "Reservation date must be today or in the future.", true);
        if (!TimeOnly.TryParse(timeText, out var time))
            return ReservationCreationResult.Fail("input.invalid_time", $"'{timeText}' is not a valid time.", true);

        if (!request.CustomerConfirmed)
        {
            return new ReservationCreationResult
            {
                Success = true,
                Code = "reservation.pending_confirmation",
                Service = service,
                Date = dateText,
                Time = timeText,
                CustomerName = customerName,
                CustomerPhone = customerPhone,
                IsBookingConfirmed = false
            };
        }

        var activePayment = request.ActivePayment
            ?? await _payments.GetActiveByConversationAsync(request.ConversationId, cancellationToken);
        if (activePayment?.Status == PaymentTransactionStatus.Created)
        {
            return ReservationCreationResult.Fail(
                "payment.required",
                "A payment link is pending for this reservation. Wait for payment confirmation before creating the reservation.",
                true);
        }
        if (activePayment?.Status == PaymentTransactionStatus.Confirmed
            && !activePayment.ReservationId.HasValue)
        {
            return ReservationCreationResult.Fail(
                "payment.fulfillment_pending",
                "Payment is confirmed but no reservation is linked yet. Payment fulfillment must create or link it.",
                true);
        }

        var canonicalService = await _serviceNames.ResolveAsync(
            request.BusinessId,
            service!,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(canonicalService))
            service = canonicalService;

        var facts = BuildEffectiveFacts(
            request.Facts,
            roles,
            service!,
            dateText!,
            timeText!,
            customerName!,
            customerPhone!,
            customerEmail,
            addOns);
        var existing = request.ExistingReservation
            ?? await _reservationLifecycle.GetActiveAsync(request.ConversationId, cancellationToken);
        var replay = BuildIdempotentResult(existing, service!, dateText!, timeText!, customerName!, customerPhone!, facts);
        if (replay is not null)
            return replay;

        var ruleResult = await _rules.ValidateReservationAsync(
            request.BusinessId,
            service!,
            date,
            time,
            cancellationToken);
        if (!ruleResult.IsValid)
        {
            return ReservationCreationResult.Fail(
                "reservation.business_rule_violation",
                ruleResult.Reason ?? "Business rules prevent this reservation.",
                true);
        }

        var policy = await _schedulingPolicy.GetAsync(request.BusinessId, cancellationToken);
        var availability = await _availability.CheckAvailabilityAsync(
            request.BusinessId,
            service!,
            date.ToDateTime(TimeOnly.MinValue),
            time.ToTimeSpan(),
            policy,
            cancellationToken);
        if (!availability.IsAvailable)
        {
            var message = availability.ResponseMessage ?? "The selected time is not available.";
            if (availability.AvailableOptions.Count > 0)
                message = $"{message} Available options: {string.Join(", ", availability.AvailableOptions.Select(option => $"{option.Start}-{option.End}"))}";
            return ReservationCreationResult.Fail("reservation.slot_unavailable", message, true);
        }

        var intent = await _intentBuilder.BuildAsync(
            new ReservationIntentContext(
                request.BusinessId,
                request.Config,
                facts,
                customerName,
                customerEmail,
                customerPhone),
            cancellationToken);
        if (intent is null)
        {
            return ReservationCreationResult.Fail(
                "reservation.invalid_booking_data",
                "Could not build reservation intent from collected facts.",
                true);
        }

        var attributes = string.IsNullOrWhiteSpace(addOns)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [ReservationBusinessAttributeKeys.SelectedAddOns] = addOns };
        _logger.LogInformation(
            "Creating reservation BusinessId={BusinessId} AgentId={AgentId} ConversationId={ConversationId} Service={Service} Date={Date} Time={Time}",
            request.BusinessId,
            request.AgentId,
            request.ConversationId,
            service,
            date,
            time);
        var response = await _reservations.CreateReservationAsync(
            new CreateReservationRequest(
                request.BusinessId,
                request.ConversationId,
                service!,
                date,
                time,
                customerName,
                customerEmail,
                customerPhone,
                attributes,
                intent.CustomAttributesJson),
            cancellationToken);

        var reservation = new Domain.Entities.Reservation
        {
            ReservationId = response.ReservationId,
            BusinessId = request.BusinessId,
            ConversationId = request.ConversationId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = date.ToDateTime(time),
            CustomerNameSnapshot = customerName,
            CustomerPhoneSnapshot = customerPhone,
            CustomAttributesJson = intent.CustomAttributesJson
        };
        return new ReservationCreationResult
        {
            Success = true,
            Code = "reservation.created",
            ReservationId = response.ReservationId,
            Service = response.ServiceName ?? service,
            Date = dateText,
            Time = timeText,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            Employee = response.EmployeeName,
            DurationMinutes = response.DurationMinutes,
            AddOnNames = response.AddOnNames,
            IsBookingConfirmed = true,
            Reservation = reservation,
            EffectiveFacts = facts
        };
    }

    private static ReservationCreationResult? BuildIdempotentResult(
        Domain.Entities.Reservation? reservation,
        string service,
        string dateText,
        string timeText,
        string customerName,
        string customerPhone,
        IReadOnlyDictionary<string, string> facts)
    {
        if (reservation?.Status != ReservationStatus.Confirmed
            || !reservation.ReservationDateTime.HasValue
            || !DateOnly.TryParse(dateText, out var requestedDate)
            || !TimeOnly.TryParse(timeText, out var requestedTime)
            || DateOnly.FromDateTime(reservation.ReservationDateTime.Value) != requestedDate
            || TimeOnly.FromDateTime(reservation.ReservationDateTime.Value) != requestedTime)
        {
            return null;
        }

        return new ReservationCreationResult
        {
            Success = true,
            Code = "reservation.idempotent_replay",
            ReservationId = reservation.ReservationId,
            Service = service,
            Date = dateText,
            Time = timeText,
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            IsBookingConfirmed = true,
            IdempotentReplay = true,
            Reservation = reservation,
            EffectiveFacts = facts
        };
    }

    private static Dictionary<string, string> BuildEffectiveFacts(
        IReadOnlyDictionary<string, string> current,
        FactRoleIndex roles,
        string service,
        string date,
        string time,
        string customerName,
        string customerPhone,
        string? customerEmail,
        string? addOns)
    {
        var facts = new Dictionary<string, string>(current, StringComparer.OrdinalIgnoreCase);
        facts[roles.KeyByRole("booking.service") ?? ConversationFactKeys.Service] = service;
        facts[roles.KeyByRole("booking.date") ?? ConversationFactKeys.DesiredDate] = date;
        facts[roles.KeyByRole("booking.time") ?? ConversationFactKeys.DesiredTime] = time;
        facts[roles.KeyByRole("customer.name") ?? ConversationFactKeys.CustomerName] = customerName;
        facts[roles.KeyByRole("customer.phone") ?? ConversationFactKeys.CustomerPhone] = customerPhone;
        if (!string.IsNullOrWhiteSpace(customerEmail))
            facts[roles.KeyByRole("customer.email") ?? ConversationFactKeys.CustomerEmail] = customerEmail;
        if (!string.IsNullOrWhiteSpace(addOns))
            facts[roles.KeyByRole("booking.addons") ?? ConversationFactKeys.AddOns] = addOns;
        return facts;
    }

    private static string? GetFact(
        FactRoleIndex roles,
        IReadOnlyDictionary<string, string> facts,
        string role,
        string fallbackKey) =>
        roles.GetByRole(facts, role) ?? ConversationFactKeys.Get(facts, fallbackKey);

    private static string? Coalesce(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
}
