using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record OnlineSalesUserIdentity(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public interface IOnlineSalesDraftStore
{
    Task<OnlineSalesDraft> GetOrCreateActiveAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext context,
        CancellationToken cancellationToken);

    Task<OnlineSalesDraft> AddProductAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid productId,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<OnlineSalesDraft> ChangeQuantityAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        decimal quantity,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<OnlineSalesDraft> ResetAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class OnlineSalesDraftForbiddenException(string message) : Exception(message);
public sealed class OnlineSalesDraftValidationException(string message) : Exception(message);
public sealed class OnlineSalesDraftConcurrencyException(string message) : Exception(message);
public sealed class OnlineSalesDraftIdempotencyException(string message) : Exception(message);

public sealed class OnlineSalesDraftService(
    IOnlineSalesDraftStore drafts)
{
    public async Task<OnlineSalesDraft> OpenAsync(
        OnlineSalesUserIdentity user,
        OpenOnlineSalesDraftRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        if (request.Context.BusinessId == Guid.Empty ||
            request.Context.LocationId == Guid.Empty ||
            request.Context.RegisterId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "Negocio, sede y caja son obligatorios.");
        return await drafts.GetOrCreateActiveAsync(
            user, request.Context, cancellationToken);
    }

    public async Task<OnlineSalesDraft> AddProductAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        AddOnlineSalesDraftProductRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        if (request.ProductId == Guid.Empty || request.Quantity <= 0)
            throw new OnlineSalesDraftValidationException(
                "Producto y cantidad positiva son obligatorios.");
        return await drafts.AddProductAsync(
            user, draftId, request.ProductId, request.Quantity,
            request.ExpectedVersion, idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesDraft> ChangeQuantityAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        ChangeOnlineSalesDraftQuantityRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        if (lineId == Guid.Empty || request.Quantity <= 0)
            throw new OnlineSalesDraftValidationException(
                "Línea y cantidad positiva son obligatorias.");
        return await drafts.ChangeQuantityAsync(
            user, draftId, lineId, request.Quantity,
            request.ExpectedVersion, idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesDraft> ResetAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        ResetOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        return await drafts.ResetAsync(
            user, draftId, request.ExpectedVersion,
            idempotencyKey, cancellationToken);
    }

    private static void DemandPermission(OnlineSalesUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
    }

    private static void ValidateMutation(
        Guid draftId,
        long expectedVersion,
        string idempotencyKey)
    {
        if (draftId == Guid.Empty || expectedVersion < 1)
            throw new OnlineSalesDraftValidationException(
                "Borrador y versión esperada son obligatorios.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new OnlineSalesDraftValidationException(
                "Idempotency-Key es obligatorio y admite máximo 100 caracteres.");
    }
}
