using System.Text.Json;
using Auraly.Platform.Application.Common.Exceptions;
using Auraly.Platform.Application.Billing;
using Auraly.Platform.Application.Identity.DTOs;
using Auraly.Platform.Application.Identity.Interfaces;
using Auraly.Platform.Domain.Entities;
using Auraly.Platform.Domain.Enums;
using Auraly.Platform.Domain.Repositories;

namespace Auraly.Platform.Application.Identity.Services;

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUsageBillingService _usageBilling;

    public DashboardService(IUnitOfWork unitOfWork, IUsageBillingService usageBilling)
    {
        _unitOfWork = unitOfWork;
        _usageBilling = usageBilling;
    }

    public async Task<DashboardStatsDto> GetStatsAsync(
        Guid tenantId, Guid businessId, string? period, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var (from, to) = ParsePeriod(period ?? "30d");
        var (prevFrom, prevTo) = GetPreviousPeriod(from, to);

        var totalReservations = (await _unitOfWork.Reservations.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, from, to, ct)).TotalCount;
        var totalLeads = (await _unitOfWork.Leads.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, ct)).TotalCount;
        var totalConversations = (await _unitOfWork.Conversations.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, null, ct)).TotalCount;
        var totalRevenue = await _unitOfWork.PaymentTransactions.GetTotalRevenueByBusinessIdAsync(
            businessId, from, to, ct);
        if (totalRevenue == 0)
        {
            totalRevenue = await GetEstimatedReservationRevenueAsync(businessId, from, to);
        }

        var prevRevenue = await _unitOfWork.PaymentTransactions.GetTotalRevenueByBusinessIdAsync(
            businessId, prevFrom, prevTo, ct);
        if (prevRevenue == 0)
        {
            prevRevenue = await GetEstimatedReservationRevenueAsync(businessId, prevFrom, prevTo);
        }

        var prevReservations = (await _unitOfWork.Reservations.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, prevFrom, prevTo, ct)).TotalCount;
        var prevLeads = (await _unitOfWork.Leads.GetPagedByBusinessIdAsync(
            businessId, 1, 1, null, ct)).TotalCount;

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
        if (data.Count == 0)
        {
            data = await GetEstimatedReservationRevenueChartAsync(businessId, from, to, groupByMonth);
        }

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
        if (revenueData.Count == 0)
        {
            revenueData = await GetEstimatedReservationRevenueChartAsync(businessId, from, to, groupByMonth);
        }

        var reservations = (await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId, from, to)).ToList();
        var scheduled = reservations.Where(r => r.ReservationDateTime.HasValue).ToList();
        var reservationsByDay = scheduled
            .GroupBy(r => r.ReservationDateTime!.Value.Date)
            .ToDictionary(g => g.Key, g => g.Count());
        var reservationsByMonth = scheduled
            .GroupBy(r => (r.ReservationDateTime!.Value.Year, r.ReservationDateTime!.Value.Month))
            .ToDictionary(g => g.Key, g => g.Count());

        if (groupByMonth)
        {
            var monthSet = revenueData
                .Select(x => DateTime.TryParse(x.Date, out var d) ? (d.Year, d.Month) : ((int Year, int Month)?)null)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .ToHashSet();
            foreach (var month in reservationsByMonth.Keys.Where(m => !monthSet.Contains(m)))
                monthSet.Add(month);

            return monthSet.OrderBy(x => x.Year).ThenBy(x => x.Month).Select(month =>
            {
                var dateStr = $"{month.Year:D4}-{month.Month:D2}-01";
                var match = revenueData.FirstOrDefault(x =>
                    DateTime.TryParse(x.Date, out var rd) &&
                    rd.Year == month.Year &&
                    rd.Month == month.Month);
                var count = reservationsByMonth.TryGetValue(month, out var c) ? c : 0;
                return new OverviewDataPointDto(dateStr, match.Amount, count);
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

    public async Task<BusinessUsageDto?> GetUsageAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);
        var usage = await _usageBilling.GetCurrentUsageAsync(businessId, ct);
        return usage is null
            ? null
            : new BusinessUsageDto(
                usage.PlanName,
                usage.PlanCode,
                usage.CreditsLimit,
                usage.CreditsUsed,
                usage.CreditsUsagePercent,
                usage.PeriodStart,
                usage.PeriodEnd,
                usage.Status);
    }

    public async Task<SubscriptionDetailsDto?> GetSubscriptionAsync(
        Guid tenantId,
        Guid businessId,
        CancellationToken ct)
    {
        await EnsureBusinessBelongsToTenantAsync(tenantId, businessId, ct);

        var usage = await _usageBilling.GetCurrentUsageAsync(businessId, ct);
        if (usage is null)
            return null;

        var subscription = await _unitOfWork.BusinessSubscriptions.GetActiveByBusinessIdAsync(businessId, ct);
        var period = await _unitOfWork.BusinessUsagePeriods.GetCurrentByBusinessIdAsync(
            businessId, DateTime.UtcNow, ct);
        if (subscription is null || period is null)
            return null;

        var entries = await _unitOfWork.UsageLedger.GetByPeriodIdAsync(
            period.BusinessUsagePeriodId, ct);
        var creditsUsed = usage.CreditsUsed;
        var breakdown = entries
            .GroupBy(entry => entry.OperationType)
            .Select(group =>
            {
                var credits = group.Sum(entry => entry.CreditsCharged);
                var percent = creditsUsed == 0
                    ? 0
                    : Math.Round(credits / (decimal)creditsUsed * 100, 2);
                return new UsageBreakdownDto(group.Key, group.Count(), credits, percent);
            })
            .OrderByDescending(item => item.CreditsUsed)
            .ToList();

        var recentUsage = entries
            .Take(50)
            .Select(entry => new UsageActivityDto(
                entry.UsageLedgerEntryId,
                entry.OperationType,
                entry.CreditsCharged,
                entry.CreatedAt))
            .ToList();

        var plan = subscription.SubscriptionPlan;
        return new SubscriptionDetailsDto(
            subscription.BusinessSubscriptionId,
            subscription.PlanNameSnapshot,
            subscription.PlanCodeSnapshot,
            subscription.MonthlyPriceCop,
            subscription.CreatedAt,
            usage.PeriodStart,
            usage.PeriodEnd,
            subscription.AutoRenew,
            subscription.Status,
            usage.Status,
            period.CreditsIncluded,
            period.CreditsExtra,
            usage.CreditsLimit,
            usage.CreditsUsed,
            Math.Max(0, usage.CreditsLimit - usage.CreditsUsed),
            usage.CreditsUsagePercent,
            plan.IncludedAgents,
            plan.IncludedUsers,
            plan.IncludedWorkspaces,
            ParseFeatures(plan.FeaturesJson),
            breakdown,
            recentUsage);
    }

    public async Task<IReadOnlyList<SubscriptionPlanDto>> GetPlansAsync(CancellationToken ct)
    {
        var plans = await _usageBilling.GetPlansAsync(ct);
        return plans.Select(p => new SubscriptionPlanDto(
            p.Code,
            p.Name,
            p.MonthlyPriceCop,
            p.IncludedCredits,
            p.IncludedAgents,
            p.IncludedUsers,
            p.IncludedWorkspaces,
            p.Features)).ToList();
    }

    private static string[] ParseFeatures(string? featuresJson)
    {
        if (string.IsNullOrWhiteSpace(featuresJson))
            return [];

        try
        {
            return JsonSerializer.Deserialize<string[]>(featuresJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static (DateTime From, DateTime To) ParsePeriod(string period)
    {
        var now = DateTime.UtcNow.Date;
        return period switch
        {
            "7d" or "7" => (now.AddDays(-7), now.AddDays(1)),
            "30d" or "30" => (now.AddDays(-30), now.AddDays(1)),
            "90d" or "90" => (now.AddDays(-90), now.AddDays(1)),
            "daily" => (now.AddDays(-30), now.AddDays(1)),
            "monthly" => (now.AddMonths(-3), now.AddDays(1)),
            _ => (now.AddDays(-30), now.AddDays(1))
        };
    }

    private static (DateTime From, DateTime To) GetPreviousPeriod(DateTime from, DateTime to)
    {
        var span = to - from;
        return (from - span, from);
    }

    private async Task<decimal> GetEstimatedReservationRevenueAsync(
        Guid businessId, DateTime from, DateTime to)
    {
        var reservations = await GetRevenueReservationsAsync(businessId, from, to);
        return reservations.Sum(r => r.Service?.Price ?? 0);
    }

    private async Task<IReadOnlyList<(string Date, decimal Amount)>> GetEstimatedReservationRevenueChartAsync(
        Guid businessId, DateTime from, DateTime to, bool groupByMonth)
    {
        var reservations = await GetRevenueReservationsAsync(businessId, from, to);

        if (groupByMonth)
        {
            return reservations
                .GroupBy(r => new
                {
                    Year = r.ReservationDateTime!.Value.Year,
                    Month = r.ReservationDateTime.Value.Month
                })
                .OrderBy(g => g.Key.Year)
                .ThenBy(g => g.Key.Month)
                .Select(g => ($"{g.Key.Year:D4}-{g.Key.Month:D2}-01", g.Sum(r => r.Service?.Price ?? 0)))
                .ToList();
        }

        return reservations
            .GroupBy(r => r.ReservationDateTime!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key.ToString("yyyy-MM-dd"), g.Sum(r => r.Service?.Price ?? 0)))
            .ToList();
    }

    private async Task<IReadOnlyList<Reservation>> GetRevenueReservationsAsync(
        Guid businessId, DateTime from, DateTime to)
    {
        var reservations = await _unitOfWork.Reservations.GetByBusinessIdAndDateRangeAsync(
            businessId, from, to);

        return reservations
            .Where(r => r.ReservationDateTime.HasValue)
            .Where(r => r.Status != ReservationStatus.Cancelled)
            .Where(r => r.Service != null)
            .ToList();
    }

    private async Task EnsureBusinessBelongsToTenantAsync(Guid tenantId, Guid businessId, CancellationToken ct)
    {
        var business = await _unitOfWork.Businesses.GetByIdAsync(businessId)
            ?? throw new NotFoundException(nameof(Business), businessId);
        if (business.TenantId != tenantId)
            throw new NotFoundException(nameof(Business), businessId);
    }
}
