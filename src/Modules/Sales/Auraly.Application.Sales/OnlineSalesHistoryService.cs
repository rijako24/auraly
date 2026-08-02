using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public sealed record StoredOnlineSalesReceipt(
    PosSaleUploadRequest Request,
    string FiscalStatus);

public interface IOnlineSalesHistoryStore
{
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
        OnlineSalesDraftContext context,
        Guid documentId,
        CancellationToken cancellationToken);
}

public sealed class OnlineSalesHistoryService(IOnlineSalesHistoryStore history)
{
    public Task<OnlineSalesCustomer?> GetCustomerAsync(
        OnlineSalesUserIdentity user,
        GetOnlineSalesCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
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
        DemandPermission(user);
        ValidateContext(request.Context);
        if (request.Skip < 0 || request.Take is < 1 or > 100)
            throw new OnlineSalesDraftValidationException(
                "La paginación solicitada no es válida.");
        if (request.Search?.Length > 120)
            throw new OnlineSalesDraftValidationException(
                "La búsqueda admite máximo 120 caracteres.");
        return history.SearchAsync(user, request, cancellationToken);
    }

    public async Task<OnlineSalesReceipt?> GetReceiptAsync(
        OnlineSalesUserIdentity user,
        OnlineSalesDraftContext context,
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        DemandPermission(user);
        ValidateContext(context);
        if (documentId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "El documento es obligatorio.");
        var stored = await history.GetReceiptAsync(
            user, context, documentId, cancellationToken);
        return stored is null
            ? null
            : OnlineSalesReceiptMapper.From(stored.Request, stored.FiscalStatus);
    }

    private static void DemandPermission(OnlineSalesUserIdentity user)
    {
        ArgumentNullException.ThrowIfNull(user);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
    }

    private static void ValidateContext(OnlineSalesDraftContext context)
    {
        if (context.BusinessId == Guid.Empty ||
            context.WorkSessionId == Guid.Empty)
            throw new OnlineSalesDraftValidationException(
                "La sede y la sesión de trabajo son obligatorias.");
    }
}

public static class OnlineSalesReceiptMapper
{
    public static OnlineSalesReceipt From(
        PosSaleUploadRequest request,
        string fiscalStatus)
    {
        ArgumentNullException.ThrowIfNull(request);
        var productCodes = request.UblSnapshot?.Lines
            .ToDictionary(line => line.LineNumber, line => line.ProductCode)
            ?? [];
        return new OnlineSalesReceipt(
            request.DocumentId,
            request.DocumentNumber.FullNumber,
            request.FiscalSnapshot.FiscalNumber,
            request.FiscalSnapshot.IssuedAt,
            request.FiscalSnapshot.CustomerIdentification,
            request.Lines.Select(line => new OnlineSalesReceiptLine(
                productCodes.GetValueOrDefault(line.LineNumber, string.Empty),
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.TaxAmount,
                line.LineTotal)).ToArray(),
            request.Payments.Select(payment => new OnlineSalesPayment(
                payment.MethodCode,
                payment.Amount,
                payment.Reference)).ToArray(),
            request.FiscalSnapshot.UntaxedAmount,
            request.FiscalSnapshot.TaxAmount,
            request.FiscalSnapshot.PayableAmount,
            request.FiscalSnapshot.Cufe,
            request.FiscalSnapshot.QrPayload,
            fiscalStatus,
            request.UblSnapshot?.Customer.RegistrationName ?? "Consumidor final");
    }
}
