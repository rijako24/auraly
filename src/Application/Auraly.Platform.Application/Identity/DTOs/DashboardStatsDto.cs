namespace Auraly.Platform.Application.Identity.DTOs;

public record DashboardStatsDto(
    decimal TotalRevenue,
    int TotalReservations,
    int TotalLeads,
    int TotalConversations,
    double ConversionRate,
    double RevenueGrowth,
    double ReservationGrowth,
    double LeadGrowth);
