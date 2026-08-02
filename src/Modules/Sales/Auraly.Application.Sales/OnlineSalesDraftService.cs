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

    Task<OnlineSalesDraft> CaptureAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        string value,
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

    Task<OnlineSalesDraft> SetDiscountAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        decimal discount,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<OnlineSalesDraft> RemoveLineAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<OnlineSalesCustomerSelection> SelectCustomerAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid? customerId,
        long expectedVersion,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<OnlineSalesProductPage> SearchProductsAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken);

    Task<OnlineSalesCustomerPage> SearchCustomersAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OnlineSalesDraft>> ListTemporariesAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken);

    Task<OnlineSalesDraft> PauseAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        PauseOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<OnlineSalesDraft> RecoverTemporaryAsync(
        OnlineSalesUserIdentity user,
        Guid temporaryDraftId,
        RecoverOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<OnlineSalesDraft> RemoveTemporaryAsync(
        OnlineSalesUserIdentity user,
        Guid temporaryDraftId,
        RemoveOnlineSalesTemporaryRequest request,
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
            request.Context.WarehouseId == Guid.Empty ||
            request.Context.WorkSessionId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "Negocio, bodega y sesión de trabajo son obligatorios.");
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

    public async Task<OnlineSalesDraft> CaptureAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        CaptureOnlineSalesDraftProductRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        if (string.IsNullOrWhiteSpace(request.Value) || request.Value.Length > 120 ||
            request.Quantity <= 0)
            throw new OnlineSalesDraftValidationException(
                "Código o referencia y cantidad positiva son obligatorios.");
        return await drafts.CaptureAsync(
            user, draftId, request.Value.Trim(), request.Quantity,
            request.ExpectedVersion, idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesDraft> SetDiscountAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        SetOnlineSalesDraftDiscountRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        if (lineId == Guid.Empty || request.Discount < 0)
            throw new OnlineSalesDraftValidationException(
                "Línea y descuento no negativo son obligatorios.");
        return await drafts.SetDiscountAsync(
            user, draftId, lineId, request.Discount,
            request.ExpectedVersion, idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesDraft> RemoveLineAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        Guid lineId,
        RemoveOnlineSalesDraftLineRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        if (lineId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "La línea es obligatoria.");
        return await drafts.RemoveLineAsync(
            user, draftId, lineId, request.ExpectedVersion,
            idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesCustomerSelection> SelectCustomerAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        SelectOnlineSalesDraftCustomerRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        return await drafts.SelectCustomerAsync(
            user, draftId, request.CustomerId, request.ExpectedVersion,
            idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesProductPage> SearchProductsAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateSearch(request);
        return await drafts.SearchProductsAsync(user, request, cancellationToken);
    }

    public async Task<OnlineSalesCustomerPage> SearchCustomersAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateSearch(request);
        return await drafts.SearchCustomersAsync(user, request, cancellationToken);
    }

    public async Task<IReadOnlyList<OnlineSalesDraft>> ListTemporariesAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateSearch(request);
        return await drafts.ListTemporariesAsync(user, request, cancellationToken);
    }

    public async Task<OnlineSalesDraft> PauseAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        PauseOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(draftId, request.ExpectedVersion, idempotencyKey);
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120 ||
            request.Reference?.Length > 120 || request.Observation?.Length > 500)
            throw new OnlineSalesDraftValidationException(
                "El nombre es obligatorio y los textos superan la longitud permitida.");
        return await drafts.PauseAsync(
            user, draftId, request with { Name = request.Name.Trim() },
            idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesDraft> RecoverTemporaryAsync(
        OnlineSalesUserIdentity user,
        Guid temporaryDraftId,
        RecoverOnlineSalesDraftRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(
            temporaryDraftId, request.ExpectedTemporaryVersion, idempotencyKey);
        if (request.ExpectedActiveVersion < 1)
            throw new OnlineSalesDraftValidationException(
                "La versión de la venta activa es obligatoria.");
        return await drafts.RecoverTemporaryAsync(
            user, temporaryDraftId, request, idempotencyKey, cancellationToken);
    }

    public async Task<OnlineSalesDraft> RemoveTemporaryAsync(
        OnlineSalesUserIdentity user,
        Guid temporaryDraftId,
        RemoveOnlineSalesTemporaryRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateMutation(temporaryDraftId, request.ExpectedVersion, idempotencyKey);
        return await drafts.RemoveTemporaryAsync(
            user, temporaryDraftId, request, idempotencyKey, cancellationToken);
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

    private static void ValidateSearch(SearchOnlineSalesRequest request)
    {
        if (request.Context.BusinessId == Guid.Empty ||
            request.Context.WarehouseId == Guid.Empty ||
            request.Context.WorkSessionId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "Negocio, bodega y sesión de trabajo son obligatorios.");
        if (request.Skip < 0 || request.Take is < 1 or > 100)
            throw new OnlineSalesDraftValidationException(
                "La paginación solicitada no es válida.");
        if (request.Search?.Length > 120)
            throw new OnlineSalesDraftValidationException(
                "La búsqueda admite máximo 120 caracteres.");
    }
}
