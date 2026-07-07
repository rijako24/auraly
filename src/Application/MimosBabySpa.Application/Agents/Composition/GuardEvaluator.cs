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

        var guard = ResolveGuard(tool, config);
        if (guard is null || guard.Requires.Count == 0)
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
                null);
        }

        if (requirement.StartsWith("verification:", StringComparison.OrdinalIgnoreCase))
        {
            var factType = requirement["verification:".Length..];
            return EvaluateVerification(factType, tool, ctx, arguments);
        }

        if (requirement.Equals("state:no_pending_checkout", StringComparison.OrdinalIgnoreCase))
        {
            var payment = ctx.ActivePayment;
            if (payment?.Status != PaymentTransactionStatus.Created)
                return ToolAvailabilityResultAvailable;

            return new ToolAvailabilityResult(
                false,
                "There is a pending checkout link for this conversation.",
                null);
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
                null);
        }

        if (requirement.Equals("flag:verbal_confirmation", StringComparison.OrdinalIgnoreCase))
            return ToolAvailabilityResultAvailable;

        if (requirement.StartsWith("expr:", StringComparison.OrdinalIgnoreCase))
        {
            var expr = requirement["expr:".Length..].Trim();
            return EvaluateExpression(expr, tool, ctx, arguments);
        }

        return new ToolAvailabilityResult(
                false,
                $"Unknown guard requirement '{requirement}'.",
                null);
    }

    private static GuardDefinition? ResolveGuard(IAgentTool tool, AgentConfig config)
    {
        foreach (var capability in tool.Capabilities)
        {
            if (config.Guards.TryGetValue($"capability:{capability}", out var capabilityGuard))
                return capabilityGuard;
        }

        return null;
    }

    /// <summary>
    /// Evaluador de expresiones guard.
    /// Gramática soportada:
    ///   facts.KEY                         → fact KEY existe y no está vacío
    ///   NOT facts.KEY                     → fact KEY no existe o está vacío
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
                null);
        }

        var passed = negate ? !result : result;

        if (passed)
            return ToolAvailabilityResultAvailable;

        return new ToolAvailabilityResult(
            false,
            negate ? negativeBlockReason : positiveBlockReason,
            null);
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
                null);
        }

        if (_verifications.IsActive(ctx.ConversationState, factType, factsForCheck))
            return ToolAvailabilityResultAvailable;

        return factType switch
        {
            VerificationFactTypes.AvailabilityChecked => new ToolAvailabilityResult(
                false,
                "Availability has not been verified for the current booking inputs.",
                null),
            VerificationFactTypes.CheckoutPrepared => new ToolAvailabilityResult(
                false,
                "Checkout summary has not been prepared for the current booking inputs.",
                null),
            VerificationFactTypes.CheckoutNoPaymentPrepared => new ToolAvailabilityResult(
                false,
                "A no-payment reservation checkout has not been prepared for the current booking inputs.",
                null),
            VerificationFactTypes.CustomerIdentified => new ToolAvailabilityResult(
                false,
                "Customer name and phone must be collected before creating a reservation.",
                null),
            _ => new ToolAvailabilityResult(
                false,
                $"Verification '{factType}' is not active.",
                null)
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
