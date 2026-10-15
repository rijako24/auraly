using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public interface IRestaurantCollectionStore
{
    Task<RestaurantTipPendingPage> PendingTipsAsync(
        RestaurantAccountActor actor, CancellationToken ct);
    Task<IReadOnlyList<RestaurantTipSettlement>> RecentTipSettlementsAsync(
        RestaurantAccountActor actor, CancellationToken ct);
    Task<RestaurantTipSettlement> SettleTipsAsync(
        RestaurantAccountActor actor, SettleRestaurantTipsRequest request,
        DateTimeOffset now, CancellationToken ct);
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
    public Task<RestaurantTipPendingPage> PendingTipsAsync(
        RestaurantAccountActor actor, CancellationToken ct)
    {
        DemandTipSettlement(actor);
        return store.PendingTipsAsync(actor, ct);
    }

    public Task<IReadOnlyList<RestaurantTipSettlement>> RecentTipSettlementsAsync(
        RestaurantAccountActor actor, CancellationToken ct)
    {
        DemandTipSettlement(actor);
        return store.RecentTipSettlementsAsync(actor, ct);
    }

    public Task<RestaurantTipSettlement> SettleTipsAsync(
        RestaurantAccountActor actor, SettleRestaurantTipsRequest request,
        CancellationToken ct)
    {
        DemandTipSettlement(actor);
        if (request.SettlementId == Guid.Empty ||
            request.CollectionIds.Count is < 1 or > 100 ||
            request.CollectionIds.Any(id => id == Guid.Empty) ||
            request.CollectionIds.Distinct().Count() != request.CollectionIds.Count ||
            request.Recipients.Count is < 1 or > 40 ||
            request.Recipients.Any(line => line.PartyId == Guid.Empty || line.Amount < 0) ||
            request.Recipients.Select(line => line.PartyId).Distinct().Count() != request.Recipients.Count ||
            request.DistributionBasis is not ("EqualByLaw" or "WorkerAgreement") ||
            (request.DistributionBasis == "WorkerAgreement" &&
             (string.IsNullOrWhiteSpace(request.AgreementReference) ||
              request.AgreementReference.Length > 300 ||
              request.Recipients.Any(line => line.Amount <= 0))) ||
            (request.DistributionBasis == "EqualByLaw" &&
             (request.AgreementReference is not null ||
              request.Recipients.Any(line => line.Amount != 0))) ||
            request.PaymentMethodCode is not ("Cash" or "Transfer") ||
            request.PaymentMethodCode == "Cash" && request.BankAccountId is not null ||
            request.PaymentMethodCode == "Transfer" &&
              (request.BankAccountId is null ||
               string.IsNullOrWhiteSpace(request.PaymentReference)) ||
            request.PaymentReference?.Length > 160)
            throw new RestaurantAccountValidationException(
                "Selecciona propinas pendientes, personal, regla de reparto y medio de entrega válidos.");
        return store.SettleTipsAsync(actor, request,
            time.GetUtcNow(), ct);
    }

    private static void DemandTipSettlement(RestaurantAccountActor actor)
    {
        if (actor.BusinessId == Guid.Empty || actor.TenantId == Guid.Empty)
            throw new RestaurantAccountValidationException("Selecciona una sede.");
        if (!actor.Permissions.Contains(CommercePermissionCodes.CashClose))
            throw new RestaurantAccountForbiddenException(
                "Necesitas permiso de cierre de caja para liquidar propinas.");
    }

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
            request.Allocations.Count > 500 ||
            request.Allocations.Any(line => line.AdditionId == Guid.Empty ||
                line.Quantity <= 0 || line.Quantity > 100000m) ||
            request.Allocations.Select(line => line.AdditionId).Distinct().Count() !=
                request.Allocations.Count ||
            request.Payments.Count > 10 ||
            (request.Allocations.Count == 0 &&
              (request.Payments.Count != 0 || request.TipPayment is null)) ||
            (request.Allocations.Count > 0 && request.Payments.Count == 0) ||
            request.Payments.Any(payment => payment.MethodCode is not
                ("Cash" or "DebitCard" or "CreditCard" or "Transfer") ||
                payment.Amount <= 0 ||
                payment.RoundingAdjustment is <= -50m or > 50m ||
                payment.CollectedAmount <= 0 ||
                payment.TenderedAmount is { } tendered &&
                    (payment.MethodCode != "Cash" || tendered < payment.CollectedAmount) ||
                payment.Reference?.Length > 160 || payment.Notes?.Length > 500 ||
                (payment.MethodCode is "DebitCard" or "CreditCard") !=
                    (!string.IsNullOrWhiteSpace(payment.CardFranchiseCode) &&
                     !string.IsNullOrWhiteSpace(payment.ApprovalNumber)) ||
                payment.MethodCode == "Transfer" &&
                    string.IsNullOrWhiteSpace(payment.Reference)) ||
            request.Payments.Count(payment => payment.MethodCode == "Cash") > 1 ||
            request.Payments.Count(payment => payment.RoundingAdjustment != 0m) > 1 ||
            request.TipPayment is { } tip &&
              (tip.MethodCode is not ("Cash" or "DebitCard" or "CreditCard" or "Transfer") ||
               tip.Amount <= 0 || tip.Amount > 1000000000m ||
               decimal.Truncate(tip.Amount) != tip.Amount ||
               tip.RoundingAdjustment != 0 ||
               tip.TenderedAmount is { } tipTendered &&
                 (tip.MethodCode != "Cash" || tipTendered < tip.Amount) ||
               tip.MethodCode != "Transfer" && tip.BankAccountId is not null ||
               (tip.MethodCode is "DebitCard" or "CreditCard") !=
                 (!string.IsNullOrWhiteSpace(tip.CardFranchiseCode) &&
                  !string.IsNullOrWhiteSpace(tip.ApprovalNumber)) ||
               tip.MethodCode == "Transfer" && string.IsNullOrWhiteSpace(tip.Reference)) ||
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
