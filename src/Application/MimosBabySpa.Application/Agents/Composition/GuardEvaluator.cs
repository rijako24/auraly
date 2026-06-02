using System.Text.Json;
using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Gating;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Composition;

public sealed class GuardEvaluator : IGuardEvaluator
{
    private readonly IConversationVerificationService _verifications;

    public GuardEvaluator(IConversationVerificationService verifications)
    {
        _verifications = verifications;
    }

    public ToolAvailabilityResult EvaluateTool(
        IAgentTool tool,
        AgentConfig config,
        AgentToolContext ctx,
        JsonElement arguments)
    {
        var toolEval = tool.Evaluate(ctx, arguments);
        if (!toolEval.IsAvailable)
            return toolEval;

        if (!config.Guards.TryGetValue(tool.Name, out var guard) || guard.Requires.Count == 0)
            return ToolAvailabilityResultAvailable;

        foreach (var requirement in guard.Requires)
        {
            var result = EvaluateRequirement(requirement, tool, ctx, arguments);
            if (!result.IsAvailable)
                return result;
        }

        return ToolAvailabilityResultAvailable;
    }

    private ToolAvailabilityResult EvaluateRequirement(
        string requirement,
        IAgentTool tool,
        AgentToolContext ctx,
        JsonElement arguments)
    {
        if (requirement.StartsWith("fact:", StringComparison.OrdinalIgnoreCase))
        {
            var key = requirement["fact:".Length..];
            if (ctx.Facts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return ToolAvailabilityResultAvailable;

            return new ToolAvailabilityResult(
                false,
                $"Missing required fact '{key}'.",
                $"Collect '{key}' with set_fact before calling {tool.Name}.");
        }

        if (requirement.StartsWith("verification:", StringComparison.OrdinalIgnoreCase))
        {
            var factType = requirement["verification:".Length..];
            return EvaluateVerification(factType, tool, ctx, arguments);
        }

        if (requirement.Equals("state:payment_confirmed_no_slot", StringComparison.OrdinalIgnoreCase))
        {
            var payment = ctx.ActivePayment;
            if (payment?.RequiresRescheduling == true
                && payment.Status == PaymentTransactionStatus.Confirmed
                && !payment.ReservationId.HasValue)
            {
                return ToolAvailabilityResultAvailable;
            }

            return new ToolAvailabilityResult(
                false,
                "No confirmed payment pending slot assignment.",
                "Only use assign_paid_slot when pago_confirmado_sin_slot is active.");
        }

        if (requirement.Equals("flag:verbal_confirmation", StringComparison.OrdinalIgnoreCase))
        {
            if (ctx.BookingPolicy?.DepositRequired == false)
                return ToolAvailabilityResultAvailable;

            return new ToolAvailabilityResult(
                false,
                "Verbal confirmation flow is not active for this business.",
                "Do not call create_reservation when deposit is required.");
        }

        if (requirement.Equals("flag:deposit_required", StringComparison.OrdinalIgnoreCase))
        {
            if (ctx.BookingPolicy?.DepositRequired == true)
                return ToolAvailabilityResultAvailable;

            return new ToolAvailabilityResult(
                false,
                "Deposit is not required for this business.",
                "Use create_reservation for verbal confirmation flows.");
        }

        if (requirement.StartsWith("expr:", StringComparison.OrdinalIgnoreCase))
        {
            var expr = requirement["expr:".Length..].Trim();
            return EvaluateExpression(expr, tool, ctx, arguments);
        }

        return new ToolAvailabilityResult(
            false,
            $"Unknown guard requirement '{requirement}'.",
            "Check agent guard configuration.");
    }

    /// <summary>
    /// Evaluador de expresiones guard.
    /// Gramática soportada:
    ///   facts.KEY                         → fact KEY existe y no está vacío
    ///   NOT facts.KEY                     → fact KEY no existe o está vacío
    ///   policy.deposit_required           → bookingPolicy.DepositRequired == true
    ///   NOT policy.deposit_required       → bookingPolicy.DepositRequired == false
    ///   verification.TYPE                 → verificación TYPE está activa
    ///   NOT verification.TYPE             → verificación TYPE no está activa
    /// </summary>
    private ToolAvailabilityResult EvaluateExpression(
        string expr,
        IAgentTool tool,
        AgentToolContext ctx,
        JsonElement arguments)
    {
        var negate = false;
        var raw = expr;

        if (raw.StartsWith("NOT ", StringComparison.OrdinalIgnoreCase))
        {
            negate = true;
            raw = raw[4..].Trim();
        }

        bool result;
        string positiveBlockReason;
        string negativeBlockReason;

        if (raw.StartsWith("facts.", StringComparison.OrdinalIgnoreCase))
        {
            var key = raw["facts.".Length..].Trim();
            var hasFact = ctx.Facts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v);
            result = hasFact;
            positiveBlockReason = $"Fact '{key}' is missing.";
            negativeBlockReason = $"Fact '{key}' must be absent or empty.";
        }
        else if (raw.Equals("policy.deposit_required", StringComparison.OrdinalIgnoreCase))
        {
            result = ctx.BookingPolicy?.DepositRequired == true;
            positiveBlockReason = "Deposit is not required for this business.";
            negativeBlockReason = "Deposit is required for this business.";
        }
        else if (raw.StartsWith("verification.", StringComparison.OrdinalIgnoreCase))
        {
            var verType = raw["verification.".Length..].Trim();
            var factsForCheck = ResolveFactsForVerification(tool, ctx, arguments);
            if (factsForCheck is null)
            {
                result = false;
            }
            else
            {
                result = _verifications.IsActive(ctx.ConversationState, verType, factsForCheck);
            }

            positiveBlockReason = $"Verification '{verType}' is not active.";
            negativeBlockReason = $"Verification '{verType}' must not be active.";
        }
        else
        {
            return new ToolAvailabilityResult(
                false,
                $"Unsupported expr guard '{raw}'.",
                "Use: facts.KEY, policy.deposit_required, or verification.TYPE.");
        }

        var passed = negate ? !result : result;

        if (passed)
            return ToolAvailabilityResultAvailable;

        return new ToolAvailabilityResult(
            false,
            negate ? negativeBlockReason : positiveBlockReason,
            $"Check guard expression: {(negate ? "NOT " : "")}{raw}");
    }

    private ToolAvailabilityResult EvaluateVerification(
        string factType,
        IAgentTool tool,
        AgentToolContext ctx,
        JsonElement arguments)
    {
        var factsForCheck = ResolveFactsForVerification(tool, ctx, arguments);
        if (factsForCheck is null)
        {
            return new ToolAvailabilityResult(
                false,
                $"Cannot evaluate '{factType}' — required inputs are missing.",
                factType switch
                {
                    VerificationFactTypes.AvailabilityChecked =>
                        "Call check_availability first for the same service, date and time.",
                    VerificationFactTypes.CheckoutPrepared =>
                        "Call prepare_checkout first to show the booking summary.",
                    _ => "Complete required fields first."
                });
        }

        if (_verifications.IsActive(ctx.ConversationState, factType, factsForCheck))
            return ToolAvailabilityResultAvailable;

        return factType switch
        {
            VerificationFactTypes.AvailabilityChecked => new ToolAvailabilityResult(
                false,
                "Availability has not been verified for the current booking inputs.",
                "Call check_availability first for the same service, date and time."),
            VerificationFactTypes.CheckoutPrepared => new ToolAvailabilityResult(
                false,
                "Checkout summary has not been prepared for the current booking inputs.",
                "Call prepare_checkout to render the summary before create_reservation."),
            VerificationFactTypes.CustomerIdentified => new ToolAvailabilityResult(
                false,
                "Customer name and phone must be collected before creating a reservation.",
                "Use set_fact for customer_name and customer_phone."),
            _ => new ToolAvailabilityResult(
                false,
                $"Verification '{factType}' is not active.",
                $"Complete the step that records '{factType}' before calling {tool.Name}.")
        };
    }

    private static IReadOnlyDictionary<string, string>? ResolveFactsForVerification(
        IAgentTool tool,
        AgentToolContext ctx,
        JsonElement arguments) =>
        tool.VerificationDependencyResolver?.Invoke(arguments, ctx) ?? ctx.Facts;

    private static readonly ToolAvailabilityResult ToolAvailabilityResultAvailable =
        new(true, null, null);
}
