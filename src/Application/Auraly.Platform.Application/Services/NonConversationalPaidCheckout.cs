using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Services;

public interface INonConversationalPaidCheckoutHandler
{
    CheckoutKind Kind { get; }
    Task<bool> IsFulfilledAsync(PaymentTransaction payment, CancellationToken cancellationToken);
    Task FulfillAsync(PaymentTransaction payment, CancellationToken cancellationToken);
}

public interface INonConversationalPaidCheckoutRegistry
{
    INonConversationalPaidCheckoutHandler Resolve(CheckoutKind kind);
}

public sealed class NonConversationalPaidCheckoutRegistry(
    IEnumerable<INonConversationalPaidCheckoutHandler> handlers)
    : INonConversationalPaidCheckoutRegistry
{
    private readonly IReadOnlyDictionary<CheckoutKind, INonConversationalPaidCheckoutHandler> _handlers =
        handlers.ToDictionary(handler => handler.Kind);

    public INonConversationalPaidCheckoutHandler Resolve(CheckoutKind kind) =>
        _handlers.TryGetValue(kind, out var handler)
            ? handler
            : throw new InvalidOperationException(
                $"CheckoutKind '{kind}' has no non-conversational fulfillment handler.");
}
