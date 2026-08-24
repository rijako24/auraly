"use client";

import { useQuery } from "@tanstack/react-query";
import { dashboardApi } from "@/services/api";
import { useBusinessContextStore } from "@/stores/business-context-store";

export const dashboardKeys = {
  all: ["dashboard"] as const,
  stats: (businessId: string | null, period?: string) =>
    [...dashboardKeys.all, "stats", businessId, period] as const,
  revenueChart: (businessId: string | null, period: string) =>
    [...dashboardKeys.all, "revenue-chart", businessId, period] as const,
  overviewChart: (businessId: string | null, period?: string) =>
    [...dashboardKeys.all, "overview-chart", businessId, period] as const,
  topServices: (businessId: string | null, limit?: number) =>
    [...dashboardKeys.all, "top-services", businessId, limit] as const,
  recentReservations: (businessId: string | null, limit?: number) =>
    [...dashboardKeys.all, "recent-reservations", businessId, limit] as const,
  usage: (businessId: string | null) =>
    [...dashboardKeys.all, "usage", businessId] as const,
  subscription: (businessId: string | null) =>
    [...dashboardKeys.all, "subscription", businessId] as const,
};

export function useDashboardStats(period?: string, enabled = true) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.stats(businessId, period),
    queryFn: () => dashboardApi.getStats(businessId!, { period }),
    enabled: !!businessId && enabled,
  });
}

export function useRevenueChart(period: "daily" | "monthly", enabled = true) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.revenueChart(businessId, period),
    queryFn: () => dashboardApi.getRevenueChart(businessId!, period),
    enabled: !!businessId && enabled,
  });
}

export function useOverviewChart(period?: string, enabled = true) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.overviewChart(businessId, period),
    queryFn: () => dashboardApi.getOverviewChart(businessId!, period),
    enabled: !!businessId && enabled,
  });
}

export function useTopServices(limit?: number, enabled = true) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.topServices(businessId, limit),
    queryFn: () => dashboardApi.getTopServices(businessId!, limit),
    enabled: !!businessId && enabled,
  });
}

export function useRecentReservations(limit?: number, enabled = true) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.recentReservations(businessId, limit),
    queryFn: () => dashboardApi.getRecentReservations(businessId!, limit),
    enabled: !!businessId && enabled,
  });
}

export function useBusinessUsage(enabled = true) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.usage(businessId),
    queryFn: () => dashboardApi.getUsage(businessId!),
    enabled: !!businessId && enabled,
  });
}

export function useSubscriptionDetails() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.subscription(businessId),
    queryFn: () => dashboardApi.getSubscription(businessId!),
    enabled: !!businessId,
  });
}
