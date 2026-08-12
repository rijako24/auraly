namespace Auraly.Contracts.Parties;

public static class ExternalCustomerReconciliationPermissionCodes
{
    public const string Read = "parties.external-customers.read";
    public const string Reconcile = "parties.external-customers.reconcile";
}

public static class ExternalCustomerReconciliationStatuses
{
    public const string Pending = "Pending";
    public const string Linked = "Linked";
    public const string Conflict = "Conflict";
}

public sealed record ExternalCustomerReconciliationQuery(
    int PageSize = 25,
    string? Search = null,
    string? Status = null,
    Guid? IntegrationConnectionId = null);

public sealed record ExternalCustomerReconciliationItem(
    Guid ExternalCommerceCustomerId,
    Guid IntegrationConnectionId,
    string IntegrationName,
    string ExternalAccountId,
    string ExternalCustomerId,
    string? Name,
    string Phone,
    string PhoneNormalized,
    string Status,
    string? Error,
    Guid? PartyId,
    Guid? CustomerId,
    DateTimeOffset LastSyncedAt,
    DateTimeOffset? ReconciledAt);

public sealed record ExternalCustomerReconciliationPage(
    IReadOnlyCollection<ExternalCustomerReconciliationItem> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ExternalCustomerReconciliationResult(
    Guid ExternalCommerceCustomerId,
    string Status,
    Guid? PartyId,
    Guid? CustomerId,
    string? Error,
    bool IdempotentReplay);

public sealed record ReconcilePendingExternalCustomersRequest(int MaximumItems = 50);

public sealed record ReconcilePendingExternalCustomersResult(
    int Requested,
    int Linked,
    int Conflicts,
    int AlreadyLinked);
