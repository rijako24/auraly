using Auraly.Contracts.Authorization;

namespace Auraly.Application.Authorization;

public sealed record PosApprovalPushRecipient(
    Guid SubscriptionId,
    string Endpoint,
    string P256dh,
    string Auth);

public interface IPosApprovalPushSubscriptionStore
{
    Task UpsertAsync(
        PosApprovalUserIdentity user,
        string endpoint,
        string p256dh,
        string auth,
        CancellationToken cancellationToken);

    Task DeleteAsync(
        PosApprovalUserIdentity user,
        string endpoint,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PosApprovalPushRecipient>> RecipientsAsync(
        PosApprovalRequestView request,
        CancellationToken cancellationToken);

    Task DeleteAsync(Guid subscriptionId, CancellationToken cancellationToken);
}
