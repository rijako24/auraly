using MimosBabySpa.Application.Agents.Packs.Booking;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Agents.Orchestration;

/// <summary>
/// Resuelve referencias declarativas en args de lookup/execute.
/// Prefijos soportados:
///   @fact.X     → session.Facts["X"]
///   @const.X    → "X" (literal)
///   @result.X   → lastToolResult.GetString("X")
///   @pack.booking.has_active_reservation → booking pack context flag
/// </summary>
public static class FlowRefResolver
{
    public static IReadOnlyDictionary<string, string> ResolveArgs(
        IReadOnlyDictionary<string, string> argTemplate,
        AgentToolContext session,
        FlowToolResult? lastToolResult) =>
        ResolveArgsDetailed(argTemplate, session, lastToolResult).Resolved;

    public static (IReadOnlyDictionary<string, string> Resolved, IReadOnlyList<string> UnresolvedKeys)
        ResolveArgsDetailed(
            IReadOnlyDictionary<string, string> argTemplate,
            AgentToolContext session,
            FlowToolResult? lastToolResult)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<string>();

        foreach (var (key, refExpr) in argTemplate)
        {
            var value = ResolveRef(refExpr, session, lastToolResult);
            if (value is not null)
            {
                resolved[key] = value;
                continue;
            }

            if (refExpr.StartsWith("@fact.", StringComparison.OrdinalIgnoreCase))
                unresolved.Add(key);
        }

        return (resolved, unresolved);
    }

    public static string? ResolveRef(string refExpr, AgentToolContext session, FlowToolResult? lastToolResult)
    {
        if (string.IsNullOrWhiteSpace(refExpr)) return null;

        if (refExpr.StartsWith("@fact.", StringComparison.OrdinalIgnoreCase))
        {
            var key = refExpr["@fact.".Length..];
            return session.Facts.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        }

        if (refExpr.StartsWith("@const.", StringComparison.OrdinalIgnoreCase))
            return refExpr["@const.".Length..];

        if (refExpr.StartsWith("@result.", StringComparison.OrdinalIgnoreCase))
        {
            var key = refExpr["@result.".Length..];
            return lastToolResult?.GetString(key);
        }

        if (refExpr.Equals("@pack.booking.has_active_reservation", StringComparison.OrdinalIgnoreCase))
        {
            var hasReservation = session.GetPackContext<IBookingPackContext>()?.ActiveReservation is not null;
            return hasReservation ? "true" : "false";
        }

        if (refExpr.Equals("@pack.booking.has_pending_payment", StringComparison.OrdinalIgnoreCase))
        {
            var pay = session.GetPackContext<IBookingPackContext>()?.ActivePayment;
            var isPending = pay is not null
                && pay.Status != PaymentTransactionStatus.Confirmed
                && pay.Status != PaymentTransactionStatus.Failed
                && pay.Status != PaymentTransactionStatus.Expired
                && pay.Status != PaymentTransactionStatus.Superseded;
            return isPending ? "true" : "false";
        }

        // Valor literal (sin @prefix)
        return refExpr;
    }

    public static bool EvaluateCondition(
        Configuration.AgentFlowStageCondition condition,
        AgentToolContext session,
        FlowToolResult? lastToolResult)
    {
        var actual = ResolveRef(condition.Field, session, lastToolResult);
        return string.Equals(actual, condition.EqualsValue, StringComparison.OrdinalIgnoreCase);
    }
}
