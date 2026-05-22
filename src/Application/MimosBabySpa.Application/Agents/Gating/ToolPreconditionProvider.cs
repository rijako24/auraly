using System.Text.Json;
using MimosBabySpa.Application.Agents.Tools;
using MimosBabySpa.Application.Agents.Tools.Impl;

namespace MimosBabySpa.Application.Agents.Gating;

public sealed record ToolPrecondition(
    string FactType,
    Func<JsonElement, AgentToolContext, string?> ScopeKeyResolver,
    string MissingMessage,
    string Remediation);

public interface IToolPreconditionProvider
{
    IReadOnlyList<ToolPrecondition> GetFor(string toolName);
}

public sealed class ToolPreconditionProvider : IToolPreconditionProvider
{
    private readonly Dictionary<string, IReadOnlyList<ToolPrecondition>> _rules =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["prepare_checkout"] =
            [
                AvailabilityPrecondition()
            ],
            ["create_reservation"] =
            [
                AvailabilityPrecondition(),
                CustomerIdentifiedPrecondition()
            ],
            ["assign_paid_slot"] =
            [
                AssignPaidSlotAvailabilityPrecondition()
            ]
        };

    public IReadOnlyList<ToolPrecondition> GetFor(string toolName) =>
        _rules.TryGetValue(toolName, out var rules) ? rules : [];

    private static ToolPrecondition AvailabilityPrecondition() =>
        new(
            VerificationFactTypes.AvailabilityChecked,
            (_, ctx) => SlotVerificationScope.FromFacts(ctx.Facts),
            "Availability has not been verified for this service, date and time.",
            "Call check_availability first for the same service, date and time.");

    private static ToolPrecondition CustomerIdentifiedPrecondition() =>
        new(
            VerificationFactTypes.CustomerIdentified,
            (_, _) => SlotVerificationScope.UniversalScope,
            "Customer name and phone must be collected before creating a reservation.",
            "Use set_fact for customer_name and customer_phone.");

    private static ToolPrecondition AssignPaidSlotAvailabilityPrecondition() =>
        new(
            VerificationFactTypes.AvailabilityChecked,
            ResolveAssignPaidSlotScope,
            "The new time slot must be verified before assigning a paid reservation.",
            "Call check_availability for the new date and time before assign_paid_slot.");

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
}
