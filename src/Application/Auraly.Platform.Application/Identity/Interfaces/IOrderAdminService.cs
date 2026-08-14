using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Domain.Enums;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IOrderAdminService
{
    Task<PagedResponse<OrderDto>> GetPagedByBusinessIdAsync(
        Guid tenantId,
        Guid businessId,
        PagedRequest request,
        string? customer = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        OrderStatus? status = null,
        CancellationToken ct = default);

    Task<OrderSummaryDto> GetSummaryByBusinessIdAsync(
        Guid tenantId,
        Guid businessId,
        string? search = null,
        string? customer = null,
        DateTime? createdFrom = null,
        DateTime? createdTo = null,
        OrderStatus? status = null,
        CancellationToken ct = default);

    Task<OrderDto> GetByIdAsync(
        Guid tenantId,
        Guid orderId,
        CancellationToken ct = default);
}
