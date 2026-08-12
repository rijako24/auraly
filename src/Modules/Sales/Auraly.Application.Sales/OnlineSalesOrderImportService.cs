using Auraly.Contracts.Authorization;
using Auraly.Contracts.Sales;

namespace Auraly.Application.Sales;

public interface IOnlineSalesOrderImportStore
{
    Task<OnlineSalesDraft> ImportOrderAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        ImportOnlineSalesOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class OnlineSalesOrderImportService(
    IOnlineSalesOrderImportStore imports)
{
    public Task<OnlineSalesDraft> ImportAsync(
        OnlineSalesUserIdentity user,
        Guid draftId,
        ImportOnlineSalesOrderRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(request);
        if (!user.Permissions.Contains(CommercePermissionCodes.SalesCreate))
            throw new OnlineSalesDraftForbiddenException(
                $"Permission '{CommercePermissionCodes.SalesCreate}' is required.");
        if (draftId == Guid.Empty || request.SourceOrderId == Guid.Empty ||
            request.ExpectedVersion < 1 ||
            string.IsNullOrWhiteSpace(request.OrderNumber) ||
            request.OrderNumber.Length > 120 ||
            request.Lines.Count == 0 ||
            request.Lines.Count > 500 ||
            string.IsNullOrWhiteSpace(idempotencyKey) ||
            idempotencyKey.Length > 100 ||
            request.Lines.Any(line =>
                line.ProductId == Guid.Empty ||
                line.Quantity <= 0 ||
                line.UnitPrice < 0 ||
                line.DiscountAmount < 0 ||
                line.DiscountAmount > line.Quantity * line.UnitPrice))
            throw new OnlineSalesDraftValidationException(
                "El pedido no contiene datos comerciales válidos para llevarlo a la venta.");

        return imports.ImportOrderAsync(
            user,
            draftId,
            request,
            idempotencyKey.Trim(),
            cancellationToken);
    }
}
