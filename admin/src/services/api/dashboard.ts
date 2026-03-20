import { apiClient } from "./client";
import type {
  DashboardStats,
  ChartDataPoint,
  TopService,
} from "@/types/api";
import type { Reservation } from "@/types/entities";

export interface OverviewDataPoint {
  date: string;
  revenue: number;
  reservations: number;
  [key: string]: string | number | undefined;
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
  getTopServices: (businessId: string, limit?: number) =>
    apiClient.get<TopService[]>(
      "/dashboard/top-services",
      { businessId, ...(limit !== undefined && { limit }) }
    ),
  getRecentReservations: (businessId: string, limit?: number) =>
    apiClient.get<(Reservation & { customerName?: string; serviceName?: string })[]>(
      "/dashboard/recent-reservations",
      { businessId, ...(limit !== undefined && { limit }) }
    ),
};
