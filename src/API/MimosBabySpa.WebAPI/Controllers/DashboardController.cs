using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.WebAPI.Authorization;
using MimosBabySpa.WebAPI.Extensions;

namespace MimosBabySpa.WebAPI.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    [HttpGet("stats")]
    [PermissionAuthorize("dashboard.read")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats(
        [FromQuery] Guid businessId,
        [FromQuery] string? period,
        CancellationToken ct)
    {
        return Ok(await _dashboardService.GetStatsAsync(User.GetTenantId(), businessId, period, ct));
    }

    [HttpGet("revenue-chart")]
    [PermissionAuthorize("dashboard.read")]
    public async Task<ActionResult<IReadOnlyList<ChartDataPointDto>>> GetRevenueChart(
        [FromQuery] Guid businessId,
        [FromQuery] string period,
        CancellationToken ct)
    {
        return Ok(await _dashboardService.GetRevenueChartAsync(User.GetTenantId(), businessId, period, ct));
    }

    [HttpGet("overview-chart")]
    [PermissionAuthorize("dashboard.read")]
    public async Task<ActionResult<IReadOnlyList<OverviewDataPointDto>>> GetOverviewChart(
        [FromQuery] Guid businessId,
        [FromQuery] string? period,
        CancellationToken ct)
    {
        return Ok(await _dashboardService.GetOverviewChartAsync(
            User.GetTenantId(), businessId, period ?? "30d", ct));
    }

    [HttpGet("top-services")]
    [PermissionAuthorize("dashboard.read")]
    public async Task<ActionResult<IReadOnlyList<TopServiceDto>>> GetTopServices(
        [FromQuery] Guid businessId,
        [FromQuery] int limit = 4,
        CancellationToken ct = default)
    {
        return Ok(await _dashboardService.GetTopServicesAsync(User.GetTenantId(), businessId, limit, ct));
    }

    [HttpGet("recent-reservations")]
    [PermissionAuthorize("dashboard.read")]
    public async Task<ActionResult<IReadOnlyList<RecentReservationDto>>> GetRecentReservations(
        [FromQuery] Guid businessId,
        [FromQuery] int limit = 5,
        CancellationToken ct = default)
    {
        return Ok(await _dashboardService.GetRecentReservationsAsync(User.GetTenantId(), businessId, limit, ct));
    }
}
