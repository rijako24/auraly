using System.Text.Json;
using MimosBabySpa.Application.Agents.Templates;
using MimosBabySpa.Application.BusinessRules;
using MimosBabySpa.Application.Configuration;
using MimosBabySpa.Application.DTOs;
using MimosBabySpa.Application.Services;
using MimosBabySpa.Domain.Enums;
using static MimosBabySpa.Application.Agents.ToolSideEffectNames;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

public sealed class CreateReservationTool : IAgentTool
{
    private readonly IReservationService _reservations;
    private readonly IReservationIntentBuilder _intentBuilder;
    private readonly IBusinessRuleEngine _rules;
    private readonly IBookingPolicyProvider _bookingPolicy;
    private readonly IPaymentLifecycleService _paymentLifecycle;

    public CreateReservationTool(
        IReservationService reservations,
        IReservationIntentBuilder intentBuilder,
        IBusinessRuleEngine rules,
        IBookingPolicyProvider bookingPolicy,
        IPaymentLifecycleService paymentLifecycle)
    {
        _reservations = reservations;
        _intentBuilder = intentBuilder;
        _rules = rules;
        _bookingPolicy = bookingPolicy;
        _paymentLifecycle = paymentLifecycle;
    }

    public string Name => "create_reservation";

    public string Description =>
        "Creates a confirmed reservation for verbal-confirmation flows only (no advance payment). " +
        "Call after prepare_checkout when flow=verbal_confirmation and the customer explicitly confirms. " +
        "Do NOT call when deposit is required — the booking is confirmed automatically after Wompi validates payment. " +
        "The confirmation message is sent automatically — do not rewrite it.";

    public string ParametersSchema => """
        {
          "type": "object",
          "properties": {
            "service": { "type": "string" },
            "date": { "type": "string" },
            "time": { "type": "string" },
            "customer_name": { "type": "string" },
            "customer_phone": { "type": "string" },
            "customer_email": { "type": "string" },
            "add_ons": { "type": "string" },
            "customer_confirmed": { "type": "boolean" }
          },
          "required": ["customer_confirmed"]
        }
        """;

    public async Task<string> ExecuteAsync(
        JsonElement arguments,
        AgentToolContext ctx,
        CancellationToken cancellationToken = default)
    {
        var service = Coalesce(arguments, "service", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service));
        var dateStr = Coalesce(arguments, "date", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredDate));
        var timeStr = Coalesce(arguments, "time", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.DesiredTime));
        var customerName = Coalesce(arguments, "customer_name",
            ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerName) ?? ctx.Conversation.CustomerName);
        var customerPhone = Coalesce(arguments, "customer_phone",
            ConversationContactPhone.Resolve(ctx.Facts, ctx.ChannelPhone));
        var customerEmail = Coalesce(arguments, "customer_email",
            ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.CustomerEmail) ?? ctx.Conversation.CustomerEmail);
        var addOns = Coalesce(arguments, "add_ons", ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.AddOns));

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(service)) missing.Add("service");
        if (string.IsNullOrWhiteSpace(dateStr)) missing.Add("date");
        if (string.IsNullOrWhiteSpace(timeStr)) missing.Add("time");
        if (string.IsNullOrWhiteSpace(customerName)) missing.Add("customer_name");
        if (string.IsNullOrWhiteSpace(customerPhone)) missing.Add("customer_phone");
        if (missing.Count > 0)
            return ToolResultHelper.MissingPrerequisites([.. missing]);

        if (!AgentDateRules.TryParseDate(dateStr!, out var date))
            return ToolResultHelper.Error("invalid_date", $"'{dateStr}' is not a valid date.");
        if (AgentDateRules.IsPastDate(date, ctx.BusinessToday))
            return ToolResultHelper.Error("past_date", "Reservation date must be today or in the future.");
        if (!TimeOnly.TryParse(timeStr, out var time))
            return ToolResultHelper.Error("invalid_time", $"'{timeStr}' is not a valid time.");

        if (!ToolResultHelper.TryGetBool(arguments, "customer_confirmed", out var confirmed) || !confirmed)
        {
            return ToolResultHelper.Ok(new
            {
                status = "pending_confirmation",
                summary = new
                {
                    service,
                    date = dateStr,
                    time = timeStr,
                    customer_name = customerName,
                    customer_phone = customerPhone,
                    add_ons = string.IsNullOrWhiteSpace(addOns) ? null : addOns
                }
            });
        }

        var policy = await _bookingPolicy.GetAsync(ctx.BusinessId, cancellationToken);
        if (policy.DepositRequired)
        {
            var hasConfirmedPayment =
                ctx.ActivePayment?.Status == PaymentTransactionStatus.Confirmed
                || await _paymentLifecycle.HasConfirmedDepositAsync(ctx.ConversationId, cancellationToken);

            if (!hasConfirmedPayment)
            {
                return ToolResultHelper.Error(
                    "payment_required",
                    "This reservation requires a confirmed advance payment. Do not call create_reservation — " +
                    "the customer pays via prepare_checkout and confirmation is sent automatically when Wompi validates the payment.",
                    "If the customer says they already paid, reassure them the confirmation will arrive shortly. " +
                    "Use verify_payment only after 3+ insistences to check status — it does not create the reservation.");
            }
        }

        var ruleResult = await _rules.ValidateReservationAsync(
            ctx.BusinessId, service!, date, time, cancellationToken);
        if (!ruleResult.IsValid)
        {
            return ToolResultHelper.Error("business_rule_violation",
                ruleResult.Reason ?? "Business rules prevent this reservation.");
        }

        var intent = await _intentBuilder.BuildFromContextAsync(ctx, cancellationToken);
        if (intent is null)
        {
            return ToolResultHelper.Error(
                "invalid_booking_data",
                "Could not build reservation intent from collected facts.");
        }

        var attributes = string.IsNullOrWhiteSpace(addOns)
            ? new Dictionary<string, string>()
            : new Dictionary<string, string> { [ReservationBusinessAttributeKeys.SelectedAddOns] = addOns! };

        var response = await _reservations.CreateReservationAsync(
            new CreateReservationRequest(
                ctx.BusinessId, ctx.ConversationId,
                service!, date, time,
                customerName!, customerEmail,
                customerPhone!, attributes,
                intent.CustomAttributesJson),
            cancellationToken);

        ctx.ActiveReservation = new Domain.Entities.Reservation
        {
            ReservationId = response.ReservationId,
            BusinessId = ctx.BusinessId,
            ConversationId = ctx.ConversationId,
            Status = ReservationStatus.Confirmed,
            ReservationDateTime = date.ToDateTime(time),
            CustomerNameSnapshot = customerName,
            CustomerPhoneSnapshot = customerPhone,
            CustomAttributesJson = intent.CustomAttributesJson
        };

        return BuildSuccessResult(
            ctx,
            response.ReservationId,
            response.ServiceName ?? service!,
            dateStr!,
            timeStr!,
            customerName!,
            response.EmployeeName,
            response.DurationMinutes,
            response.AddOnNames);
    }

    private static string BuildSuccessResult(
        AgentToolContext ctx,
        Guid reservationId,
        string service,
        string dateStr,
        string timeStr,
        string customerName,
        string? employee = null,
        int? durationMinutes = null,
        IReadOnlyList<string>? addOnNames = null)
    {
        string? confirmationToken = null;
        if (ctx.Turn is not null
            && DateOnly.TryParse(dateStr, out var date)
            && TimeOnly.TryParse(timeStr, out var time))
        {
            var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_name"] = customerName,
                ["service_name"] = service,
                ["date_formatted"] = date.ToString("dd/MM/yyyy"),
                ["time"] = time.ToString("HH:mm")
            };

            foreach (var (key, value) in ctx.Facts)
            {
                if (!data.ContainsKey(key))
                    data[key] = value;
            }

            confirmationToken = ctx.Turn.RegisterFragment(
                "CONFIRMATION", "reservation_created", data, FragmentRenderMode.Exclusive);
        }

        return ToolResultHelper.Ok(new
        {
            reservation_id = reservationId,
            service,
            date = dateStr,
            time = timeStr,
            status = ReservationStatus.Confirmed.ToString(),
            is_booking_confirmed = true,
            employee,
            duration_minutes = durationMinutes,
            add_ons = addOnNames,
            confirmation_token = confirmationToken
        }, ReservationCreated);
    }

    private static string? Coalesce(JsonElement args, string property, string? fallback)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs))
            return fromArgs;
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}
