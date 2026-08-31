using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record ServiceInvoiceUserIdentity(
    Guid UserId,
    Guid TenantId,
    IReadOnlySet<string> Permissions);

public interface IServiceInvoiceStore
{
    Task<BillableServicePage> SearchServicesAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceSearchRequest request,
        CancellationToken cancellationToken);

    Task<ServiceInvoiceCustomerPage> SearchCustomersAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceSearchRequest request,
        CancellationToken cancellationToken);

    Task<IssuedServiceInvoice> IssueAsync(
        ServiceInvoiceUserIdentity user,
        IssueServiceInvoiceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<ServiceInvoiceHistoryPage> SearchInvoicesAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceHistoryRequest request,
        CancellationToken cancellationToken);

    Task<ServiceInvoiceDetail?> GetInvoiceAsync(
        ServiceInvoiceUserIdentity user,
        Guid businessId,
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed class ServiceInvoiceValidationException(string message) : Exception(message);
public sealed class ServiceInvoiceForbiddenException(string message) : Exception(message);
public sealed class ServiceInvoiceIdempotencyException(string message) : Exception(message);

public sealed class ServiceInvoiceWorkspaceService(IServiceInvoiceStore store)
{
    public Task<BillableServicePage> SearchServicesAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, ServiceInvoicePermissionCodes.Read);
        ValidateSearch(request);
        return store.SearchServicesAsync(user, request, cancellationToken);
    }

    public Task<ServiceInvoiceCustomerPage> SearchCustomersAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, ServiceInvoicePermissionCodes.Read);
        ValidateSearch(request);
        return store.SearchCustomersAsync(user, request, cancellationToken);
    }

    public Task<IssuedServiceInvoice> IssueAsync(
        ServiceInvoiceUserIdentity user,
        IssueServiceInvoiceRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        Demand(user, ServiceInvoicePermissionCodes.Create);
        Demand(user, ServiceInvoicePermissionCodes.Issue);
        if (request.BusinessId == Guid.Empty || request.CustomerId == Guid.Empty)
            throw new ServiceInvoiceValidationException(
                "El negocio y el cliente son obligatorios.");
        if (request.Lines is null || request.Lines.Count == 0 || request.Lines.Count > 200)
            throw new ServiceInvoiceValidationException(
                "La factura debe contener entre 1 y 200 servicios.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 96)
            throw new ServiceInvoiceValidationException(
                "La clave de idempotencia es obligatoria y admite máximo 96 caracteres.");
        foreach (var line in request.Lines)
        {
            if (line.BillableServiceId == Guid.Empty || line.Quantity <= 0)
                throw new ServiceInvoiceValidationException(
                    "Cada servicio debe tener identidad y cantidad positiva.");
            if (line.Quantity > 999999 || line.UnitPrice is < 0 || line.DiscountValue < 0)
                throw new ServiceInvoiceValidationException(
                    "La cantidad, precio o descuento de una línea no es válido.");
            if (line.Description?.Length > 500)
                throw new ServiceInvoiceValidationException(
                    "La descripción admite máximo 500 caracteres.");
            if (line.UnitPrice is not null)
                Demand(user, ServiceInvoicePermissionCodes.OverridePrice);
            if (line.DiscountValue > 0)
                Demand(user, ServiceInvoicePermissionCodes.Discount);
        }
        if (request.CreditAmount < 0 ||
            (request.CreditAmount == 0) != (request.CreditDueDate is null))
            throw new ServiceInvoiceValidationException(
                "El crédito requiere un valor positivo y una fecha de vencimiento.");
        return store.IssueAsync(user, request, idempotencyKey.Trim(), cancellationToken);
    }

    public Task<ServiceInvoiceHistoryPage> SearchInvoicesAsync(
        ServiceInvoiceUserIdentity user,
        ServiceInvoiceHistoryRequest request,
        CancellationToken cancellationToken = default)
    {
        Demand(user, ServiceInvoicePermissionCodes.Read);
        if (request.BusinessId == Guid.Empty || request.Page < 1 ||
            request.PageSize is < 1 or > 100 || request.Query?.Length > 120 ||
            request.From > request.To)
            throw new ServiceInvoiceValidationException(
                "Los filtros del historial no son válidos.");
        return store.SearchInvoicesAsync(user, request, cancellationToken);
    }

    public Task<ServiceInvoiceDetail?> GetInvoiceAsync(
        ServiceInvoiceUserIdentity user,
        Guid businessId,
        Guid documentId,
        bool forPrint,
        CancellationToken cancellationToken = default)
    {
        Demand(user, forPrint
            ? ServiceInvoicePermissionCodes.Print
            : ServiceInvoicePermissionCodes.Read);
        if (businessId == Guid.Empty || documentId == Guid.Empty)
            throw new ServiceInvoiceValidationException(
                "El negocio y el documento son obligatorios.");
        return store.GetInvoiceAsync(user, businessId, documentId, cancellationToken);
    }

    private static void ValidateSearch(ServiceInvoiceSearchRequest request)
    {
        if (request.BusinessId == Guid.Empty || request.Page < 1 ||
            request.PageSize is < 1 or > 100 || request.Query?.Length > 120)
            throw new ServiceInvoiceValidationException(
                "Los parámetros de búsqueda no son válidos.");
    }

    private static void Demand(ServiceInvoiceUserIdentity user, string permission)
    {
        if (!user.Permissions.Contains(permission))
            throw new ServiceInvoiceForbiddenException(
                $"El usuario no tiene el permiso '{permission}'.");
    }
}
