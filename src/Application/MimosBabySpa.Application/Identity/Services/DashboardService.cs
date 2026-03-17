using MimosBabySpa.Application.Common.Exceptions;
using MimosBabySpa.Application.Identity.DTOs;
using MimosBabySpa.Application.Identity.Interfaces;
using MimosBabySpa.Domain.Entities;
using MimosBabySpa.Domain.Enums;
using MimosBabySpa.Domain.Repositories;

namespace MimosBabySpa.Application.Identity.Services;

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _unitOfWork;

    public DashboardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(
        Guid tenantId, Guid businessId, string? period, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (from, to) = ParsePeriod(period ?? "30d");
        var (prevFrom, prevTo) = GetPreviousPeriod(from, to);

        var reservationsTask = _unitOfWork.Reservations.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, from, to, ct);
        var leadsTask = _unitOfWork.Leads.GetPagedByBusinessIdAsync(businessId, 1, 1, null, ct);
        var convTask = _unitOfWork.Conversations.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, null, ct);
        var revenueTask = _unitOfWork.PaymentTransactions.GetTotalRevenueByBusinessIdAsync(
            businessId, from, to, ct);
        var prevRevenueTask = _unitOfWork.PaymentTransactions.GetTotalRevenueByBusinessIdAsync(
            businessId, prevFrom, prevTo, ct);
        var prevReservationsTask = _unitOfWork.Reservations.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, prevFrom, prevTo, ct);
        var prevLeadsTask = _unitOfWork.Leads.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, ct);

        await Task.WhenAll(
            reservationsTask, leadsTask, convTask, revenueTask,
            prevRevenueTask, prevReservationsTask, prevLeadsTask);

        var totalReservations = (await reservationsTask).TotalCount;
        var totalLeads = (await leadsTask).TotalCount;
        var totalConversations = (await convTask).TotalCount;
        var totalRevenue = await revenueTask;
        var prevRevenue = await prevRevenueTask;
        var prevReservations = (await prevReservationsTask).TotalCount;
        var prevLeads = (await prevLeadsTask).TotalCount;

        var conversionRate = totalLeads > 0 ? (double)totalReservations / totalLeads * 100 : 0;
        var revenueGrowth = prevRevenue > 0 ? (double)((totalRevenue - prevRevenue) / prevRevenue * 100) : 0;
        var reservationGrowth = prevReservations > 0 ? (double)(totalReservations - prevReservations) / prevReservations * 100 : 0;
        var leadGrowth = prevLeads > 0 ? (double)(totalLeads - prevLeads) / prevLeads * 100 : 0;

        return new DashboardStatsDto(
            TotalRevenue: totalRevenue,
            TotalReservations: totalReservations,
            TotalLeads: totalLeads,
            TotalConversations: totalConversations,
            ConversionRate: Math.Round(conversionRate, 2),
            RevenueGrowth: Math.Round(revenueGrowth, 2),
            ReservationGrowth: Math.Round(reservationGrowth, 2),
            LeadGrowth: Math.Round(leadGrowth, 2));
    }

    public async Task<IReadOnlyList<ChartDataPointDto>> GetRevenueChartAsync(
        Guid tenantId, Guid businessId, string period, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (from, to) = ParsePeriod(period);
        var groupByMonth = period.Contains("m") || period == "30d" || period == "90d";

        var data = await _unitOfWork.PaymentTransactions.GetRevenueChartDataAsync(
            businessId, from, to, groupByMonth, ct);

        return data.Select(x => new ChartDataPointDto(x.Date, x.Amount)).ToList();
    }

    public async Task<IReadOnlyList<OverviewDataPointDto>> GetOverviewChartAsync(
        Guid tenantId, Guid businessId, string period, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (from, to) = ParsePeriod(period);
        var groupByMonth = period.Contains("m") || period == "30d" || period == "90d";

        var revenueData = await _unitOfWork.PaymentTransactions.GetRevenueChartDataAsync(
            businessId, from, to, groupByMonth, ct);

        var reservations = (await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId, from, to)).ToList();
        var reservationsByDay = reservations
            .GroupBy(r => r.ReservationDateTime.Date)
            .ToDictionary(g => g.Key, g => g.Count());
        var reservationsByMonth = reservations
            .GroupBy(r => (r.ReservationDateTime.Year, r.ReservationDateTime.Month))
            .ToDictionary(g => g.Key, g => g.Count());

        if (groupByMonth)
        {
            return revenueData.Select(x =>
            {
                if (!DateTime.TryParse(x.Date, out var parsed))
                    return new OverviewDataPointDto(x.Date, x.Amount, 0);
                var key = (parsed.Year, parsed.Month);
                var count = reservationsByMonth.TryGetValue(key, out var c) ? c : 0;
                return new OverviewDataPointDto(x.Date, x.Amount, count);
            }).ToList();
        }

        var dateSet = revenueData
            .Select(x => DateTime.TryParse(x.Date, out var d) ? d.Date : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToHashSet();
        foreach (var day in reservationsByDay.Keys.Where(d => !dateSet.Contains(d)))
            dateSet.Add(day);

        return dateSet.OrderBy(d => d).Select(date =>
        {
            var dateStr = date.ToString("yyyy-MM-dd");
            var match = revenueData.FirstOrDefault(x =>
                DateTime.TryParse(x.Date, out var rd) && rd.Date == date);
            var revenue = match.Amount;
            var count = reservationsByDay.TryGetValue(date, out var dc) ? dc : 0;
            return new OverviewDataPointDto(dateStr, revenue, count);
        }).ToList();
    }

    public async Task<IReadOnlyList<TopServiceDto>> GetTopServicesAsync(
        Guid tenantId, Guid businessId, int limit, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (from, to) = ParsePeriod("30d");
        var items = await _unitOfWork.Reservations.GetTopServicesByBusinessIdAsync(
            businessId, limit, from, to, ct);

        return items.Select(x => new TopServiceDto(
            x.ServiceId, x.ServiceName, x.TotalReservations, x.Revenue)).ToList();
    }

    public async Task<IReadOnlyList<RecentReservationDto>> GetRecentReservationsAsync(
        Guid tenantId, Guid businessId, int limit, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var reservations = await _unitOfWork.Reservations.GetRecentByBusinessIdAsync(businessId, limit, ct);

        return reservations.Select(r => new RecentReservationDto(
            r.ReservationId,
            r.ReservationDateTime,
            r.Service?.ServiceName ?? "",
            r.Conversation?.CustomerName,
            r.Status.ToString(),
            r.Service?.Price ?? 0)).ToList();
    }

    private static (DateTime From, DateTime To) ParsePeriod(string period)
    {
        var now = DateTime.UtcNow.Date;
        return period switch
        {
            "7d" or "7" => (now.AddDays(-7), now.AddDays(1)),
            "30d" or "30" => (now.AddDays(-30), now.AddDays(1)),
            "90d" or "90" => (now.AddDays(-90), now.AddDays(1)),
            "daily" => (now.AddDays(-7), now.AddDays(1)),
            "monthly" => (now.AddMonths(-3), now.AddDays(1)),
            _ => (now.AddDays(-30), now.AddDays(1))
        };
    }

    private static (DateTime From, DateTime To) GetPreviousPeriod(DateTime from, DateTime to)
    {
        var span = to - from;
        return (from - span, from);
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
