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
        var snapshot = request.CommercialSnapshot;
        var productCodes = request.UblSnapshot?.Lines
            .ToDictionary(line => line.LineNumber, line => line.ProductCode)
            ?? [];
        return new OnlineSalesReceipt(
            request.DocumentId,
            snapshot.DocumentType,
            request.DocumentNumber.FullNumber,
            request.FiscalSnapshot?.FiscalNumber,
            snapshot.IssuedAt,
            snapshot.CustomerIdentification,
            request.Lines.Select(line => new OnlineSalesReceiptLine(
                productCodes.GetValueOrDefault(line.LineNumber, string.Empty),
                line.Description,
                line.Quantity,
                line.UnitPrice,
                line.DiscountAmount,
                line.TaxAmount,
                line.LineTotal,
                line.TaxCode,
                line.TaxRate)).ToArray(),
            request.Payments.Select(payment => new OnlineSalesPayment(
                    payment.MethodCode,
                    payment.Amount,
                    payment.Reference,
                    payment.CardFranchiseCode,
                    payment.ApprovalNumber,
                    payment.BankAccountId,
                    payment.Notes))
                .Concat(request.Credit is null
                    ? []
                    : [new OnlineSalesPayment("Credit", request.Credit.Amount, request.Credit.DueDate.ToString("O"))])
                .ToArray(),
            snapshot.UntaxedAmount,
            snapshot.TaxAmount,
            snapshot.PayableAmount,
            request.FiscalSnapshot?.Cufe,
            request.FiscalSnapshot?.QrPayload,
            fiscalStatus,
            request.UblSnapshot?.Customer.RegistrationName ?? "Consumidor final",
            WithholdingTotal: snapshot.Withholding?.WithholdingTotal ?? 0m,
            NetPayableAmount: snapshot.Withholding?.NetAmount ?? snapshot.PayableAmount,
            Withholdings: snapshot.Withholding?.Lines);
    }
}
