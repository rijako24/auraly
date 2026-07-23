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

export function useDashboardStats(period?: string) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.stats(businessId, period),
    queryFn: () => dashboardApi.getStats(businessId!, { period }),
    enabled: !!businessId,
  });
}

export function useRevenueChart(period: "daily" | "monthly") {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.revenueChart(businessId, period),
    queryFn: () => dashboardApi.getRevenueChart(businessId!, period),
    enabled: !!businessId,
  });
}

export function useOverviewChart(period?: string) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.overviewChart(businessId, period),
    queryFn: () => dashboardApi.getOverviewChart(businessId!, period),
    enabled: !!businessId,
  });
}

export function useTopServices(limit?: number) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.topServices(businessId, limit),
    queryFn: () => dashboardApi.getTopServices(businessId!, limit),
    enabled: !!businessId,
  });
}

export function useRecentReservations(limit?: number) {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.recentReservations(businessId, limit),
    queryFn: () => dashboardApi.getRecentReservations(businessId!, limit),
    enabled: !!businessId,
  });
}

export function useBusinessUsage() {
  const businessId = useBusinessContextStore((s) => s.selectedBusinessId);
  return useQuery({
    queryKey: dashboardKeys.usage(businessId),
    queryFn: () => dashboardApi.getUsage(businessId!),
    enabled: !!businessId,
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
