using Auraly.Platform.Application.Common.DTOs;
using Auraly.Platform.Application.Identity.DTOs;

namespace Auraly.Platform.Application.Identity.Interfaces;

public interface IReservationAdminService
{
    Task<ReservationDto> GetByIdAsync(Guid tenantId, Guid reservationId, CancellationToken ct = default);
    Task<IReadOnlyList<ReservationDto>> GetByBusinessIdAsync(Guid tenantId, Guid businessId, CancellationToken ct = default);
    Task<PagedResponse<ReservationDto>> GetPagedByBusinessIdAsync(
        Guid tenantId, Guid businessId, PagedRequest request, DateTime? startDate = null, DateTime? endDate = null, CancellationToken ct = default);
    Task<IReadOnlyList<ReservationDto>> GetByBusinessIdAndDateRangeAsync(
        Guid tenantId, Guid businessId, DateTime startDate, DateTime endDate, CancellationToken ct = default);
    Task<ReservationDto> CreateAsync(Guid tenantId, CreateReservationRequest request, CancellationToken ct = default);
    Task<ReservationDto> UpdateAsync(Guid tenantId, Guid reservationId, UpdateReservationRequest request, CancellationToken ct = default);
    Task CancelAsync(Guid tenantId, Guid reservationId, CancellationToken ct = default);
}
