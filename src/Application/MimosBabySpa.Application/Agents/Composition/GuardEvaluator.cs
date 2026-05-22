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
    private readonly IToolPreconditionProvider _legacyPreconditions;

    public GuardEvaluator(
        IConversationVerificationService verifications,
        IToolPreconditionProvider legacyPreconditions)
    {
        _verifications = verifications;
        _legacyPreconditions = legacyPreconditions;
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

        if (config.Guards.TryGetValue(tool.Name, out var guard) && guard.Requires.Count > 0)
        {
            foreach (var requirement in guard.Requires)
            {
                var result = EvaluateRequirement(requirement, tool, ctx, arguments);
                if (!result.IsAvailable)
                    return result;
            }

            return ToolAvailabilityResultAvailable;
        }

        return EvaluateLegacyPreconditions(tool, ctx, arguments);
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
            if (ConversationFactKeys.Get(ctx.Facts, key) is not null
                || (ctx.Facts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v)))
            {
                return ToolAvailabilityResultAvailable;
            }

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

        return new ToolAvailabilityResult(
            false,
            $"Unknown guard requirement '{requirement}'.",
            "Check agent guard configuration.");
    }

    private ToolAvailabilityResult EvaluateVerification(
        string factType,
        IAgentTool tool,
        AgentToolContext ctx,
        JsonElement arguments)
    {
        string? scopeKey = factType switch
        {
            VerificationFactTypes.AvailabilityChecked when string.Equals(
                tool.Name, "assign_paid_slot", StringComparison.OrdinalIgnoreCase) =>
                ResolveAssignPaidSlotScope(arguments, ctx),
            VerificationFactTypes.AvailabilityChecked => SlotVerificationScope.FromFacts(ctx.Facts),
            VerificationFactTypes.CustomerIdentified => SlotVerificationScope.UniversalScope,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(scopeKey))
        {
            return new ToolAvailabilityResult(
                false,
                $"Cannot evaluate '{factType}' — required booking fields are missing.",
                factType switch
                {
                    VerificationFactTypes.AvailabilityChecked =>
                        "Call check_availability first for the same service, date and time.",
                    VerificationFactTypes.CustomerIdentified =>
                        "Use set_fact for customer_name and customer_phone.",
                    _ => "Complete required fields first."
                });
        }

        if (_verifications.IsActive(ctx.ConversationState, factType, scopeKey))
            return ToolAvailabilityResultAvailable;

        return new ToolAvailabilityResult(
            false,
            factType switch
            {
                VerificationFactTypes.AvailabilityChecked =>
                    "Availability has not been verified for this service, date and time.",
                VerificationFactTypes.CustomerIdentified =>
                    "Customer name and phone must be collected before creating a reservation.",
                _ => $"'{factType}' verification is not active."
            },
            factType switch
            {
                VerificationFactTypes.AvailabilityChecked =>
                    "Call check_availability first for the same service, date and time.",
                VerificationFactTypes.CustomerIdentified =>
                    "Use set_fact for customer_name and customer_phone.",
                _ => "Complete the required step first."
            });
    }

    private ToolAvailabilityResult EvaluateLegacyPreconditions(
        IAgentTool tool,
        AgentToolContext ctx,
        JsonElement arguments)
    {
        foreach (var precondition in _legacyPreconditions.GetFor(tool.Name))
        {
            var scopeKey = precondition.ScopeKeyResolver(arguments, ctx);
            if (string.IsNullOrWhiteSpace(scopeKey))
            {
                return new ToolAvailabilityResult(
                    false,
                    $"Cannot evaluate '{precondition.FactType}' — required booking fields are missing.",
                    precondition.Remediation);
            }

            if (!_verifications.IsActive(ctx.ConversationState, precondition.FactType, scopeKey))
            {
                return new ToolAvailabilityResult(
                    false,
                    precondition.MissingMessage,
                    precondition.Remediation);
            }
        }

        return ToolAvailabilityResultAvailable;
    }

    private static string? ResolveAssignPaidSlotScope(JsonElement args, AgentToolContext ctx)
    {
        var date = ResolveArgOrFact(args, "date", ConversationFactKeys.DesiredDate, ctx.Facts);
        var time = ResolveArgOrFact(args, "time", ConversationFactKeys.DesiredTime, ctx.Facts);
        var serviceName = ConversationFactKeys.Get(ctx.Facts, ConversationFactKeys.Service);

        if (string.IsNullOrWhiteSpace(serviceName)
            || string.IsNullOrWhiteSpace(date)
            || string.IsNullOrWhiteSpace(time))
        {
            return null;
        }

        return SlotVerificationScope.Build(serviceName, date, time);
    }

    private static string? ResolveArgOrFact(
        JsonElement args,
        string property,
        string factKey,
        IReadOnlyDictionary<string, string> facts)
    {
        if (ToolResultHelper.TryGetString(args, property, out var fromArgs)
            && !string.IsNullOrWhiteSpace(fromArgs))
        {
            return fromArgs;
        }

        return ConversationFactKeys.Get(facts, factKey);
    }

    private static readonly ToolAvailabilityResult ToolAvailabilityResultAvailable =
        new(true, null, null);
}
