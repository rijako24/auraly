using Auraly.Platform.Application.Agents.Configuration;
using Auraly.Platform.Application.Agents.Facts;
using Auraly.Platform.Application.Services;

namespace Auraly.Platform.Application.Agents.Operations.Support;

internal static class CheckoutPaymentFact
{
    private const string PaymentMethodRole = "payment.method";
    private const string FallbackPaymentMethodKey = "payment_method";

    public static string? ResolveDeclaredKey(FactRoleIndex roles) =>
        roles.KeyByRole(PaymentMethodRole)
        ?? (roles.EntryFor(FallbackPaymentMethodKey) is not null ? FallbackPaymentMethodKey : null);

    public static string ResolveDependencyKey(FactRoleIndex roles) =>
        ResolveDeclaredKey(roles) ?? FallbackPaymentMethodKey;

    public static string? Get(AgentConversationContext ctx, FactRoleIndex roles)
    {
        var key = ResolveDeclaredKey(roles);
        if (key is null)
            return null;

        return ctx.Facts.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    public static Task PersistSelectionAsync(
        IConversationFactsService factsService,
        AgentConversationContext ctx,
        FactRoleIndex roles,
        CheckoutQuote quote,
        CancellationToken ct) =>
        PersistCanonicalAsync(factsService, ctx, roles, quote.PaymentMethodKey, ct);

    public static async Task PersistSelectionAsync(
        IConversationFactsService factsService,
        AgentConversationContext ctx,
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

        dependencies[ResolveDependencyKey(roles)] = paymentMethod.Trim();
    }

    private static async Task PersistCanonicalAsync(
        IConversationFactsService factsService,
        AgentConversationContext ctx,
        FactRoleIndex roles,
        string? methodKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(methodKey))
            return;

        var key = ResolveDeclaredKey(roles);
        if (key is null)
            return;

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
