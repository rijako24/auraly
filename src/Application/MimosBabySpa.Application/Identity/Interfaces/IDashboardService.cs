using MimosBabySpa.Application.Identity.DTOs;

namespace MimosBabySpa.Application.Identity.Interfaces;

public interface IDashboardService
{
    Task<DashboardStatsDto> GetStatsAsync(
        Guid tenantId, Guid businessId, string? period = null, CancellationToken ct = default);

    Task<IReadOnlyList<ChartDataPointDto>> GetRevenueChartAsync(
        Guid tenantId, Guid businessId, string period, CancellationToken ct = default);

    Task<IReadOnlyList<OverviewDataPointDto>> GetOverviewChartAsync(
        Guid tenantId, Guid businessId, string period, CancellationToken ct = default);

    Task<IReadOnlyList<TopServiceDto>> GetTopServicesAsync(
        Guid tenantId, Guid businessId, int limit = 5, CancellationToken ct = default);

    Task<IReadOnlyList<RecentReservationDto>> GetRecentReservationsAsync(
        Guid tenantId, Guid businessId, int limit = 5, CancellationToken ct = default);
}
