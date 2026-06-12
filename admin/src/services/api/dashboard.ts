import { apiClient } from "./client";
import type {
  DashboardStats,
  ChartDataPoint,
  TopService,
  BusinessUsage,
} from "@/types/api";

export interface OverviewDataPoint {
  date: string;
  revenue: number;
  reservations: number;
  [key: string]: string | number | undefined;
}

export interface AnalyticsMetrics {
  conversionRate: number;
  conversionRateChange: number;
  avgBookingValue: number;
  avgBookingValueChange: number;
  repeatCustomerRate: number;
  repeatCustomerRateChange: number;
  avgResponseTime: number;
  avgResponseTimeChange: number;
}

export interface CategoryRevenue {
  name: string;
  value: number;
  color: string;
}

export interface FunnelStage {
  stage: string;
  count: number;
}

export interface TopServiceWithGrowth extends TopService {
  growthPercent: number;
}

export interface RecentReservation {
  reservationId: string;
  reservationDateTime: string | null;
  serviceName: string;
  customerName: string | null;
  status: string;
  price: number;
}

export const dashboardApi = {
  getStats: (businessId: string, params?: { period?: string }) =>
    apiClient.get<DashboardStats>("/dashboard/stats", {
      businessId,
      ...params,
    }),
  getRevenueChart: (businessId: string, period: "daily" | "monthly") =>
    apiClient.get<ChartDataPoint[]>("/dashboard/revenue-chart", { businessId, period }),
  getOverviewChart: (businessId: string, period?: string) =>
    apiClient.get<OverviewDataPoint[]>("/dashboard/overview-chart", {
      businessId,
      ...(period && { period }),
    }),
  getReservationsChart: (businessId: string, period: "daily" | "monthly") =>
    apiClient.get<ChartDataPoint[]>("/dashboard/reservations-chart", { businessId, period }),
  getTopServices: (businessId: string, limit?: number) =>
    apiClient.get<TopService[]>(
      "/dashboard/top-services",
      { businessId, ...(limit !== undefined && { limit }) }
    ),
  getRecentReservations: (businessId: string, limit?: number) =>
    apiClient.get<RecentReservation[]>(
      "/dashboard/recent-reservations",
      { businessId, ...(limit !== undefined && { limit }) }
    ),
  getUsage: (businessId: string) =>
    apiClient.get<BusinessUsage | null>("/dashboard/usage", { businessId }),
  getAnalyticsMetrics: (period?: string) =>
    apiClient.get<AnalyticsMetrics>("/dashboard/analytics-metrics", { period }),
  getCustomerGrowth: (period?: string) =>
    apiClient.get<ChartDataPoint[]>("/dashboard/customer-growth", { period }),
  getReservationsByDay: (period?: string) =>
    apiClient.get<{ day: string; count: number }[]>("/dashboard/reservations-by-day", { period }),
  getRevenueByCategory: (period?: string) =>
    apiClient.get<CategoryRevenue[]>("/dashboard/revenue-by-category", { period }),
  getLeadFunnel: (period?: string) =>
    apiClient.get<FunnelStage[]>("/dashboard/lead-funnel", { period }),
  getTopPerformingServices: (period?: string, limit?: number) =>
    apiClient.get<TopServiceWithGrowth[]>(
      "/dashboard/top-performing-services",
      { period, limit }
    ),
};
