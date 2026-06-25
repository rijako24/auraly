using MimosBabySpa.Application.Agents.Configuration;
using MimosBabySpa.Application.Agents.Facts;
using MimosBabySpa.Application.Services;

namespace MimosBabySpa.Application.Agents.Tools.Impl;

internal static class CheckoutPaymentFact
{
    private const string PaymentMethodRole = "payment.method";
    private const string FallbackPaymentMethodKey = "payment_method";

    public static string ResolveKey(FactRoleIndex roles) =>
        roles.KeyByRole(PaymentMethodRole) ?? FallbackPaymentMethodKey;

    public static string? Get(AgentToolContext ctx, FactRoleIndex roles)
    {
        var key = ResolveKey(roles);
        if (ctx.Facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            return value.Trim();

        return !key.Equals(FallbackPaymentMethodKey, StringComparison.OrdinalIgnoreCase)
            && ctx.Facts.TryGetValue(FallbackPaymentMethodKey, out var fallback)
            && !string.IsNullOrWhiteSpace(fallback)
                ? fallback.Trim()
                : null;
    }

    public static Task PersistSelectionAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        FactRoleIndex roles,
        CheckoutQuote quote,
        CancellationToken ct) =>
        PersistCanonicalAsync(factsService, ctx, roles, quote.PaymentMethodKey, ct);

    public static async Task PersistSelectionAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        FactRoleIndex roles,
        CheckoutPaymentSelection selection,
        CancellationToken ct)
    {
        if (selection.MissingPaymentMethod || selection.Error is not null)
            return;

        await PersistCanonicalAsync(factsService, ctx, roles, selection.MethodKey, ct);
    }

    public static void AddDependency(
        IDictionary<string, string> dependencies,
        FactRoleIndex roles,
        string? paymentMethod)
    {
        if (string.IsNullOrWhiteSpace(paymentMethod))
            return;

        dependencies[ResolveKey(roles)] = paymentMethod.Trim();
    }

    private static async Task PersistCanonicalAsync(
        IConversationFactsService factsService,
        AgentToolContext ctx,
        FactRoleIndex roles,
        string? methodKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(methodKey))
            return;

        var key = ResolveKey(roles);
        var canonicalValue = methodKey.Trim();
        if (ctx.Facts.TryGetValue(key, out var current)
            && current.Trim().Equals(canonicalValue, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var schemaEntry = roles.EntryFor(key);
        await factsService.SetAsync(
            ctx.ConversationId,
            ctx.BusinessId,
            key,
            canonicalValue,
            schemaEntry?.ShouldRememberAcrossRequests() ?? false,
            ct);
        ctx.Facts[key] = canonicalValue;
    }
}
