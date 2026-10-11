using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public interface IRestaurantCollectionStore
{
    Task<RestaurantCollectionSummary> GetAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct);

    Task<RestaurantCollectionSummary> CaptureAsync(
        RestaurantAccountActor actor, Guid accountId,
        CaptureRestaurantCollectionRequest request, string idempotencyKey,
        DateTimeOffset now, CancellationToken ct);

    Task<RestaurantCollectionSummary> ReverseAsync(
        RestaurantAccountActor actor, Guid accountId, Guid collectionId,
        long expectedVersion, DateTimeOffset now, CancellationToken ct);
}

public sealed class RestaurantCollectionService(
    IRestaurantCollectionStore store, TimeProvider time)
{
    public Task<RestaurantCollectionSummary> GetAsync(
        RestaurantAccountActor actor, Guid accountId, CancellationToken ct)
    {
        DemandCashier(actor, accountId);
        return store.GetAsync(actor, accountId, ct);
    }

    public Task<RestaurantCollectionSummary> CaptureAsync(
        RestaurantAccountActor actor, Guid accountId,
        CaptureRestaurantCollectionRequest request, string idempotencyKey,
        CancellationToken ct)
    {
        DemandCashier(actor, accountId);
        if (request.CollectionId == Guid.Empty || request.WorkSessionId == Guid.Empty ||
            request.ExpectedAccountVersion < 1 ||
            request.Allocations.Count is < 1 or > 500 ||
            request.Allocations.Any(line => line.AdditionId == Guid.Empty ||
                line.Quantity <= 0 || line.Quantity > 100000m) ||
            request.Allocations.Select(line => line.AdditionId).Distinct().Count() !=
                request.Allocations.Count ||
            request.Payments.Count is < 1 or > 10 ||
            request.Payments.Any(payment => payment.MethodCode is not
                ("Cash" or "DebitCard" or "CreditCard" or "Transfer") ||
                payment.Amount <= 0 || payment.RoundingAdjustment != 0 ||
                payment.TenderedAmount is { } tendered &&
                    (payment.MethodCode != "Cash" || tendered < payment.Amount) ||
                payment.Reference?.Length > 160 || payment.Notes?.Length > 500 ||
                (payment.MethodCode is "DebitCard" or "CreditCard") !=
                    (!string.IsNullOrWhiteSpace(payment.CardFranchiseCode) &&
                     !string.IsNullOrWhiteSpace(payment.ApprovalNumber)) ||
                payment.MethodCode == "Transfer" &&
                    string.IsNullOrWhiteSpace(payment.Reference)) ||
            request.Payments.Count(payment => payment.MethodCode == "Cash") > 1 ||
            string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 128)
            throw new RestaurantAccountValidationException(
                "El cobro parcial no tiene cantidades, medios o clave válidos.");
        return store.CaptureAsync(actor, accountId, request,
            idempotencyKey.Trim(), time.GetUtcNow(), ct);
    }

    public Task<RestaurantCollectionSummary> ReverseAsync(
        RestaurantAccountActor actor, Guid accountId, Guid collectionId,
        long expectedVersion, CancellationToken ct)
    {
        DemandCashier(actor, accountId);
        if (collectionId == Guid.Empty || expectedVersion < 1)
            throw new RestaurantAccountValidationException(
                "El cobro o la versión de la cuenta no son válidos.");
        return store.ReverseAsync(actor, accountId, collectionId,
            expectedVersion, time.GetUtcNow(), ct);
    }

    private static void DemandCashier(RestaurantAccountActor actor, Guid accountId)
    {
        if (accountId == Guid.Empty || actor.BusinessId == Guid.Empty ||
            actor.TenantId == Guid.Empty)
            throw new RestaurantAccountValidationException(
                "La mesa y la sede son obligatorias.");
        if (!actor.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new RestaurantAccountForbiddenException(
                "Solo un cajero autorizado puede recibir pagos de la mesa.");
    }
}
