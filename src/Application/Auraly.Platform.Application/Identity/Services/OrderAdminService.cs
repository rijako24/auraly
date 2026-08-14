using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class OrderAdminService : IOrderAdminService
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderAdminService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResponse<OrderDto>> GetPagedByBusinessIdAsync(
        Guid tenantId,
        Guid businessId,
        PagedRequest request,
        string? customer = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        OrderStatus? status = null,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (items, totalCount) = await _unitOfWork.Orders.GetPagedByBusinessIdAsync(
            businessId,
            request.Page,
            request.PageSize,
            request.Search,
            customer,
            createdFrom,
            createdTo,
            status,
            ct);

        return new PagedResponse<OrderDto>(
            items.Select(MapToDto).ToList(),
            totalCount,
            request.Page,
            request.PageSize);
    }

    public async Task<OrderSummaryDto> GetSummaryByBusinessIdAsync(
        Guid tenantId,
        Guid businessId,
        string? search = null,
        string? customer = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        OrderStatus? status = null,
        CancellationToken ct = default)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var summary = await _unitOfWork.Orders.GetSummaryByBusinessIdAsync(
            businessId,
            search,
            customer,
            createdFrom,
            createdTo,
            status,
            ct);

        return new OrderSummaryDto(
            summary.TotalOrders,
            summary.TotalAmount,
            summary.DraftCount,
            summary.AwaitingPaymentCount,
            summary.ConfirmedCount,
            summary.SyncedCount,
            summary.CancelledCount);
    }

    public async Task<OrderDto> GetByIdAsync(Guid tenantId, Guid orderId, CancellationToken ct = default)
    {
        var businesses = await _unitOfWork.Businesses.GetByTenantIdAsync(tenantId, ct);

        foreach (var business in businesses)
        {
            var order = await _unitOfWork.Orders.GetByIdAsync(business.BusinessId, orderId, ct);
            if (order is not null)
                return MapToDto(order);
        }

        throw new NotFoundException(nameof(Order), orderId);
    }

    private static OrderDto MapToDto(Order order) =>
        new(
            order.OrderId,
            order.BusinessId,
            order.AgentId,
            order.ConversationId,
            order.IntegrationConnectionId,
            order.PaymentTransactionId,
            order.Source.ToString(),
            order.FulfillmentMode.ToString(),
            order.Status.ToString(),
            order.CustomerNameSnapshot,
            order.CustomerEmailSnapshot,
            order.CustomerPhoneSnapshot,
            order.CustomerDocumentSnapshot,
            order.DeliveryAddressSnapshot,
            order.Notes,
            order.Currency,
            order.Subtotal,
            order.DiscountTotal,
            order.Total,
            order.CustomerConfirmed,
            order.ExternalOrderId,
            order.ExternalDocumentNumber,
            order.ExternalStatus,
            order.CustomAttributesJson,
            order.CreatedAt,
            order.UpdatedAt,
            order.Items.Select(MapItemToDto).ToList());

    private static OrderItemDto MapItemToDto(OrderItem item) =>
        new(
            item.OrderItemId,
            item.OrderId,
            item.BusinessId,
            item.ProductId,
            item.IntegrationConnectionId,
            item.ExternalProductId,
            item.Sku,
            item.ProductNameSnapshot,
            item.DescriptionSnapshot,
            item.Quantity,
            item.UnitPrice,
            item.DiscountAmount,
            item.LineTotal,
            item.CreatedAt,
            item.UpdatedAt);

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
