using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record StoredOnlineSalesReceipt(
    PosSaleUploadRequest Request,
    string? FiscalStatus);

public interface IOnlineSalesHistoryStore
{
    Task<OnlineSalesCustomerPage> SearchCustomersAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesHistoryOptionsRequest request,
        CancellationToken cancellationToken);

    Task<OnlineSalesProductPage> SearchProductsAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesHistoryOptionsRequest request,
        CancellationToken cancellationToken);

    Task<OnlineSalesCustomer?> GetCustomerAsync(
        OnlineSalesUserIdentity user,
        GetOnlineSalesCustomerRequest request,
        CancellationToken cancellationToken);

    Task<OnlineSalesIssuedSalePage> SearchAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesIssuedSalesRequest request,
        CancellationToken cancellationToken);

    Task<StoredOnlineSalesReceipt?> GetReceiptAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext context,
        Guid documentId,
        CancellationToken cancellationToken);

    Task<bool> RecordReprintAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext context,
        Guid documentId,
        CancellationToken cancellationToken);
}
public sealed class OnlineSalesHistoryService(IOnlineSalesHistoryStore history)
{
    public Task<OnlineSalesCustomerPage> SearchCustomersAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesHistoryOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateHistoryOptions(user, request);
        return history.SearchCustomersAsync(user, request, cancellationToken);
    }

    public Task<OnlineSalesProductPage> SearchProductsAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesHistoryOptionsRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateHistoryOptions(user, request);
        return history.SearchProductsAsync(user, request, cancellationToken);
    }

    public Task<OnlineSalesCustomer?> GetCustomerAsync(
        OnlineSalesUserIdentity user,
        GetOnlineSalesCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandCreatePermission(user);
        ValidateContext(request.Context);
        if (request.CustomerId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "El cliente es obligatorio.");
        return history.GetCustomerAsync(user, request, cancellationToken);
    }

    public Task<OnlineSalesIssuedSalePage> SearchAsync(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesIssuedSalesRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandReprintPermission(user);
        ValidateHistoryContext(request.Context);
        if (request.Skip < 0 || request.Take is < 1 or > 100)
            throw new OnlineSalesDraftValidationException(
                "La paginación solicitada no es válida.");
        if (request.Search?.Length > 120)
            throw new OnlineSalesDraftValidationException(
                "La búsqueda admite máximo 120 caracteres.");
        if ((request.CustomerId is null) != (request.PartySiteId is null) ||
            request.From > request.To ||
            request.To == DateOnly.MaxValue ||
            request.MinimumTotal < 0 || request.MaximumTotal < 0 ||
            request.MinimumTotal > request.MaximumTotal)
            throw new OnlineSalesDraftValidationException(
                "Cliente, sede, fechas y rango de valores deben ser válidos.");
        return history.SearchAsync(user, request, cancellationToken);
    }

    public async Task<OnlineSalesReceipt?> GetReceiptAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext context,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        DemandReprintPermission(user);
        ValidateHistoryContext(context);
        if (documentId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "El documento es obligatorio.");
        var stored = await history.GetReceiptAsync(
            user, context, documentId, cancellationToken);
        return stored is null
            ? null
            : SalesInvoicePresentationMapper.From(stored.Request, stored.FiscalStatus);
    }

    public async Task<bool> RecordReprintAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesHistoryContext context,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        DemandReprintPermission(user);
        ValidateHistoryContext(context);
        if (documentId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "El documento es obligatorio.");
        return await history.RecordReprintAsync(
            user, context, documentId, cancellationToken);
    }

    private static void DemandCreatePermission(OnlineSalesUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
    }

    private static void DemandReprintPermission(OnlineSalesUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesReprint))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesReprint}' is required.");
    }

    private static void ValidateHistoryOptions(
        OnlineSalesUserIdentity user,
        SearchOnlineSalesHistoryOptionsRequest request)
    {
        DemandReprintPermission(user);
        ValidateHistoryContext(request.Context);
        if (request.Skip < 0 || request.Take is < 1 or > 100 ||
            request.Search?.Length > 120)
            throw new OnlineSalesDraftValidationException(
                "La búsqueda y paginación solicitadas no son válidas.");
    }

    private static void ValidateContext(OnlineSalesDraftContext context)
    {
        if (context.BusinessId == Guid.Empty ||
            context.WorkSessionId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "La sede y la sesión de trabajo son obligatorias.");
    }

    private static void ValidateHistoryContext(OnlineSalesHistoryContext context)
    {
        if (context.BusinessId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "La sede es obligatoria.");
    }
}
