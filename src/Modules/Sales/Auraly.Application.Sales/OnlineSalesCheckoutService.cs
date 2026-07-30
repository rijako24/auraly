using Auraly.Contracts.Authorization;
using Auraly.Contracts.Fiscal;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record OnlineSalesFiscalKeyContext(FiscalKeyReference Reference);

public sealed record PreparedOnlineSalesCheckout(
    PosSaleUploadRequest Request,
    OnlineSalesDraft NextDraft,
    bool IsReplay);

public interface IOnlineSalesCheckoutStore
{
    Task<OnlineSalesFiscalKeyContext> ResolveFiscalKeyContextAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CancellationToken cancellationToken);

    Task<PreparedOnlineSalesCheckout> PrepareAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        FiscalVerificationMaterial fiscalMaterial,
        CancellationToken cancellationToken);

    Task MarkResultAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid documentId,
        string status,
        CancellationToken cancellationToken);
}

public sealed class OnlineSalesCheckoutService(
    IOnlineSalesCheckoutStore checkouts,
    IFiscalTechnicalKeyProvider technicalKeys,
    ReceivePosSaleService receiver)
{
    private static readonly HashSet<string> PaymentMethods =
    [
        "Cash",
        "DebitCard",
        "CreditCard",
        "Transfer"
    ];

    public async Task<CompleteOnlineSalesDraftResponse> CompleteAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        Validate(draftId, request, idempotencyKey);
        var keyContext = await checkouts.ResolveFiscalKeyContextAsync(
            user, draftId, cancellationToken);
        var material = await technicalKeys.ResolveAsync(
            keyContext.Reference, cancellationToken)
            ?? throw new OnlineSalesDraftValidationException(
                "La clave técnica de la resolución fiscal activa no está disponible.");
        var prepared = await checkouts.PrepareAsync(
            user, draftId, request, idempotencyKey.Trim(),
            material, cancellationToken);
        var reception = await receiver.ReceiveOnlineAsync(
            user,
            $"online:{prepared.Request.DocumentId:N}",
            prepared.Request,
            cancellationToken);
        await checkouts.MarkResultAsync(
            user,
            draftId,
            prepared.Request.DocumentId,
            reception.Status == PosSaleRemoteStatuses.FiscalIntegrityConflict
                ? "FiscalConflict"
                : "Completed",
            cancellationToken);
        return new CompleteOnlineSalesDraftResponse(
            OnlineSalesReceiptMapper.From(prepared.Request, reception.Status),
            prepared.NextDraft,
            prepared.IsReplay || reception.IsDuplicate);
    }

    private static void DemandPermission(OnlineSalesUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
    }

    private static void Validate(
        Guid draftId,
        CompleteOnlineSalesDraftRequest request,
        string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (draftId == Guid.Empty || request.ExpectedVersion < 1)
            throw new OnlineSalesDraftValidationException(
                "Borrador y versión esperada son obligatorios.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new OnlineSalesDraftValidationException(
                "Idempotency-Key es obligatorio y admite máximo 100 caracteres.");
        if (request.Payments.Count is < 1 or > 10)
            throw new OnlineSalesDraftValidationException(
                "La venta requiere entre uno y diez medios de pago.");
        if (request.Payments.Any(payment =>
                !PaymentMethods.Contains(payment.MethodCode) ||
                payment.Amount <= 0 ||
                payment.Reference?.Length > 160))
            throw new OnlineSalesDraftValidationException(
                "Uno de los medios de pago no es válido.");
        if (request.Payments.Count(payment => payment.MethodCode == "Cash") > 1)
            throw new OnlineSalesDraftValidationException(
                "La venta admite una sola línea de efectivo.");
    }
}
