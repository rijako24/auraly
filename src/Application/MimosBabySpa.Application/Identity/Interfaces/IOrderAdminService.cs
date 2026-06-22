using MimosBabySpa.Application.Common.DTOs;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Domain.Enums;

namespace MimosBabySpa.Application.Identity.Interfaces;

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
